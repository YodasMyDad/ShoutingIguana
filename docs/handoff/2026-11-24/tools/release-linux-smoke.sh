#!/usr/bin/env bash
# Runs app/scripts/smoke-installer.sh on a Linux bundle in a fresh container, as app-release.yml's "Installer smoke
# test (Linux)" step does. From Git Bash on Windows, in the repo, after release-linux-build.sh:
#
#   bash docs/handoff/2026-11-24/tools/release-linux-smoke.sh <work>/out/bundle ubuntu:22.04 deb
#   bash docs/handoff/2026-11-24/tools/release-linux-smoke.sh <work>/out/bundle debian:13 appimage
#
# The container gets the bundles, the scripts and a node binary (the si-rel-opt volume the build filled), all
# read-only, and nothing else: apt installs the .deb with only its own dependencies. The rendered crawl downloads
# Chromium in each container, so it needs the internet. Seen passing on 2026-11-24 for
# both bundles on ubuntu:22.04, ubuntu:24.04, ubuntu:26.04, debian:12 and debian:13.
set -euo pipefail
bundles="$1"
image="$2"
kind="$3"
case "$kind" in
  deb) pattern='*.deb' ;;
  appimage) pattern='*.AppImage' ;;
  *) echo "kind is deb or appimage" >&2; exit 2 ;;
esac
scripts="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../../app/scripts" && pwd)"
MSYS_NO_PATHCONV=1 docker run --rm --init \
  -v "$(cygpath -w "$bundles"):/installers:ro" -v "$(cygpath -w "$scripts"):/scripts:ro" -v si-rel-opt:/opt:ro \
  -e PATH=/opt/node/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin \
  "$image" bash -c "bash /scripts/smoke-installer.sh $kind /installers/$pattern"
