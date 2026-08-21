<#
.SYNOPSIS
    Pre-deploy config guard (P1-4).
.DESCRIPTION
    Refuses to provision without real Entra (AzureAd) values so the API never
    deploys as a crash-looping revision — mirrors, at deploy time, the runtime
    fail-fast guard in AuthenticationSetup. Values come from azd env
    (`azd env set AzureAd__ClientId <...>` etc.).
#>
$ErrorActionPreference = 'Stop'

$clientId = $env:AzureAd__ClientId

if ([string]::IsNullOrWhiteSpace($clientId)) {
    Write-Host 'AzureAd__ClientId is not set.'
    Write-Host "Set the Entra config first: azd env set AzureAd__Instance/TenantId/ClientId/Audience."
    exit 1
}

if ($clientId -like '*REPLACE*') {
    Write-Host "AzureAd__ClientId still contains a REPLACE placeholder — set real Entra values via 'azd env set'."
    exit 1
}

Write-Host 'Entra (AzureAd) config present.'
exit 0
