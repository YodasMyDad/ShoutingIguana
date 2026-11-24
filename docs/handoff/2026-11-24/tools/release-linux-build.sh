#!/usr/bin/env bash
# Builds the Linux release bundles (.deb and .AppImage) as app-release.yml's linux-x64 leg does, in an ubuntu:22.04
# container, from a committed branch. From Git Bash on Windows, with Docker running, in the repo:
#
#   work=<an empty folder>; mkdir -p "$work/in" "$work/out"
#   git archive --format=tar -o "$work/in/src.tar" <branch>
#   cp docs/handoff/2026-11-24/tools/release-linux-build.sh "$work/in/"
#   MSYS_NO_PATHCONV=1 docker run --rm --cpus 12 --memory 16g \
#     -v "$(cygpath -w "$work/in"):/in:ro" -v "$(cygpath -w "$work/out"):/out" \
#     -v si-rel-cache22:/cache -v si-rel-cargo:/root/.cargo -v si-rel-rustup:/root/.rustup \
#     -v si-rel-npm:/root/.npm -v si-rel-nuget:/root/.nuget -v si-rel-opt:/opt \
#     ubuntu:22.04 bash /in/release-linux-build.sh
#
# The bundles land in $work/out/bundle. The named volumes keep Node, the .NET SDK, Rust, the crates and the build
# folder between runs: the first run takes about ten minutes, the next about five. si-rel-opt also gives
# release-linux-smoke.sh its node binary. Remove them with `docker volume rm` when done.
#
# With the updater's signing key in the container's environment (add `-e TAURI_SIGNING_PRIVATE_KEY -e
# TAURI_SIGNING_PRIVATE_KEY_PASSWORD` to docker run, with both set in the shell that runs it, so neither is on a
# command line), the build is the release's: the updater's artifacts are on (tauri.updater.conf.json), the .sig files
# land beside the bundles, and the AppImage's signature is checked as the app checks it.
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
# The workflow's "System packages" list, plus what a runner already has.
apt-get install -y -qq --no-install-recommends libwebkit2gtk-4.1-dev libayatana-appindicator3-dev librsvg2-dev libxdo-dev libssl-dev libdbus-1-dev pkg-config build-essential patchelf file wget curl ca-certificates git xz-utils >/dev/null
echo "== packages installed"
if [ ! -x /opt/node/bin/node ]; then
  name="$(curl -fsSL https://nodejs.org/dist/latest-v24.x/SHASUMS256.txt | awk '/linux-x64.tar.xz/{print $2}')"
  curl -fsSL "https://nodejs.org/dist/latest-v24.x/$name" -o /tmp/node.tar.xz
  mkdir -p /opt/node && tar -xJf /tmp/node.tar.xz -C /opt/node --strip-components=1
fi
export PATH=/opt/node/bin:$PATH
node --version
if [ ! -x /opt/dotnet/dotnet ]; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /opt/dotnet >/dev/null
fi
export PATH=/opt/dotnet:$PATH DOTNET_ROOT=/opt/dotnet DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet --version
rm -rf /work/src && mkdir -p /work/src && tar -xf /in/src.tar -C /work/src
mkdir -p /cache/target /work/src/app/src-tauri && ln -sfn /cache/target /work/src/app/src-tauri/target
cd /work/src/app/src-tauri
if [ ! -x "$HOME/.cargo/bin/rustup" ]; then
  curl -sSf https://sh.rustup.rs | sh -s -- -y --profile minimal --default-toolchain none >/dev/null 2>&1
fi
. "$HOME/.cargo/env"
rustup toolchain install 2>&1 | tail -1
# A runner has a default toolchain; the UI build's licence list runs rustc outside src-tauri and needs one.
rustup default "$(rustup show active-toolchain | cut -d" " -f1)"
rustup target add x86_64-unknown-linux-gnu
cd /work/src/app
npm ci --no-audit --no-fund 2>&1 | tail -3
echo "== sidecar"
npm run build:sidecar -- --target x86_64-unknown-linux-gnu 2>&1 | tail -5
echo "== tauri build"
export APPIMAGE_EXTRACT_AND_RUN=1
root=src-tauri/target/x86_64-unknown-linux-gnu/release/bundle
rm -rf "$root" /out/bundle && mkdir -p /out/bundle
if [ -n "${TAURI_SIGNING_PRIVATE_KEY:-}" ]; then
  npm run tauri build -- --target x86_64-unknown-linux-gnu --bundles deb,appimage --config src-tauri/tauri.updater.conf.json 2>&1 | tail -40
  cp "$root"/deb/*.deb.sig "$root"/appimage/*.AppImage.sig /out/bundle/
  version="$(node -p "require('./src-tauri/tauri.conf.json').version")"
  node scripts/updater-manifest.mjs verify "$(ls "$root"/appimage/*.AppImage)" --version "$version"
else
  npm run tauri build -- --target x86_64-unknown-linux-gnu --bundles deb,appimage 2>&1 | tail -40
fi
cp "$root"/deb/*.deb "$root"/appimage/*.AppImage /out/bundle/
ls -la /out/bundle
echo "== BUILD DONE"
