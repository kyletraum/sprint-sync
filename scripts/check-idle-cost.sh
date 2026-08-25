#!/bin/sh
# Pre-deploy cost guard (constitution: Deployment & Cost Constraints).
#
# Refuses the deploy if the synthesized infrastructure would bill while nobody
# is using the app. Fail-closed: it also refuses if the resources it is meant to
# vet are ABSENT, so it can never pass by having nothing to check (P1-5). POSIX
# twin of check-idle-cost.ps1.
#
# Deploy-path classification (constitution v1.1.0, feature 002 US4):
# posture checks are verified against templates that are ACTUALLY DEPLOYED, so
# a file's mere presence under infra/ can no longer satisfy them. Three paths
# count as deployed:
#
#   provision      reachable from main.bicep by transitive `module` reference
#   service-deploy azd's per-service module — declares a container app AND takes
#                  a container-image parameter, which cannot exist at provision
#                  time, which is exactly why it is not in main.bicep
#   hook           referenced by a hook command in azure.yaml
#
# A template on NONE of these is dead infrastructure and fails the check.
#
# Reachability alone is NOT the test. Applying it as such condemns two correct
# templates in this repository — the azd service-deploy module and the
# hook-deployed budget — and a guard that fails every legitimate run gets
# disabled, which is worse than the bug it fixes.

set -eu

INFRA_PATH="${1:-$(dirname "$0")/../infra}"
AZURE_YAML="${2:-$(dirname "$0")/../azure.yaml}"
# Hand-written infrastructure that cannot live in the generated tree. Scanned
# too, so hosting is held to the same posture rules as everything else: a Free
# Static Web App silently becoming Standard is a standing-cost regression, and
# the point of this guard is that posture claims are machine-checked rather
# than trusted (feature 002, T016).
WEB_INFRA_PATH="${3:-$(dirname "$0")/../infra-web}"

if [ ! -d "$INFRA_PATH" ]; then
  echo "No synthesized infrastructure at '$INFRA_PATH'."
  echo "Run 'azd infra gen' first, then re-run this check."
  exit 1
fi

BICEP_FILES=$(find "$INFRA_PATH" -name '*.bicep' 2>/dev/null || true)
if [ -d "$WEB_INFRA_PATH" ]; then
  WEB_FILES=$(find "$WEB_INFRA_PATH" -name '*.bicep' 2>/dev/null || true)
  [ -n "$WEB_FILES" ] && BICEP_FILES="$BICEP_FILES
$WEB_FILES"
fi
if [ -z "$BICEP_FILES" ]; then
  echo "No .bicep files under '$INFRA_PATH' — nothing to check."
  exit 1
fi

failures=0
fail() {
  echo "  - $1"
  failures=$((failures + 1))
}

# --- path normalisation: strip './' and resolve 'x/../' so that a module
# --- reference and the `find` output for the same file compare equal.
norm_path() {
  _p=$(printf '%s\n' "$1" | sed -e 's#//*#/#g' -e 's#/\./#/#g' -e 's#^\./##')
  # Resolve 'a/b/../c' -> 'a/c', repeatedly, until it stops changing.
  while :; do
    case "$_p" in
      */../*)
        _new=$(printf '%s\n' "$_p" | sed -e 's#/[^/][^/]*/\.\./#/#')
        [ "$_new" = "$_p" ] && break
        _p=$_new
        ;;
      *) break ;;
    esac
  done
  printf '%s\n' "$_p"
}

# Membership test. An EMPTY needle must never match: `grep -Fxq ''` matches the
# empty line that `printf` emits for an empty set, which would silently classify
# every template as reachable and let the guard pass anything.
in_set() { # needle, haystack(newline-separated)
  [ -n "$1" ] || return 1
  [ -n "$2" ] || return 1
  printf '%s\n' "$2" | grep -Fxq -- "$1"
}

# --- path 1: provision — transitive closure from main.bicep -------------------
MAIN_BICEP=$(norm_path "$INFRA_PATH/main.bicep")
reachable=""
[ -f "$MAIN_BICEP" ] && reachable="$MAIN_BICEP"

