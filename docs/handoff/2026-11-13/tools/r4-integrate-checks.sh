#!/usr/bin/env bash
# Runs one named step of the final checks in app/, logging to the scratch folder; prints "<step> <exit>" and a summary.
L="${L:-$(pwd)/r4-checks-logs}"; mkdir -p "$L/tmp"
export SI_DEV_SERVER_PORT=51874 SI_DEV_SIDECAR_PORT=51875 SI_PREVIEW_PORT=51876 SI_E2E_SIDECAR_PORT=51877 SI_STORYBOOK_PORT=5956
export TEMP=$L/tmp TMP=$L/tmp
cd /c/Projects/ShoutingIguana/.claude/worktrees/r4-integrate/app || exit 1
for step in "$@"; do
  case "$step" in
    light)
      npm run typecheck > $L/f-typecheck.log 2>&1; echo "typecheck $?"
      npm run lint > $L/f-lint.log 2>&1; echo "lint $?"
      npm run format:check > $L/f-format.log 2>&1; echo "format $?"
      node --test "scripts/**/*.test.mjs" > $L/f-scripts.log 2>&1; echo "scripts $?"; grep -E "^ℹ (tests|pass|fail)" $L/f-scripts.log
      ;;
    unit*)
      npm test > $L/f-$step.log 2>&1; echo "$step $?"; grep -E "Test Files|Tests  |FAIL" $L/f-$step.log
      ;;
    storybook)
      npm run build-storybook > $L/f-storybook.log 2>&1; echo "storybook $?"
      ;;
    stories)
      npm run test:stories > $L/f-stories.log 2>&1; echo "stories $?"; tail -1 $L/f-stories.log
      ;;
    stories-forced)
      npm run test:stories -- --forced > $L/f-stories-forced.log 2>&1; echo "stories-forced $?"; tail -1 $L/f-stories-forced.log
      ;;
    stories-reduced)
      npm run test:stories -- --reduced-motion > $L/f-stories-reduced.log 2>&1; echo "stories-reduced $?"; tail -1 $L/f-stories-reduced.log
      ;;
    cargo)
      (cd src-tauri && cargo fmt --check > $L/f-cargo-fmt.log 2>&1; echo "cargo-fmt $?"
       cargo clippy --all-targets -- -D warnings > $L/f-clippy.log 2>&1; echo "clippy $?"
       cargo test > $L/f-cargo-test.log 2>&1; echo "cargo-test $?"; grep "^test result" $L/f-cargo-test.log)
      ;;
    e2e-filled)
      npm run e2e:filled > $L/f-e2e-filled.log 2>&1; echo "e2e-filled $?"; grep -E "passed|failed|skipped|flaky" $L/f-e2e-filled.log | tail -4
      ;;
    e2e-parts)
      npx playwright test e2e/simple-home.spec.ts e2e/simple-results.spec.ts e2e/report.spec.ts e2e/simple-links.spec.ts --repeat-each 5 > $L/f-e2e-parts.log 2>&1; echo "e2e-parts $?"; grep -E "passed|failed|skipped|flaky" $L/f-e2e-parts.log | tail -4
      ;;
    e2e)
      npm run e2e > $L/f-e2e.log 2>&1; echo "e2e $?"; grep -E "passed|failed|skipped|flaky" $L/f-e2e.log | tail -4
      ;;
  esac
done
echo "ALLDONE"
