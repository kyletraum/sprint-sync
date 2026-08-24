<#
.SYNOPSIS
    Pre-deploy cost guard (constitution: Deployment & Cost Constraints).

.DESCRIPTION
    Refuses the deploy if anything in the synthesized infrastructure would bill
    while nobody is using the app. Fail-closed: it also refuses if the resources
    it is meant to vet are ABSENT, so it can never pass by having nothing to
    check (P1-5).

      * a Container App is present and scales to zero (no minReplicas > 0)
      * a SQL database is present, on the free serverless offer, and auto-pauses
      * no always-on resource (Redis, Service Bus, dedicated workload profiles,
        Cosmos, PostgreSQL flexible server, AKS) appears at all

    Deploy-path classification (constitution v1.1.0, feature 002 US4): posture
    checks are verified against templates that are ACTUALLY DEPLOYED, so a file's
    mere presence under infra/ can no longer satisfy them. Three paths count as
    deployed:

      provision       reachable from main.bicep by transitive `module` reference
      service-deploy  azd's per-service module -- declares a container app AND
                      takes a container-image parameter, which cannot exist at
                      provision time, which is exactly why it is not in main.bicep
      hook            referenced by a hook command in azure.yaml

    A template on NONE of these is dead infrastructure and fails the check.

    Reachability alone is NOT the test. Applying it as such condemns two correct
    templates in this repository -- the azd service-deploy module and the
    hook-deployed budget -- and a guard that fails every legitimate run gets
    disabled, which is worse than the bug it fixes.

    Wired into `azd up` as a preprovision hook, so it runs whether or not anyone
    remembers to. Windows twin of check-idle-cost.sh; the two MUST reach
    identical verdicts (tests/cost-guard/run-tests.sh asserts this).
#>
[CmdletBinding()]
param(
    # Where azd/Aspire wrote the Bicep. Defaults to azd's own output location.
    [string] $InfraPath = "$PSScriptRoot/../infra",

    # azure.yaml, read to discover hook-deployed templates.
    [string] $AzureYamlPath = "$PSScriptRoot/../azure.yaml"
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

# --- path normalisation so a module reference and the enumerated file compare equal
function Get-NormalPath([string] $Path) {
    return [System.IO.Path]::GetFullPath($Path).Replace('\', '/')
}

# --- path 1: provision -- transitive closure from main.bicep -----------------
$reachable = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)

$mainBicep = Join-Path $InfraPath 'main.bicep'
if (Test-Path $mainBicep) {
    $queue = [System.Collections.Generic.Queue[string]]::new()
    $queue.Enqueue((Get-NormalPath $mainBicep))
    [void]$reachable.Add((Get-NormalPath $mainBicep))

    while ($queue.Count -gt 0) {
        $current = $queue.Dequeue()
        if (-not (Test-Path $current)) { continue }
        $currentDir = Split-Path -Parent $current
        $body = Get-Content -Path $current -Raw
        foreach ($m in [regex]::Matches($body, "(?m)^\s*module\s+[A-Za-z0-9_]+\s+'([^']+)'")) {
            $target = Get-NormalPath (Join-Path $currentDir $m.Groups[1].Value)
            if ($reachable.Add($target)) { $queue.Enqueue($target) }
        }
    }
}

# --- path 3: hook -- any .bicep token named in an azure.yaml hook command -----
$hookTokens = @()
if (Test-Path $AzureYamlPath) {
    $yaml = Get-Content -Path $AzureYamlPath -Raw
    $hookTokens = [regex]::Matches($yaml, '[A-Za-z0-9_./-]+\.bicep') |
        ForEach-Object { $_.Value }
}

function Test-HookDeployed([string] $NormalPath) {
    if (-not $hookTokens) { return $false }
    $leaf = Split-Path -Leaf $NormalPath
    foreach ($tok in $hookTokens) {
        $normTok = $tok.Replace('\', '/')
        if ($NormalPath.EndsWith($normTok, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
        if ((Split-Path -Leaf $normTok) -ieq $leaf) { return $true }
    }
    return $false
}

# --- path 2: service-deploy -- container app + a container-image parameter ----
function Test-ServiceDeploy([string] $Body) {
    if ($Body -notmatch 'Microsoft\.App/containerApps') { return $false }
    return $Body -match '(?im)^\s*param\s+[A-Za-z0-9_]*containerimage[A-Za-z0-9_]*\s+string'
}

function Get-DeployPath([string] $FullPath, [string] $Body) {
    $normal = Get-NormalPath $FullPath
    if ($reachable.Contains($normal)) { return 'provision' }
    if (Test-ServiceDeploy $Body) { return 'service-deploy' }
    if (Test-HookDeployed $normal) { return 'hook' }
    return 'none'
}

$failures = [System.Collections.Generic.List[string]]::new()
$sawContainerApp = $false
$sawZeroFloor = $false
$sawSql = $false

foreach ($file in $bicep) {
    $text = Get-Content -Path $file.FullName -Raw
    $relative = Resolve-Path -Relative $file.FullName

    # Dead infrastructure: on no deploy path at all.
    $deployPath = Get-DeployPath $file.FullName $text
    if ($deployPath -eq 'none') {
        $failures.Add("$relative : on no deploy path (not reachable from main.bicep, not an azd service-deploy module, not referenced by an azure.yaml hook) -- wire it in or delete it")
        continue
    }

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

    # Keep the registry on Basic so the ~$5/mo floor cannot silently grow.
    if ($text -match 'Microsoft\.ContainerRegistry/registries' -and $text -match "name\s*:\s*'(Standard|Premium)'") {
        $failures.Add("$relative : container registry is not on the Basic SKU")
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
    Write-Host 'Idle-cost check FAILED (Deployment & Cost Constraints):' -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    Write-Host ''
    Write-Host 'Fix the AppHost configuration and re-synth before deploying.'
    exit 1
}

Write-Host 'Idle-cost check passed: container app scales to zero, SQL auto-pauses on the free limit,' -ForegroundColor Green
Write-Host 'no always-on resources, and every template sits on a real deploy path.' -ForegroundColor Green
exit 0
