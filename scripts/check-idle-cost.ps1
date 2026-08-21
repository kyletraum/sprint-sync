<#
.SYNOPSIS
    Pre-deploy cost guard (constitution Principle 12, task T045).

.DESCRIPTION
    Synthesizes the infrastructure and refuses the deploy if anything in it
    would bill while nobody is using the app. The rule this enforces is not
    "keep costs low" -- it is "nothing idles billably", which is a property you
    can actually check mechanically:

      * every Container App scales to zero (no minReplicas > 0)
      * the SQL database uses the free serverless offer and auto-pauses
      * no always-on resource (Redis, Service Bus, dedicated workload
        profiles) appears at all

    Wired into `azd up` as a preprovision hook, so it runs whether or not
    anyone remembers to.
#>
[CmdletBinding()]
param(
    # Where azd/Aspire wrote the Bicep. Defaults to azd's own output location.
    [string] $InfraPath = "$PSScriptRoot/../infra"
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $InfraPath)) {
    Write-Host "No synthesized infrastructure at '$InfraPath'."
    Write-Host "Run 'azd infra synth' first, then re-run this check."
    exit 1
}

$bicep = Get-ChildItem -Path $InfraPath -Recurse -Filter *.bicep -ErrorAction SilentlyContinue
if (-not $bicep) {
    Write-Host "No .bicep files under '$InfraPath' -- nothing to check."
    exit 1
}

$failures = [System.Collections.Generic.List[string]]::new()

foreach ($file in $bicep) {
    $text = Get-Content -Path $file.FullName -Raw
    $relative = Resolve-Path -Relative $file.FullName

    # 1. Scale-to-zero. Any explicit non-zero floor keeps a replica warm.
    foreach ($match in [regex]::Matches($text, 'minReplicas\s*:\s*(\d+)')) {
        if ([int]$match.Groups[1].Value -ne 0) {
            $failures.Add("$relative : minReplicas = $($match.Groups[1].Value) (must be 0)")
        }
    }

    # 2. Resource types that bill continuously by their nature.
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

    # 3. Dedicated ACA workload profiles bill per-node regardless of traffic.
    if ($text -match "workloadProfileType\s*:\s*'(?!Consumption)") {
        $failures.Add("$relative : uses a non-Consumption workload profile")
    }
}

# 4. The SQL database must actually be on the free serverless offer.
$sqlFiles = $bicep | Where-Object { (Get-Content $_.FullName -Raw) -match 'Microsoft.Sql/servers/databases' }
foreach ($file in $sqlFiles) {
    $text = Get-Content -Path $file.FullName -Raw
    $relative = Resolve-Path -Relative $file.FullName

    if ($text -notmatch 'useFreeLimit\s*:\s*true') {
        $failures.Add("$relative : SQL database is not on the free limit (useFreeLimit != true)")
    }
    if ($text -notmatch "freeLimitExhaustionBehavior\s*:\s*'AutoPause'") {
        $failures.Add("$relative : SQL database does not auto-pause when the free limit is spent")
    }
}

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host 'Idle-cost check FAILED (Principle 12):' -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    Write-Host ''
    Write-Host 'Fix the AppHost configuration and re-synth before deploying.'
    exit 1
}

Write-Host 'Idle-cost check passed: scale-to-zero, SQL auto-pause, no always-on resources.' -ForegroundColor Green
exit 0
