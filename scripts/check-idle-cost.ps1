<#
.SYNOPSIS
    Pre-deploy cost guard (constitution Principle 12, task T045).

.DESCRIPTION
    Refuses the deploy if anything in the synthesized infrastructure would bill
    while nobody is using the app. Fail-closed: it also refuses if the resources
    it is meant to vet are ABSENT, so it can never pass by having nothing to
    check (P1-5).

      * a Container App is present and scales to zero (no minReplicas > 0)
      * a SQL database is present, on the free serverless offer, and auto-pauses
      * no always-on resource (Redis, Service Bus, dedicated workload profiles,
        Cosmos, PostgreSQL flexible server, AKS) appears at all

    Wired into `azd up` as a preprovision hook, so it runs whether or not anyone
    remembers to.
#>
[CmdletBinding()]
param(
    # Where azd/Aspire wrote the Bicep. Defaults to azd's own output location.
    [string] $InfraPath = "$PSScriptRoot/../infra"
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $InfraPath)) {
    Write-Host "No synthesized infrastructure at '$InfraPath'."
    Write-Host "Run 'azd infra gen' first, then re-run this check."
    exit 1
}

$bicep = Get-ChildItem -Path $InfraPath -Recurse -Filter *.bicep -ErrorAction SilentlyContinue
if (-not $bicep) {
    Write-Host "No .bicep files under '$InfraPath' -- nothing to check."
    exit 1
}

$failures = [System.Collections.Generic.List[string]]::new()
$sawContainerApp = $false
$sawZeroFloor = $false
$sawSql = $false

foreach ($file in $bicep) {
    $text = Get-Content -Path $file.FullName -Raw
    $relative = Resolve-Path -Relative $file.FullName

    # Presence + scale-to-zero.
    if ($text -match 'Microsoft\.App/containerApps') { $sawContainerApp = $true }
    foreach ($match in [regex]::Matches($text, 'minReplicas\s*:\s*(\d+)')) {
        if ([int]$match.Groups[1].Value -eq 0) { $sawZeroFloor = $true }
        else { $failures.Add("$relative : minReplicas = $($match.Groups[1].Value) (must be 0)") }
    }

    # Resource types that bill continuously by their nature.
    $alwaysOn = @(
        'Microsoft.Cache/redis',
        'Microsoft.ServiceBus/namespaces',
        'Microsoft.DBforPostgreSQL/flexibleServers',
        'Microsoft.DocumentDB/databaseAccounts',
        'Microsoft.ContainerService/managedClusters'
    )
    foreach ($type in $alwaysOn) {
        if ($text -match [regex]::Escape($type)) {
            $failures.Add("$relative : declares '$type', which bills while idle")
        }
    }

    # Dedicated ACA workload profiles bill per-node regardless of traffic.
    if ($text -match "workloadProfileType\s*:\s*'(?!Consumption)") {
        $failures.Add("$relative : uses a non-Consumption workload profile")
    }

    # The SQL database must actually be on the free serverless offer.
    if ($text -match 'Microsoft\.Sql/servers/databases') {
        $sawSql = $true
        if ($text -notmatch 'useFreeLimit\s*:\s*true') {
            $failures.Add("$relative : SQL database is not on the free limit (useFreeLimit != true)")
        }
        if ($text -notmatch "freeLimitExhaustionBehavior\s*:\s*'AutoPause'") {
            $failures.Add("$relative : SQL database does not auto-pause when the free limit is spent")
        }
    }
}

# Fail-closed: rules verified against resources that are not present prove nothing.
if (-not $sawContainerApp) {
    $failures.Add('no Microsoft.App/containerApps resource found -- cannot confirm scale-to-zero')
}
elseif (-not $sawZeroFloor) {
    $failures.Add('a container app is present but none declares minReplicas: 0')
}
if (-not $sawSql) {
    $failures.Add('no Microsoft.Sql/servers/databases resource found -- cannot confirm the free serverless offer')
}

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host 'Idle-cost check FAILED (Principle 12):' -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    Write-Host ''
    Write-Host 'Fix the AppHost configuration and re-synth before deploying.'
    exit 1
}

Write-Host 'Idle-cost check passed: container app scales to zero, SQL auto-pauses on the free limit, no always-on resources.' -ForegroundColor Green
exit 0
