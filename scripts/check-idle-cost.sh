#!/bin/sh
# Pre-deploy cost guard (constitution Principle 12, task T045).
#
# Refuses the deploy if the synthesized infrastructure would bill while nobody
# is using the app: a Container App with a non-zero replica floor, a SQL
# database not on the free auto-pausing serverless offer, or any resource that
# idles billably by nature. POSIX twin of check-idle-cost.ps1.

set -eu

INFRA_PATH="${1:-$(dirname "$0")/../infra}"

if [ ! -d "$INFRA_PATH" ]; then
  echo "No synthesized infrastructure at '$INFRA_PATH'."
  echo "Run 'azd infra synth' first, then re-run this check."
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

for file in $BICEP_FILES; do
  # 1. Scale-to-zero: any explicit non-zero floor keeps a replica warm.
  if grep -qE 'minReplicas[[:space:]]*:[[:space:]]*[1-9]' "$file"; then
    fail "$file : minReplicas is not 0"
  fi

  # 2. Resource types that bill continuously by their nature.
  for type in 'Microsoft.Cache/redis' \
              'Microsoft.ServiceBus/namespaces' \
              'Microsoft.DBforPostgreSQL/flexibleServers' \
              'Microsoft.DocumentDB/databaseAccounts' \
              'Microsoft.ContainerService/managedClusters'; do
    if grep -qF "$type" "$file"; then
      fail "$file : declares '$type', which bills while idle"
    fi
  done

  # 3. Dedicated ACA workload profiles bill per-node regardless of traffic.
  if grep -qE "workloadProfileType[[:space:]]*:[[:space:]]*'" "$file" &&
     ! grep -qE "workloadProfileType[[:space:]]*:[[:space:]]*'Consumption'" "$file"; then
    fail "$file : uses a non-Consumption workload profile"
  fi

  # 4. SQL must be on the free serverless offer and auto-pause.
  if grep -qF 'Microsoft.Sql/servers/databases' "$file"; then
    grep -qE 'useFreeLimit[[:space:]]*:[[:space:]]*true' "$file" ||
      fail "$file : SQL database is not on the free limit"
    grep -qE "freeLimitExhaustionBehavior[[:space:]]*:[[:space:]]*'AutoPause'" "$file" ||
      fail "$file : SQL database does not auto-pause when the free limit is spent"
  fi
done

if [ "$failures" -gt 0 ]; then
  echo ''
  echo "Idle-cost check FAILED (Principle 12): $failures problem(s) above."
  echo 'Fix the AppHost configuration and re-synth before deploying.'
  exit 1
fi

echo 'Idle-cost check passed: scale-to-zero, SQL auto-pause, no always-on resources.'
exit 0
