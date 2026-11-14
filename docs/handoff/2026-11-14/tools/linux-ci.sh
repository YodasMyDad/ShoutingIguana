# Runs app.yml's shell-linux job (clippy + cargo test of app/src-tauri) in an ubuntu:24.04 container, on a committed
# branch. From Git Bash, with Docker running:
#   git archive --format=tar -o <dir>/r5-shell.tar <branch>      (the script reads /in/r5-shell.tar)
#   MSYS_NO_PATHCONV=1 docker run --rm --cpus 8 --memory 8g -v "$(cygpath -w <dir>):/in:ro" \
#     -v si-cargo-registry:/root/.cargo/registry ubuntu:24.04 bash /in/linux-ci.sh
# (MSYS_NO_PATHCONV stops Git Bash rewriting /in/... into a Windows path.) Copy this file into <dir> first.
#!/usr/bin/env bash
# Mirrors .github/workflows/app.yml's shell-linux job on ubuntu:24.04 (ubuntu-latest).
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq --no-install-recommends libwebkit2gtk-4.1-dev libayatana-appindicator3-dev librsvg2-dev libxdo-dev libssl-dev libdbus-1-dev pkg-config build-essential curl ca-certificates git file >/dev/null
echo "== packages installed"
mkdir -p /work && tar -xf /in/r5-shell.tar -C /work
cd /work/app/src-tauri
curl -sSf https://sh.rustup.rs | sh -s -- -y --profile minimal --default-toolchain none >/dev/null 2>&1
. "$HOME/.cargo/env"
rustup toolchain install 2>&1 | tail -1
rustc --version
install -D -m 755 /dev/null binaries/si-sidecar-x86_64-unknown-linux-gnu
set +e +o pipefail
echo "== clippy"
cargo clippy --all-targets --locked -- -D warnings 2>&1 | tail -25
echo "clippy exit ${PIPESTATUS[0]}"
echo "== test"
cargo test --locked 2>&1 | grep -E "^test result|FAILED|panicked|error(\[|:)|failures:" | head -40
echo "test exit ${PIPESTATUS[0]}"
