#!/bin/sh
# Pre-deploy cost guard (constitution Principle 12, task T045).
#
# Refuses the deploy if the synthesized infrastructure would bill while nobody
# is using the app. Fail-closed: it also refuses if the resources it is meant to
# vet are ABSENT, so it can never pass by having nothing to check (P1-5). POSIX
# twin of check-idle-cost.ps1.

set -eu

INFRA_PATH="${1:-$(dirname "$0")/../infra}"

if [ ! -d "$INFRA_PATH" ]; then
  echo "No synthesized infrastructure at '$INFRA_PATH'."
  echo "Run 'azd infra gen' first, then re-run this check."
  exit 1
fi

BICEP_FILES=$(find "$INFRA_PATH" -name '*.bicep' 2>/dev/null || true)
if [ -z "$BICEP_FILES" ]; then
  echo "No .bicep files under '$INFRA_PATH' — nothing to check."
  exit 1
fi

failures=0
fail() {
  echo "  - $1"
  failures=$((failures + 1))
}

saw_containerapp=0
saw_zero_floor=0
saw_sql=0

for file in $BICEP_FILES; do
  # Presence + scale-to-zero.
  if grep -qF 'Microsoft.App/containerApps' "$file"; then saw_containerapp=1; fi
  if grep -qE 'minReplicas[[:space:]]*:[[:space:]]*0' "$file"; then saw_zero_floor=1; fi
  if grep -qE 'minReplicas[[:space:]]*:[[:space:]]*[1-9]' "$file"; then
    fail "$file : minReplicas is not 0"
  fi

  # Resource types that bill continuously by their nature.
  for type in 'Microsoft.Cache/redis' \
              'Microsoft.ServiceBus/namespaces' \
              'Microsoft.DBforPostgreSQL/flexibleServers' \
              'Microsoft.DocumentDB/databaseAccounts' \
              'Microsoft.ContainerService/managedClusters'; do
    if grep -qF "$type" "$file"; then
      fail "$file : declares '$type', which bills while idle"
    fi
  done

  # Dedicated ACA workload profiles bill per-node regardless of traffic.
  if grep -qE "workloadProfileType[[:space:]]*:[[:space:]]*'" "$file" &&
     ! grep -qE "workloadProfileType[[:space:]]*:[[:space:]]*'Consumption'" "$file"; then
    fail "$file : uses a non-Consumption workload profile"
  fi

  # SQL must be on the free serverless offer and auto-pause.
  if grep -qF 'Microsoft.Sql/servers/databases' "$file"; then
    saw_sql=1
    grep -qE 'useFreeLimit[[:space:]]*:[[:space:]]*true' "$file" ||
      fail "$file : SQL database is not on the free limit"
    grep -qE "freeLimitExhaustionBehavior[[:space:]]*:[[:space:]]*'AutoPause'" "$file" ||
      fail "$file : SQL database does not auto-pause when the free limit is spent"
  fi

  # The registry is an unavoidable ~$5/mo floor; keep it on Basic so that floor
  # cannot silently grow into Standard/Premium.
  if grep -qF 'Microsoft.ContainerRegistry/registries' "$file" &&
     grep -qE "name:[[:space:]]*'(Standard|Premium)'" "$file"; then
    fail "$file : container registry is not on the Basic SKU"
  fi
done

# Fail-closed: rules verified against resources that are not present prove nothing.
[ "$saw_containerapp" -eq 1 ] ||
  fail "no Microsoft.App/containerApps resource found — cannot confirm scale-to-zero"
if [ "$saw_containerapp" -eq 1 ] && [ "$saw_zero_floor" -ne 1 ]; then
  fail "a container app is present but none declares minReplicas: 0"
fi
[ "$saw_sql" -eq 1 ] ||
  fail "no Microsoft.Sql/servers/databases resource found — cannot confirm the free serverless offer"

if [ "$failures" -gt 0 ]; then
  echo ''
  echo "Idle-cost check FAILED (Principle 12): $failures problem(s) above."
  echo 'Fix the AppHost configuration and re-synth before deploying.'
  exit 1
fi

echo 'Idle-cost check passed: container app scales to zero, SQL auto-pauses on the free limit, no always-on resources.'
exit 0
