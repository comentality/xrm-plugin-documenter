<#
.SYNOPSIS
    Times the scan and the write over generated source trees, and fails when either grows
    faster than reading the tree once.

.DESCRIPTION
    write.ps1 pins what the tool writes. This pins what it costs. The harness generates a
    plugin repository of a given size - projects, areas, a shared base class, plugins,
    ordinary code around them and a build's worth of output to walk past - and times the
    real entry points over it: the folder scan the source column runs, the per-class
    StateOf the list render runs, and one press of Write.

    Every phase is quoted against the floor: opening and reading every source file once,
    measured on this machine, over this tree. That is what an honest answer about the
    folder costs, and it makes the budgets below mean something on a laptop and on a build
    agent alike. A phase far above the floor is a phase walking the folder once per class
    rather than once per press - which is the shape of the bug this script exists to catch,
    and the reason a 33 class project took five seconds to scan.

    Absolute budgets are there too, because a ratio says nothing about whether the window
    froze. They are the numbers a person notices: a scan is a background task and may take
    a second, a render sits on the UI thread between a click and the screen and may not.

      .\perf.ps1                     # three tree sizes
      .\perf.ps1 -Files 4000         # one size
      .\perf.ps1 -Classes 120        # a bigger registration
      .\perf.ps1 -SkipBuild          # reuse the last build
      .\perf.ps1 -Keep               # leave the generated trees for a profiler

    Trees are generated under tests\.perf and rebuilt on every run. The write phases
    change them, so each size is generated once and its phases run in order.

    A note on cold caches: everything here runs against a tree Windows has just written, so
    the file cache is warm. A first scan of somebody's repository after a reboot pays real
    disk on top of these numbers. The ratios are what transfer; the milliseconds are a
    floor on what a person will see, not a ceiling.
#>
[CmdletBinding()]
param(
    [int[]] $Files = @(250, 1000, 4000),
    [int]   $Classes = 33,
    [switch] $SkipBuild,
    [switch] $Keep
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root    = $PSScriptRoot
$project = Join-Path $root 'PluginStepCodegen.PerfHarness\PluginStepCodegen.PerfHarness.csproj'
$exe     = Join-Path $root 'PluginStepCodegen.PerfHarness\bin\Debug\net48\PluginStepCodegen.PerfHarness.exe'
$work    = Join-Path $root '.perf'

if (-not $SkipBuild) {
    dotnet build $project -c Debug -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw 'The harness did not build.' }
}
if (-not (Test-Path $exe)) { throw "No harness at $exe - run without -SkipBuild." }

# What each phase is allowed to cost. Ratio is against the floor - reading every source
# file once - and Ms is the wall clock a person would notice. A phase needs to pass both.
#
# The floor is a whole tree read, so a phase that only touches the classes it was asked
# about is allowed to be a fraction of it; the ones that read everything get room for the
# work they do on top. Ambition, not measurement: these are what the numbers should be,
# and the ones that were failing when this script was written are noted in tests\README.md.
$budgets = @(
    @{ Phase = 'scan';    Ratio = 3.0; Ms = 1200; What = 'holding the folder against the registrations' }
    @{ Phase = 'rescan';  Ratio = 3.0; Ms = 1200; What = 'the scan a finished write starts' }
    @{ Phase = 'render';  Ratio = 0.5; Ms = 100;  What = 'the list render, on the UI thread' }
    @{ Phase = 'lookup';  Ratio = 1.5; Ms = 800;  What = 'finding the file for every class' }
    @{ Phase = 'write';   Ratio = 4.0; Ms = 2000; What = 'one press of Write' }
    @{ Phase = 'rewrite'; Ratio = 4.0; Ms = 2000; What = 'pressing Write again, nothing to change' }
)

$script:failures = @()

function Format-Ms {
    param([double] $Ms)
    if ($Ms -ge 1000) { '{0,7:N0} ms' -f $Ms } else { '{0,7:N1} ms' -f $Ms }
}

foreach ($size in $Files) {
    $tree = Join-Path $work "tree-$size"
    Write-Host ''
    Write-Host "$size source files, $Classes registered classes" -ForegroundColor Cyan

    $output = & $exe $tree $size $Classes
    if ($LASTEXITCODE -ne 0) { throw "The harness failed for $size files:`n$($output -join [Environment]::NewLine)" }

    $phases = [ordered]@{}
    foreach ($line in $output) {
        $parts = $line -split "`t"
        if ($parts.Count -lt 3) { continue }
        $phases[$parts[0]] = [pscustomobject]@{ Ms = [double] $parts[1]; Note = $parts[2] }
    }

    foreach ($required in @('floor') + ($budgets | ForEach-Object { $_.Phase })) {
        if (-not $phases.Contains($required)) { throw "The harness printed no '$required' line." }
    }

    $floor = $phases['floor'].Ms
    Write-Host ("  {0,-9} {1}          {2}" -f 'tree', (Format-Ms $phases['tree'].Ms), $phases['tree'].Note)
    Write-Host ("  {0,-9} {1}          {2}" -f 'floor', (Format-Ms $floor), $phases['floor'].Note)
    Write-Host ("  {0,-9} {1}          {2}" -f 'enumerate', (Format-Ms $phases['enumerate'].Ms), $phases['enumerate'].Note)

    foreach ($budget in $budgets) {
        $phase = $phases[$budget.Phase]
        $ratio = if ($floor -gt 0) { $phase.Ms / $floor } else { 0 }
        $over  = @()
        if ($ratio -gt $budget.Ratio) { $over += ('{0:N1}x floor, allowed {1:N1}x' -f $ratio, $budget.Ratio) }
        if ($phase.Ms -gt $budget.Ms) { $over += ('{0:N0} ms, allowed {1:N0} ms' -f $phase.Ms, $budget.Ms) }

        $line = "  {0,-9} {1}  {2,5:N1}x  {3}" -f $budget.Phase, (Format-Ms $phase.Ms), $ratio, $phase.Note
        if ($over.Count -eq 0) {
            Write-Host $line
        } else {
            Write-Host "$line" -ForegroundColor Red
            Write-Host ("             OVER BUDGET - " + ($over -join '; ') + " - " + $budget.What) -ForegroundColor Red
            $script:failures += "$size files: $($budget.Phase) $($over -join '; ')"
        }
    }
}

if (-not $Keep -and (Test-Path $work)) { Remove-Item $work -Recurse -Force }

Write-Host ''
if ($script:failures.Count -gt 0) {
    Write-Host "$($script:failures.Count) phase(s) over budget:" -ForegroundColor Red
    $script:failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}

Write-Host 'Every phase within budget.' -ForegroundColor Green
