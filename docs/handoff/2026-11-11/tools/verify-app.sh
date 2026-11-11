#!/usr/bin/env bash
# Runs the app's full check list in one worktree, one log per step, and a one-line result per step in summary.txt.
# usage: verify-app.sh <appDir> <logDir> <portBase> <storybookPort>
app="$1"; logs="$2"; base="$3"; sb="$4"
mkdir -p "$logs/tmp"
# A private temp folder: the dev and e2e scripts keep crawl copies in os.tmpdir(), which other worktrees share.
TEMP="$(cygpath -w "$logs/tmp")"; TMP="$TEMP"; export TEMP TMP
export SI_DEV_SERVER_PORT="$base" SI_DEV_SIDECAR_PORT="$((base + 1))" SI_PREVIEW_PORT="$((base + 2))"
export SI_E2E_SIDECAR_PORT="$((base + 3))" SI_STORYBOOK_PORT="$sb"
cd "$app" || exit 1
: > "$logs/summary.txt"

run() {
  local name="$1"; shift
  local start; start=$(date +%s)
  "$@" > "$logs/$name.log" 2>&1
  local code=$?
  echo "$name exit=$code $(( $(date +%s) - start ))s" >> "$logs/summary.txt"
}

run ci npm ci
run typecheck npm run typecheck
run lint npm run lint
run format npm run format:check
run test npm test
run scripts node --test "scripts/**/*.test.mjs"
run build-storybook npm run build-storybook
run stories npm run test:stories
run stories-forced npm run test:stories -- --forced
run stories-reduced npm run test:stories -- --reduced-motion
run build-sidecar npm run build:sidecar
run e2e npm run e2e
echo "DONE $(git rev-parse --short HEAD)" >> "$logs/summary.txt"
