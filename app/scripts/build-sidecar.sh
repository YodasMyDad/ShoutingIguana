#!/usr/bin/env bash
# Builds the engine's sidecar for the Tauri shell. Twin of build-sidecar.ps1; `npm run build:sidecar` runs whichever
# fits the platform.
#
# Publishes engine/src/ShoutingIguana.Sidecar as one self-contained file for the target's .NET runtime, so users need
# no .NET installed, into app/.sidecar-build (its own artefacts folder, so it never collides with other builds of the
# engine). Then copies si-sidecar to src-tauri/binaries/si-sidecar-<target triple>(.exe), where Tauri's externalBin
# expects it, and signs a Windows build there with scripts/sign.mjs (which skips, saying so, without a certificate).
# Tauri copies it next to the app, without the triple, in dev, build and bundles. IIS's in-process module, which a
# self-hosted Kestrel never loads, is left out.
#
# Options:
#   --engine-root <folder>    The engine folder. Defaults to the repo's engine/, found from this script's location.
#   --configuration <name>    The .NET build configuration. Release by default.
#   --target <triple>         A Rust target triple. Defaults to the host's, from `rustc -vV`.
#   --no-ready-to-run         Leave out ahead-of-time compilation (PublishReadyToRun), on by default because it
#                             starts the sidecar about a quarter faster (build-sidecar.ps1 has the numbers).
set -euo pipefail

fail() {
  echo "build-sidecar: $*" >&2
  exit 1
}

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
app_root="$(dirname "$here")"
engine_root="$(dirname "$app_root")/engine"
configuration=Release
target=""
ready_to_run=true

while [ $# -gt 0 ]; do
  case "$1" in
    --engine-root | --configuration | --target)
      [ $# -ge 2 ] || fail "$1 needs a value."
      case "$1" in
        --engine-root) engine_root="$2" ;;
        --configuration) configuration="$2" ;;
        --target) target="$2" ;;
      esac
      shift 2
      ;;
    --engine-root=*) engine_root="${1#*=}"; shift ;;
    --configuration=*) configuration="${1#*=}"; shift ;;
    --target=*) target="${1#*=}"; shift ;;
    --no-ready-to-run) ready_to_run=false; shift ;;
    -h | --help) sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) fail "unknown option '$1'. Use --engine-root, --configuration, --target or --no-ready-to-run." ;;
  esac
done

engine_root="$(cd "$engine_root" 2>/dev/null && pwd)" || fail "the engine folder doesn't exist. Pass it with --engine-root."
project="$engine_root/src/ShoutingIguana.Sidecar/ShoutingIguana.Sidecar.csproj"
[ -f "$project" ] || fail "there is no sidecar project at $project. Pass the engine folder with --engine-root."

if [ -z "$target" ]; then
  rustc_bin="$(command -v rustc || true)"
  [ -n "$rustc_bin" ] || rustc_bin="${CARGO_HOME:-$HOME/.cargo}/bin/rustc"
  [ -x "$rustc_bin" ] || fail "rustc wasn't found, so the host's target triple is unknown: install Rust, or pass --target."
  target="$("$rustc_bin" -vV | sed -n 's/^host: //p')"
  [ -n "$target" ] || fail "\`rustc -vV\` didn't name the host's target triple: pass --target."
fi

# Rust target triples and the .NET runtime identifiers that build for them.
case "$target" in
  x86_64-pc-windows-msvc) runtime=win-x64 ;;
  aarch64-pc-windows-msvc) runtime=win-arm64 ;;
  x86_64-apple-darwin) runtime=osx-x64 ;;
  aarch64-apple-darwin) runtime=osx-arm64 ;;
  x86_64-unknown-linux-gnu) runtime=linux-x64 ;;
  aarch64-unknown-linux-gnu) runtime=linux-arm64 ;;
  *) fail "there is no .NET runtime for the target $target." ;;
esac
case "$target" in
  *-windows-*) extension=.exe ;;
  *) extension= ;;
esac

command -v dotnet >/dev/null || fail "dotnet wasn't found: install the .NET SDK the engine targets (see engine/README.md)."

artifacts="$app_root/.sidecar-build"
echo "build-sidecar: publishing $runtime ($configuration) from $engine_root"
dotnet publish "$project" \
  -c "$configuration" \
  -r "$runtime" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  "-p:CustomAfterMicrosoftCommonTargets=$here/sidecar-publish.targets" \
  -p:DebugType=none \
  "-p:PublishReadyToRun=$ready_to_run" \
  --artifacts-path "$artifacts" \
  --nologo || fail "dotnet publish failed with exit code $?."

# The artefacts layout puts a publish in publish/<project>/<configuration>_<runtime>.
pivot="$(printf '%s' "$configuration" | tr '[:upper:]' '[:lower:]')_$runtime"
published="$artifacts/publish/ShoutingIguana.Sidecar/$pivot/si-sidecar$extension"
[ -f "$published" ] || fail "dotnet publish succeeded but $published isn't there."
binaries="$app_root/src-tauri/binaries"
mkdir -p "$binaries"
destination="$binaries/si-sidecar-$target$extension"
cp "$published" "$destination"
chmod +x "$destination"
# Signed as soon as it exists, so the release never depends on the bundler covering externalBin.
if [ "$extension" = .exe ]; then
  command -v node >/dev/null || fail "node wasn't found, which signs the sidecar (scripts/sign.mjs)."
  node "$here/sign.mjs" "$destination" || fail "signing the sidecar failed."
fi
ahead_of_time=""
[ "$ready_to_run" = true ] && ahead_of_time=", ReadyToRun"
echo "build-sidecar: $destination ($runtime, $configuration$ahead_of_time)"
