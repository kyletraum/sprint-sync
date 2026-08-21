<#
.SYNOPSIS
    Lists OPEN session handoffs under .specify/handoffs.
.DESCRIPTION
    The load-time check for the handoff mechanism (see .specify/handoffs/README.md
    and CLAUDE.md). Run at session start and before any /speckit-* command. Exit 0
    always — it reports, it does not gate. Prints nothing actionable when there are
    no open handoffs.
#>
[CmdletBinding()]
param(
    [string] $HandoffDir
)

$ErrorActionPreference = 'Stop'

if (-not $HandoffDir) {
    $HandoffDir = Join-Path $PSScriptRoot '..\..\handoffs'
}

if (-not (Test-Path $HandoffDir)) {
    Write-Host 'No handoffs directory — nothing to consume.'
    exit 0
}

$open = @()
Get-ChildItem -Path $HandoffDir -Filter *.md -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne 'README.md' } |
    ForEach-Object {
        $text = Get-Content -Path $_.FullName -Raw
        if ($text -match '(?m)^\s*status:\s*open\b') {
            $id = if ($text -match '(?m)^\s*id:\s*(.+)$') { $Matches[1].Trim() } else { $_.BaseName }
            $feature = if ($text -match '(?m)^\s*feature:\s*(.+)$') { $Matches[1].Trim() } else { '-' }
            $items = ([regex]::Matches($text, '(?m)^\|\s*\d+\s*\|')).Count
            $open += [pscustomobject]@{ File = $_.Name; Id = $id; Feature = $feature; Items = $items }
        }
    }

if ($open.Count -eq 0) {
    Write-Host 'No OPEN handoffs.'
    exit 0
}

Write-Host "OPEN handoffs: $($open.Count) - read and consume before proceeding (CLAUDE.md)."
foreach ($h in $open) {
    Write-Host ("  - .specify/handoffs/" + $h.File + "  [feature: " + $h.Feature + "]  ~" + $h.Items + " item(s)")
}
exit 0