changed=1
while [ "$changed" -eq 1 ]; do
  changed=0
  for f in $BICEP_FILES; do
    n=$(norm_path "$f")
    in_set "$n" "$reachable" || continue
    d=$(dirname "$n")
    refs=$(grep -oE "^[[:space:]]*module[[:space:]]+[A-Za-z0-9_]+[[:space:]]+'[^']+'" "$f" 2>/dev/null |
             sed -E "s/.*'([^']+)'.*/\1/" || true)
    for r in $refs; do
      t=$(norm_path "$d/$r")
      if ! in_set "$t" "$reachable"; then
        reachable="$reachable
$t"
        changed=1
      fi
    done
  done
done

# --- path 3: hook — any .bicep token named in an azure.yaml hook command ------
hook_tokens=""
if [ -f "$AZURE_YAML" ]; then
  hook_tokens=$(grep -oE '[A-Za-z0-9_./-]+\.bicep' "$AZURE_YAML" 2>/dev/null || true)
fi

is_hook_deployed() { # normalised path
  [ -n "$hook_tokens" ] || return 1
  _base=$(basename "$1")
  for tok in $hook_tokens; do
    case "$1" in *"$tok") return 0 ;; esac
    [ "$(basename "$tok")" = "$_base" ] && return 0
  done
  return 1
}

# --- path 2: service-deploy — container app + a container-image parameter -----
is_service_deploy() { # file
  grep -qF 'Microsoft.App/containerApps' "$1" || return 1
  grep -qiE "^[[:space:]]*param[[:space:]]+[A-Za-z0-9_]*containerimage[A-Za-z0-9_]*[[:space:]]+string" "$1"
}

classify() { # file -> prints provision|service-deploy|hook|none
  _n=$(norm_path "$1")
  if in_set "$_n" "$reachable"; then echo provision
  elif is_service_deploy "$1"; then echo service-deploy
  elif is_hook_deployed "$_n"; then echo hook
  else echo none
  fi
}

saw_containerapp=0
saw_zero_floor=0
saw_sql=0

for file in $BICEP_FILES; do
  path_class=$(classify "$file")

  # Dead infrastructure: on no deploy path at all.
  if [ "$path_class" = none ]; then
    fail "$file : on no deploy path (not reachable from main.bicep, not an azd service-deploy module, not referenced by an azure.yaml hook) — wire it in or delete it"
    continue
  fi

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
  #
  # Checked PER OCCURRENCE, never per file. `workloadProfiles` is an ARRAY, and
  # infra/cae/cae.module.bicep already declares a Consumption entry — so the
  # earlier whole-file form ("contains a profile AND contains no non-Consumption
  # profile") was permanently satisfied by that one entry, leaving the rule dead
  # on the exact tree it guards while still printing a pass. The PowerShell twin
  # used a per-occurrence negative lookahead and caught what this missed, so the
  # twins silently disagreed and no fixture covered the shape.
  #
  # Case-INSENSITIVE on both the property name and the value, and comparing the
  # extracted value to exactly "Consumption" rather than substring-matching it.
  # All three matter (B-3, round 3): bicep property binding is case-insensitive,
  # so ARM accepts `workloadprofiletype: 'D4'` and deploys a real billable node,
  # which the case-sensitive form missed entirely — a permissive hole on the twin
  # that CI and the preprovision hook actually run. A substring test would also
  # wave through a value like 'ConsumptionPlus'.
  bad_profiles=$(grep -oiE "workloadProfileType[[:space:]]*:[[:space:]]*'[^']*'" "$file" 2>/dev/null |
                   sed -E "s/.*'([^']*)'.*/\1/" |
                   grep -viE '^Consumption$' || true)
  if [ -n "$bad_profiles" ]; then
    fail "$file : declares a non-Consumption workload profile ($(printf '%s' "$bad_profiles" | tr '\n' ' '))"
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

  # Static Web Apps must stay on Free: the constitution names that tier for the
  # React app, and any other tier introduces a standing monthly charge.
  if grep -qF 'Microsoft.Web/staticSites' "$file" &&
     ! grep -qE "name:[[:space:]]*'Free'" "$file"; then
    fail "$file : Static Web App is not on the Free SKU"
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
  echo "Idle-cost check FAILED (Deployment & Cost Constraints): $failures problem(s) above."
  echo 'Fix the AppHost configuration and re-synth before deploying.'
  exit 1
fi

echo 'Idle-cost check passed: container app scales to zero, SQL auto-pauses on the free limit,'
echo 'no always-on resources, and every template sits on a real deploy path.'
exit 0
