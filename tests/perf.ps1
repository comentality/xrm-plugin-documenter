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
      .\perf.ps1 -Folder C:\src\Mine # time a repository you already have, read only

    -Folder answers the question a generated tree cannot: why *this* project is slow. It
    reads the folder and nothing else - no file is written and the write phases are not
    run - and stands the registrations in with the plugin classes the folder declares.

    Trees are generated under tests\.perf and rebuilt on every run. The write phases
    change them, so each size is generated once and its phases run in order.

    A note on cold caches: everything here runs against a tree Windows has just written, so
    the file cache is warm. A first scan of somebody's repository after a reboot pays real
    disk on top of these numbers. The ratios are what transfer; the milliseconds are a
    floor on what a person will see, not a ceiling.
#>
[CmdletBinding()]
param(
    [int[]]  $Files = @(250, 1000, 4000),
    [int]    $Classes = 33,
    [string] $Folder,
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

# What each phase is allowed to cost, said the way the phase costs it: Ratio is against
# the floor - reading every source file once - and PerClass is milliseconds per registered
# class. A phase is allowed Ratio x floor + PerClass x classes, and separately may not
# exceed Ms of wall clock, which is the part a person notices.
#
# Writing that budget in two terms is the point rather than a nicety. A scan is a folder
# sized job and should cost about a read; a render never opens the folder at all and is
# only ever as slow as the number of classes; a write is both. A phase whose real cost is
# in the wrong term is the bug this script exists to catch - one press of Write used to
# read the whole folder once per class, and that is a Ratio of thirty, not of four.
#
# These are ambitions rather than measurements, set a comfortable way above what the code
# does today so an ordinary machine having a bad minute is not a failure.
$budgets = @(
    @{ Phase = 'scan';    Ratio = 3.0; PerClass = 0.0; Ms = 1200; What = 'holding the folder against the registrations' }
    @{ Phase = 'rescan';  Ratio = 3.0; PerClass = 0.0; Ms = 1200; What = 'the scan a finished write starts' }
    @{ Phase = 'render';  Ratio = 0.0; PerClass = 2.0; Ms = 100;  What = 'the list render, on the UI thread' }
    @{ Phase = 'lookup';  Ratio = 0.0; PerClass = 0.05; Ms = 20;  What = 'finding the file for every class' }
    @{ Phase = 'write';   Ratio = 2.5; PerClass = 2.0; Ms = 2000; What = 'one press of Write' }
    @{ Phase = 'rewrite'; Ratio = 2.5; PerClass = 2.0; Ms = 2000; What = 'pressing Write again, nothing to change' }
)

$script:failures = @()

function Format-Ms {
    param([double] $Ms)
    if ($Ms -ge 1000) { '{0,7:N0} ms' -f $Ms } else { '{0,7:N1} ms' -f $Ms }
}

# One run of the harness, reported and judged. Which phases are judged is the caller's,
# because a read-only run of somebody's own repository never reaches the write phases.
function Show-Run {
    param([string] $Label, [string[]] $Output, [int] $ClassCount, [string[]] $Judge)

    $phases = [ordered]@{}
    foreach ($line in $Output) {
        $parts = $line -split "`t"
        if ($parts.Count -lt 3) { continue }
        $phases[$parts[0]] = [pscustomobject]@{ Ms = [double] $parts[1]; Note = $parts[2] }
    }

    foreach ($required in @('floor', 'enumerate') + $Judge) {
        if (-not $phases.Contains($required)) { throw "The harness printed no '$required' line." }
    }

    $floor = $phases['floor'].Ms
    foreach ($name in @('tree', 'floor', 'enumerate', 'index')) {
        if ($phases.Contains($name)) {
            Write-Host ("  {0,-9} {1}          {2}" -f $name, (Format-Ms $phases[$name].Ms), $phases[$name].Note)
        }
    }

    foreach ($budget in $budgets | Where-Object { $Judge -contains $_.Phase }) {
        $phase = $phases[$budget.Phase]
        $ratio = if ($floor -gt 0) { $phase.Ms / $floor } else { 0 }
        $allowed = $budget.Ratio * $floor + $budget.PerClass * $ClassCount
        $over  = @()
        if ($phase.Ms -gt $allowed) {
            $over += ('{0:N1} ms, allowed {1:N1} ms ({2:N1}x floor + {3:N1} ms per class)' -f
                $phase.Ms, $allowed, $budget.Ratio, $budget.PerClass)
        }
        if ($phase.Ms -gt $budget.Ms) { $over += ('{0:N0} ms, over the {1:N0} ms a person notices' -f $phase.Ms, $budget.Ms) }

        $line = "  {0,-9} {1}  {2,5:N1}x  {3}" -f $budget.Phase, (Format-Ms $phase.Ms), $ratio, $phase.Note
        if ($over.Count -eq 0) {
            Write-Host $line
        } else {
            Write-Host "$line" -ForegroundColor Red
            Write-Host ("             OVER BUDGET - " + ($over -join '; ') + " - " + $budget.What) -ForegroundColor Red
            $script:failures += "${Label}: $($budget.Phase) $($over -join '; ')"
        }
    }
}

if ($Folder) {
    if (-not (Test-Path $Folder)) { throw "No folder at $Folder" }
    $full = (Resolve-Path $Folder).Path

    Write-Host ''
    Write-Host "$full - read only, nothing is written" -ForegroundColor Cyan

    $output = & $exe --measure $full
    if ($LASTEXITCODE -ne 0) { throw "The harness failed:`n$($output -join [Environment]::NewLine)" }

    # The stand-in count the harness reported, which is what the per-class budgets are per.
    $standIns = if (($output -join "`n") -match 'standing in for (\d+) registrations') { [int] $Matches[1] } else { 0 }
    Show-Run $full $output $standIns @('scan', 'render', 'lookup')
} else {
    foreach ($size in $Files) {
        $tree = Join-Path $work "tree-$size"
        Write-Host ''
        Write-Host "$size source files, $Classes registered classes" -ForegroundColor Cyan

        $output = & $exe $tree $size $Classes
        if ($LASTEXITCODE -ne 0) { throw "The harness failed for $size files:`n$($output -join [Environment]::NewLine)" }

        Show-Run "$size files" $output $Classes ($budgets | ForEach-Object { $_.Phase })
    }

    if (-not $Keep -and (Test-Path $work)) { Remove-Item $work -Recurse -Force }
}

Write-Host ''
if ($script:failures.Count -gt 0) {
    Write-Host "$($script:failures.Count) phase(s) over budget:" -ForegroundColor Red
    $script:failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}

Write-Host 'Every phase within budget.' -ForegroundColor Green
