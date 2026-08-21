#!/bin/sh
# Pre-deploy config guard (P1-4/P1-5). Refuses to provision unless ALL FOUR Entra
# (AzureAd) values are set and placeholder-free, so the API never deploys as a
# crash-looping revision and a half-configured tenant (e.g. TenantId still a
# placeholder) is caught before the revision goes live. These are the azd env
# values that feed the container app's AzureAd__* env via Bicep parameters.

set -eu

missing=""
for var in AZURE_AZURE_AD_INSTANCE AZURE_AZURE_AD_TENANT_ID AZURE_AZURE_AD_CLIENT_ID AZURE_AZURE_AD_AUDIENCE; do
  eval "val=\${$var:-}"
  case "$val" in
    "") missing="$missing $var" ;;
    *REPLACE*) missing="$missing $var(placeholder)" ;;
  esac
done

if [ -n "$missing" ]; then
  echo "Entra (AzureAd) config incomplete —$missing"
  echo "Set all four: azd env set AZURE_AZURE_AD_INSTANCE / _TENANT_ID / _CLIENT_ID / _AUDIENCE <...>"
  exit 1
fi

echo "Entra (AzureAd) config present (all four values set)."
exit 0
