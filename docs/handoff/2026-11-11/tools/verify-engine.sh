#!/usr/bin/env bash
# Builds and tests the engine in one worktree and runs the 100k bench and the read benchmark's gate, one log per step
# and a one-line result per step in summary.txt.
# usage: verify-engine.sh <repoDir> <logDir>
repo="$1"; logs="$2"
mkdir -p "$logs/tmp" "$logs/crawls"
TEMP="$(cygpath -w "$logs/tmp")"; TMP="$TEMP"; export TEMP TMP
cd "$repo" || exit 1
: > "$logs/summary.txt"

run() {
  local name="$1"; shift
  local start; start=$(date +%s)
  "$@" > "$logs/$name.log" 2>&1
  local code=$?
  echo "$name exit=$code $(( $(date +%s) - start ))s" >> "$logs/summary.txt"
}

si="engine/src/ShoutingIguana.Cli/bin/Release/net10.0/si.exe"
crawl="$(cygpath -w "$logs/crawls/bench.sicrawl")"

run build dotnet build engine/ShoutingIguana.Engine.slnx -c Release
# A build that failed leaves the last build's binaries: testing or benching those would pass for the wrong code.
if ! grep -q "^build exit=0 " "$logs/summary.txt"; then echo "ABORT: the build failed; nothing after it was run" >> "$logs/summary.txt"; exit 1; fi
run test dotnet test engine/ShoutingIguana.Engine.slnx -c Release --no-build
if [ ! -x "$si" ]; then si="$(find engine/src/ShoutingIguana.Cli/bin/Release -name 'si.exe' | head -1)"; fi
echo "si=$si" >> "$logs/summary.txt"
run bench "$si" bench --pages 100000 --concurrency 16 --db "$crawl" --json "$(cygpath -w "$logs/bench.json")"
run readbench "$si" readbench "$crawl" --runs 20 --gate --json "$(cygpath -w "$logs/readbench.json")"
echo "DONE $(git rev-parse --short HEAD)" >> "$logs/summary.txt"
