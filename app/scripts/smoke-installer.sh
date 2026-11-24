#!/usr/bin/env bash
# The installer smoke test for Linux and macOS (smoke-installer.ps1 is the Windows one). It installs a built bundle the
# way a user would, runs it, and proves the app really came up:
#
#   smoke-installer.sh deb <file.deb>             apt installs it, then removes it (Debian and Ubuntu, as root or with sudo)
#   smoke-installer.sh appimage <file.AppImage>   runs it with --appimage-extract-and-run (containers have no FUSE)
#   smoke-installer.sh dmg <file.dmg> [arch]      mounts it, copies the .app out, verifies its signature (macOS);
#                                                 arch is arm64 or x86_64, an x86_64 app runs under Rosetta
#
# For each it checks, in this order:
#   1. The packaged engine on its own (smoke-sidecar.mjs --render): si-sidecar starts, crawls a two-page local site,
#      analyses it, then crawls a page whose only link a script adds, with JavaScript rendering on, and stops cleanly.
#      A native library it can't load fails here, and so does a browser it can't start: Playwright's driver, the
#      Chromium it downloads on first use (this needs the internet), or a system library Chromium needs.
#   2. The app, on temporary data, state and profile folders (SI_APP_DATA_DIR, SI_APP_STATE_DIR, SI_APP_PROFILE_DIR),
#      under a virtual display on Linux: the shell's log says its engine is ready on a port; that port answers 401
#      without the token (so it is the engine, and it is guarded); a si-sidecar process runs on the test's state
#      folder; the engine's own log shows the window's page calling it with the token (GET /api/crawls, 200), which
#      is the page loaded in the webview and talking to the engine; the shell is still running after that.
#   3. Stopping the app (Quit on macOS, SIGTERM on Linux, where there's no window manager to close a window with)
#      leaves no si-sidecar behind.
#
# Needs node on PATH (a bare binary will do: smoke-sidecar.mjs uses no packages). On Linux it installs what the test
# itself needs and a container lacks (Xvfb, xwininfo, ps), after the installer's own dependencies have been checked,
# so the test's packages can't hide a missing one. Exits 1 on any failure.
set -uo pipefail

READY_SECONDS=90
PAGE_SECONDS=60
STOP_SECONDS=45
DISPLAY_NUMBER=99
# What an AppImage leaves to the system, because every desktop has it (the AppImage excludelist): a bare container
# doesn't, so the test installs these for the AppImage. Named here so the README's list is this one.
APPIMAGE_SYSTEM_PACKAGES="libfribidi0 libfontconfig1 libfreetype6 libharfbuzz0b libx11-6 libx11-xcb1 libxcb1 libwayland-client0 libgbm1 libdrm2 libgl1 libegl1 libgles2 libexpat1 libgpg-error0 libcom-err2 zlib1g"
# What the browser the engine downloads for JavaScript rendering needs and an AppImage doesn't carry. The .deb
# depends on these (tauri.conf.json); a desktop usually has them.
APPIMAGE_BROWSER_PACKAGES="libnss3 libasound2"

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
kind="${1:-}"
file="${2:-}"
arch="${3:-}"
failed=0
work=""
xvfb_pid=""
mount_point=""
app_pid=""

say() { echo "smoke-installer: $*"; }
fail() {
  echo "smoke-installer: FAIL $*" >&2
  failed=1
}
die() {
  fail "$@"
  exit 1
}
as_root() {
  if [ "$(id -u)" -eq 0 ]; then "$@"; else sudo "$@"; fi
}
# Running, and not a zombie waiting for its parent to collect it.
alive() {
  local state
  state="$(ps -o stat= -p "$1" 2>/dev/null | tr -d ' ')" || return 1
  [ -n "$state" ] && [ "${state#Z}" = "$state" ]
}
# The si-sidecar processes started on this test's state folder, one pid a line.
sidecars_on() {
  local pid
  for pid in $(pgrep -f "si-sidecar.*--state-dir $1" 2>/dev/null); do
    if alive "$pid"; then echo "$pid"; fi
  done
}
wait_until() {
  local seconds="$1"
  shift
  local deadline=$((SECONDS + seconds))
  until "$@"; do
    [ "$SECONDS" -ge "$deadline" ] && return 1
    sleep 0.5
  done
}
apt_install() {
  as_root env DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends "$@" >"$work/apt.log" 2>&1 ||
    { tail -20 "$work/apt.log" >&2; return 1; }
}

