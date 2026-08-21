#!/bin/sh
# Pre-deploy config guard (P1-4). Refuses to provision without real Entra
# (AzureAd) values, so the API never deploys as a crash-looping revision — it
# mirrors, at deploy time, the runtime fail-fast guard in AuthenticationSetup.
# The values come from azd env: `azd env set AzureAd__ClientId <...>` etc.

set -eu

client_id="${AzureAd__ClientId:-}"

case "$client_id" in
  "")
    echo "AzureAd__ClientId is not set."
    echo "Set the Entra config first: azd env set AzureAd__Instance/TenantId/ClientId/Audience."
    exit 1
    ;;
  *REPLACE*)
    echo "AzureAd__ClientId still contains a REPLACE placeholder — set real Entra values via 'azd env set'."
    exit 1
    ;;
esac

echo "Entra (AzureAd) config present."
exit 0
