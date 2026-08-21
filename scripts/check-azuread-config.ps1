<#
.SYNOPSIS
    Pre-deploy config guard (P1-4/P1-5).
.DESCRIPTION
    Refuses to provision unless ALL FOUR Entra (AzureAd) values are set and
    placeholder-free, so the API never deploys as a crash-looping revision and a
    half-configured tenant is caught before the revision goes live. These are the
    azd env values that feed the container app's AzureAd__* env via Bicep params.
#>
$ErrorActionPreference = 'Stop'

$vars = @(
    'AZURE_AZURE_AD_INSTANCE',
    'AZURE_AZURE_AD_TENANT_ID',
    'AZURE_AZURE_AD_CLIENT_ID',
    'AZURE_AZURE_AD_AUDIENCE'
)

$missing = @()
foreach ($v in $vars) {
    $val = [Environment]::GetEnvironmentVariable($v)
    if ([string]::IsNullOrWhiteSpace($val)) { $missing += $v }
    elseif ($val -like '*REPLACE*') { $missing += "$v(placeholder)" }
}

if ($missing.Count -gt 0) {
    Write-Host "Entra (AzureAd) config incomplete — $($missing -join ' ')"
    Write-Host 'Set all four: azd env set AZURE_AZURE_AD_INSTANCE / _TENANT_ID / _CLIENT_ID / _AUDIENCE <...>'
    exit 1
}

Write-Host 'Entra (AzureAd) config present (all four values set).'
exit 0