cleanup() {
  [ -n "$app_pid" ] && alive "$app_pid" && kill -KILL "$app_pid" 2>/dev/null
  if [ -n "$work" ]; then
    local pid
    for pid in $(sidecars_on "$work" 2>/dev/null); do kill -KILL "$pid" 2>/dev/null; done
  fi
  [ -n "$mount_point" ] && hdiutil detach "$mount_point" -quiet 2>/dev/null
  [ -n "$xvfb_pid" ] && kill "$xvfb_pid" 2>/dev/null
  [ -n "$work" ] && rm -rf "$work"
}
trap cleanup EXIT

# What the test itself needs on Linux. Called only after the installer's own dependencies have been checked.
linux_test_tools() {
  local missing=()
  command -v Xvfb >/dev/null || missing+=(xvfb)
  command -v xwininfo >/dev/null || missing+=(x11-utils)
  command -v pgrep >/dev/null || missing+=(procps)
  if [ ${#missing[@]} -gt 0 ]; then
    say "installing the test's own tools: ${missing[*]}"
    apt_install "${missing[@]}" || die "couldn't install ${missing[*]}."
  fi
  if [ -z "${DISPLAY:-}" ]; then
    Xvfb ":$DISPLAY_NUMBER" -screen 0 1280x800x24 -nolisten tcp >"$work/xvfb.log" 2>&1 &
    xvfb_pid=$!
    wait_until 10 test -S "/tmp/.X11-unix/X$DISPLAY_NUMBER" || die "Xvfb didn't start: $(cat "$work/xvfb.log")"
    export DISPLAY=":$DISPLAY_NUMBER"
  fi
}

engine_port() {
  sed -n 's/.*The engine is ready on port \([0-9][0-9]*\).*/\1/p' "$1"/profile/logs/*.log 2>/dev/null | head -1
}
# True once there's a port, and also once the app has gone: there's nothing more to wait for then.
engine_is_ready() { [ -n "$(engine_port "$1")" ] || ! alive "$app_pid"; }
page_called_engine() {
  grep -q "Request finished HTTP/1.1 GET http://127.0.0.1:$2/api/crawls - 200" "$1"/state/logs/*.log 2>/dev/null
}
none_left() { [ -z "$(sidecars_on "$1")" ]; }
not_alive() { ! alive "$1"; }

# Step 2 and 3: the app itself. $1 is how it is stopped (quit or term), the rest is the command that starts it.
run_app() {
  local stop="$1"
  shift
  local run="$work/run"
  mkdir -p "$run"
  SI_APP_DATA_DIR="$run/data" SI_APP_STATE_DIR="$run/state" SI_APP_PROFILE_DIR="$run/profile" \
    "$@" >"$run/app.out" 2>&1 &
  app_pid=$!
  say "started $* (pid $app_pid)"

  wait_until "$READY_SECONDS" engine_is_ready "$run"
  if [ -z "$(engine_port "$run")" ]; then
    if alive "$app_pid"; then
      fail "the shell's log never said its engine was ready (${READY_SECONDS} s)."
    else
      fail "the app stopped before its engine was ready."
    fi
    echo "--- the app's output" >&2
    tail -30 "$run/app.out" >&2
    echo "--- the shell's log" >&2
    cat "$run"/profile/logs/*.log 2>/dev/null | tail -30 >&2
    return
  fi
  local port
  port="$(engine_port "$run")"
  say "the shell's log: the engine is ready on port $port"

  local status
  status="$(node -e "fetch('http://127.0.0.1:$port/api/health').then((r) => console.log(r.status), () => console.log(0))")"
  if [ "$status" = 401 ]; then
    say "port $port answers 401 without the token: the engine, guarded"
  else
    fail "GET /api/health on port $port without the token answered $status, not 401."
  fi

  local sidecars shell_pid=""
  sidecars="$(sidecars_on "$run/state")"
  if [ -z "$sidecars" ]; then
    fail "no si-sidecar process runs on $run/state."
  else
    # The shell is the sidecar's parent, whatever started it (an AppImage's runtime starts it as a child).
    shell_pid="$(ps -o ppid= -p "$(echo "$sidecars" | head -1)" | tr -d ' ')"
    say "si-sidecar runs (pid $(echo "$sidecars" | tr '\n' ' ')), started by pid $shell_pid"
  fi

  if wait_until "$PAGE_SECONDS" page_called_engine "$run" "$port"; then
    say "the window's page called the engine with the token (GET /api/crawls, 200)"
  else
    fail "the engine's log shows no call from the window's page (${PAGE_SECONDS} s): the webview didn't load the app."
    echo "--- the app's output" >&2
    tail -30 "$run/app.out" >&2
  fi
  if command -v xwininfo >/dev/null && [ -n "${DISPLAY:-}" ]; then
    if xwininfo -root -tree 2>/dev/null | grep -q '"shouting-iguana"'; then
      say "the display has the app's window"
    else
      fail "the display has no window of the app."
    fi
  fi
  if [ -n "$shell_pid" ] && alive "$shell_pid"; then
    say "the shell is still running"
  else
    fail "the shell has stopped by itself."
    tail -30 "$run/app.out" >&2
  fi

  # Step 3: stop it, and nothing of the engine stays.
  if [ "$stop" = quit ]; then
    # The app's own Quit, as the menu sends it. Not waited for: an Apple event that isn't answered blocks for minutes.
    osascript -e 'tell application id "uk.co.aptitude.shoutingiguana" to quit' >/dev/null 2>&1 &
    if wait_until 20 not_alive "${shell_pid:-$app_pid}"; then
      say "the app quit when asked to"
    else
      say "the app didn't quit when asked to (osascript): sending SIGTERM"
      kill -TERM "${shell_pid:-$app_pid}" 2>/dev/null
    fi
  else
    kill -TERM "${shell_pid:-$app_pid}" 2>/dev/null
  fi
  if ! wait_until "$STOP_SECONDS" not_alive "${shell_pid:-$app_pid}"; then
    fail "the app was still running ${STOP_SECONDS} s after it was asked to stop."
    kill -KILL "${shell_pid:-$app_pid}" 2>/dev/null
  fi
  if wait_until "$STOP_SECONDS" none_left "$run/state"; then
    say "the app has stopped, and no si-sidecar is left"
  else
    fail "a si-sidecar was still running ${STOP_SECONDS} s after the app stopped: $(sidecars_on "$run/state" | tr '\n' ' ')"
  fi
  wait "$app_pid" 2>/dev/null
  app_pid=""
}

sidecar_smoke() {
  node "$here/smoke-sidecar.mjs" --render --sidecar "$1" || fail "the packaged engine failed on its own ($1)."
}

smoke_deb() {
  command -v apt-get >/dev/null || die "a .deb needs apt-get."
  say "$(. /etc/os-release && echo "$PRETTY_NAME"), glibc $(ldd --version | head -1 | awk '{print $NF}')"
  as_root env DEBIAN_FRONTEND=noninteractive apt-get update -qq >/dev/null 2>&1
  # A copy apt's own user can read, named as dpkg names packages.
  cp "$file" "$work/shouting-iguana.deb"
  chmod a+rx "$work"
  chmod a+r "$work/shouting-iguana.deb"
  say "depends on: $(dpkg-deb -f "$work/shouting-iguana.deb" Depends)"
  apt_install "$work/shouting-iguana.deb" || die "apt couldn't install $file."
  local shell=/usr/bin/shouting-iguana sidecar=/usr/bin/si-sidecar
  [ -x "$shell" ] && [ -x "$sidecar" ] || die "the package didn't install $shell and $sidecar."
  # Before the test installs anything of its own: what the package's dependencies brought is all there is.
  local missing
  missing="$(ldd "$shell" | grep '=> not found' || true)"
  [ -z "$missing" ] || fail "the shell can't find libraries the package doesn't depend on: $missing"
  "$sidecar" --help >/dev/null 2>"$work/sidecar.err" || fail "si-sidecar --help failed: $(cat "$work/sidecar.err")"
  grep -q '^MimeType=application/x-shouting-iguana-crawl' /usr/share/applications/*.desktop 2>/dev/null ||
    fail "no desktop entry names the .sicrawl type."
  # Opening a .sicrawl file: the entry hands the file to the app, and the system knows the type by its extension.
  grep -q '^Exec=shouting-iguana %F' /usr/share/applications/*.desktop 2>/dev/null ||
    fail "the desktop entry doesn't pass the opened file to the app (Exec has no %F)."
  if [ -f /usr/share/mime/globs ]; then
    grep -Fxq 'application/x-shouting-iguana-crawl:*.sicrawl' /usr/share/mime/globs ||
      fail "the system doesn't know .sicrawl files as application/x-shouting-iguana-crawl."
  else
    fail "there is no MIME database (/usr/share/mime/globs) to know .sicrawl files by."
  fi
  say "installed, with $(dpkg -s libwebkit2gtk-4.1-0 2>/dev/null | sed -n 's/^Version: /WebKitGTK /p')"

  sidecar_smoke "$sidecar"
  linux_test_tools
  run_app term "$shell"

  as_root env DEBIAN_FRONTEND=noninteractive apt-get remove -y -qq shouting-iguana >/dev/null 2>&1 ||
    fail "apt couldn't remove the package."
  if [ -e "$shell" ] || [ -e "$sidecar" ]; then fail "removing the package left $shell or $sidecar."; else say "removed"; fi
}

smoke_appimage() {
  say "$(. /etc/os-release && echo "$PRETTY_NAME"), glibc $(ldd --version | head -1 | awk '{print $NF}')"
  local image="$work/shouting-iguana.AppImage"
  cp "$file" "$image"
  chmod +x "$image"
  if command -v apt-get >/dev/null && command -v dpkg >/dev/null; then
    local absent=() package
    for package in $APPIMAGE_SYSTEM_PACKAGES $APPIMAGE_BROWSER_PACKAGES; do
      dpkg -s "$package" >/dev/null 2>&1 || absent+=("$package")
    done
    if [ ${#absent[@]} -gt 0 ]; then
      say "installing the system libraries an AppImage expects of a desktop: ${absent[*]}"
      as_root env DEBIAN_FRONTEND=noninteractive apt-get update -qq >/dev/null 2>&1
      apt_install "${absent[@]}" || die "couldn't install ${absent[*]}."
    fi
  fi
  # The engine inside it, on its own: unpacked, as --appimage-extract-and-run unpacks it.
  (cd "$work" && "$image" --appimage-extract >/dev/null 2>&1) || die "the AppImage couldn't unpack itself."
  local root="$work/squashfs-root"
  [ -x "$root/usr/bin/si-sidecar" ] || die "the AppImage has no usr/bin/si-sidecar."
  local missing
  missing="$(LD_LIBRARY_PATH="$root/usr/lib" ldd "$root/usr/bin/shouting-iguana" | grep '=> not found' || true)"
  [ -z "$missing" ] || fail "the shell can't find libraries the AppImage doesn't carry: $missing"
  # With what the AppImage's own start script sets (its GTK hook), as the shell and so its engine get it.
  (
    export APPDIR="$root"
    for hook in "$root"/apprun-hooks/*.sh; do
      # shellcheck disable=SC1090
      [ -f "$hook" ] && . "$hook"
    done
    node "$here/smoke-sidecar.mjs" --render --sidecar "$root/usr/bin/si-sidecar"
  ) || fail "the packaged engine failed on its own ($root/usr/bin/si-sidecar)."
  rm -rf "$root"

  linux_test_tools
  run_app term "$image" --appimage-extract-and-run
}

smoke_dmg() {
  [ "$(uname -s)" = Darwin ] || die "a .dmg needs macOS."
  say "macOS $(sw_vers -productVersion) on $(uname -m)"
  arch="${arch:-$(uname -m)}"
  if [ "$arch" = x86_64 ] && [ "$(uname -m)" = arm64 ] && ! arch -x86_64 /usr/bin/true 2>/dev/null; then
    say "installing Rosetta to run the x86_64 app"
    /usr/sbin/softwareupdate --install-rosetta --agree-to-license >/dev/null 2>&1
    arch -x86_64 /usr/bin/true 2>/dev/null || die "Rosetta isn't installed, so the x86_64 app can't run here."
  fi

  mount_point="$work/mount"
  mkdir -p "$mount_point"
  hdiutil attach "$file" -nobrowse -readonly -noautoopen -mountpoint "$mount_point" >/dev/null ||
    die "the disk image couldn't be mounted."
  local source
  source="$(ls -d "$mount_point"/*.app 2>/dev/null | head -1)"
  [ -n "$source" ] || die "the disk image holds no .app."
  [ -L "$mount_point/Applications" ] || say "note: the disk image has no Applications shortcut to drag the app to."
  # As dragging it to Applications copies it.
  local app
  app="$work/Applications/$(basename "$source")"
  mkdir -p "$work/Applications"
  ditto "$source" "$app" || die "the app couldn't be copied out of the disk image."
  hdiutil detach "$mount_point" -quiet
  mount_point=""

  local shell="$app/Contents/MacOS/shouting-iguana" sidecar="$app/Contents/MacOS/si-sidecar"
  [ -x "$shell" ] && [ -x "$sidecar" ] || die "the app has no shouting-iguana and si-sidecar in Contents/MacOS."
  local binary
  for binary in "$shell" "$sidecar"; do
    if [ "$(lipo -archs "$binary" 2>/dev/null)" = "$arch" ]; then
      say "$(basename "$binary") is $arch"
    else
      fail "$(basename "$binary") is $(lipo -archs "$binary" 2>&1), not $arch."
    fi
  done

  # Signed, the sidecar too: an app with no signature at all is the one macOS calls damaged on Apple silicon.
  if codesign --verify --deep --strict --verbose=2 "$app" >"$work/codesign.log" 2>&1; then
    say "codesign verifies the app (deep, strict)"
  else
    fail "codesign doesn't verify the app: $(cat "$work/codesign.log")"
  fi
  for binary in "$app" "$sidecar"; do
    codesign --display --verbose=2 "$binary" 2>&1 | grep -E '^(Identifier|Signature|CodeDirectory|TeamIdentifier)' |
      sed "s|^|smoke-installer: $(basename "$binary"): |"
  done
  codesign --verify --strict "$sidecar" >/dev/null 2>&1 || fail "the sidecar inside the app isn't signed."
  # As a browser download is marked. Gatekeeper's verdict is printed, not judged: an un-notarised app is rejected
  # until the user allows it. Then the mark is taken off the way the README's fallback does it.
  xattr -w com.apple.quarantine "0083;$(printf '%x' "$(date +%s)");Safari;" "$app" 2>/dev/null
  say "Gatekeeper on the app as downloaded: $(spctl --assess --type execute --verbose=2 "$app" 2>&1 | tr '\n' ' ')"
  xattr -dr com.apple.quarantine "$app" || fail "xattr -dr com.apple.quarantine failed."

  sidecar_smoke "$sidecar"
  run_app quit "$shell"
}

command -v node >/dev/null || die "node isn't on PATH."
[ -n "$file" ] && [ -f "$file" ] || die "usage: smoke-installer.sh deb|appimage|dmg <file> [arch]"
work="$(mktemp -d "${TMPDIR:-/tmp}/si-smoke-installer.XXXXXX")"
say "$kind $file"
case "$kind" in
  deb) smoke_deb ;;
  appimage) smoke_appimage ;;
  dmg) smoke_dmg ;;
  *) die "unknown kind '$kind': deb, appimage or dmg." ;;
esac

if [ "$failed" -ne 0 ]; then
  echo "smoke-installer: FAIL" >&2
  exit 1
fi
say "PASS"
