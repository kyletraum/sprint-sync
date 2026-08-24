#!/bin/sh
# Cost-guard verification suite (feature 002, US4 / T050-T053).
#
# Runs BOTH twins against every fixture and asserts:
#   1. each twin reaches the expected verdict          (T050, T051, T052)
#   2. the two twins reach the SAME verdict            (T053, FR-012)
#
# Twin parity matters because CI runs the POSIX twin while developers run the
# Windows one — divergence means a developer's green is not CI's green.
#
# Usage: sh tests/cost-guard/run-tests.sh

set -eu

ROOT=$(cd "$(dirname "$0")/../.." && pwd)
SH_GUARD="$ROOT/scripts/check-idle-cost.sh"
PS_GUARD="$ROOT/scripts/check-idle-cost.ps1"
FIXTURES="$ROOT/tests/cost-guard/fixtures"

pass=0
fail=0
skipped_ps=0

# Locate a PowerShell to test the twin with; skip parity if none exists.
PWSH=""
for c in pwsh powershell; do
  if command -v "$c" >/dev/null 2>&1; then PWSH=$c; break; fi
done

report_pass() { echo "  PASS  $1"; pass=$((pass + 1)); }
report_fail() { echo "  FAIL  $1"; fail=$((fail + 1)); }

run_case() { # name, expected_exit, why
  name=$1; expected=$2; why=$3
  dir="$FIXTURES/$name"

  echo ""
  echo "$name — $why"

  # Fixtures must be hermetic. Both guards default their web-infra path to the
  # REAL repository's infra-web/, so it has to be overridden here — otherwise
  # every fixture also scans the real Static Web App template, which the
  # fixture's own azure.yaml does not reference, and it classifies as dead
  # infrastructure. A fixture that wants web infra puts it in web/.
  web_dir="$dir/web"
  [ -d "$web_dir" ] || web_dir="$dir/__no_web__"

  set +e
  sh "$SH_GUARD" "$dir/infra" "$dir/azure.yaml" "$web_dir" >"$dir/.sh.out" 2>&1
  sh_exit=$?
  set -e

  if [ "$sh_exit" -eq "$expected" ]; then
    report_pass "sh: exit $sh_exit (expected $expected)"
  else
    report_fail "sh: exit $sh_exit, expected $expected"
    sed 's/^/        /' "$dir/.sh.out"
  fi

  if [ -z "$PWSH" ]; then
    skipped_ps=$((skipped_ps + 1))
    rm -f "$dir/.sh.out"
    return 0
  fi

  set +e
  "$PWSH" -NoProfile -NonInteractive -File "$PS_GUARD" \
    -InfraPath "$dir/infra" -AzureYamlPath "$dir/azure.yaml" \
    -WebInfraPath "$web_dir" >"$dir/.ps.out" 2>&1
  ps_exit=$?
  set -e

  if [ "$ps_exit" -eq "$expected" ]; then
    report_pass "ps: exit $ps_exit (expected $expected)"
  else
    report_fail "ps: exit $ps_exit, expected $expected"
    sed 's/^/        /' "$dir/.ps.out"
  fi

  # T053 — twin parity, asserted independently of expectation.
  if [ "$sh_exit" -eq "$ps_exit" ]; then
    report_pass "parity: both twins agree (exit $sh_exit)"
  else
    report_fail "parity: sh=$sh_exit but ps=$ps_exit — twins DIVERGE"
  fi

  rm -f "$dir/.sh.out" "$dir/.ps.out"
}

echo "Cost-guard verification suite"
echo "============================="
[ -n "$PWSH" ] || echo "(no PowerShell found — twin-parity assertions will be skipped)"

# T050 — no false positives on legitimate non-provision deploy paths.
run_case all-paths-ok 0 \
  "all three deploy paths present and correct: must PASS with no false positive on the service-deploy module or the hook-deployed budget"

# T051 — dead infrastructure fails, named.
run_case orphan-containerapp 1 \
  "a container app on NO deploy path: must FAIL and name the template"

# T052 — an unclassified template cannot satisfy a posture check.
run_case orphan-only-zero-floor 1 \
  "the only minReplicas:0 lives in an unclassified template: must FAIL rather than count it"

# Regression: the original posture check still works on deployed templates.
run_case min-replicas-one 1 \
  "minReplicas:1 on a deployed template: must FAIL"

# T016 — hosting is held to the same posture rules; the Free tier is enforced
# rather than trusted. Also exercises a hook-deployed template outside infra/.
run_case paid-static-web-app 1 \
  "a Static Web App on a paid tier: must FAIL even though it is legitimately hook-deployed"

# Fail-closed: nothing to check is not a pass.
run_case empty-infra 1 \
  "no .bicep files at all: must FAIL closed, never pass by having nothing to check"

echo ""
echo "============================="
echo "passed: $pass   failed: $fail"
[ "$skipped_ps" -gt 0 ] && echo "note: PowerShell twin skipped for $skipped_ps case(s)"

[ "$fail" -eq 0 ] || exit 1
echo "Cost-guard suite OK."
exit 0
