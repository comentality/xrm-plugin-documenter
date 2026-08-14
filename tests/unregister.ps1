<#
.SYNOPSIS
    Removes everything register.ps1 put in the environment.

.DESCRIPTION
    Deletes both fixture solutions, companion first. Because they are managed, deleting
    them takes the steps, images, plugin types and the assembly itself with them - which
    an unmanaged solution would not: its components stay behind in the Default solution.

    A solution that is not there is not an error; this is safe to run at any time.
#>
[CmdletBinding()]
param([string] $Environment)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$manifest = Import-PowerShellDataFile (Join-Path $PSScriptRoot 'registrations.psd1')
$envArgs = if ($Environment) { @('--environment', $Environment) } else { @() }

# Companion first: its steps run against plugin types the main solution owns.
foreach ($name in @($manifest.DisabledSolutionName, $manifest.SolutionName)) {
    Write-Host "Deleting $name..."
    $output = & pac @(@('solution', 'delete', '--solution-name', $name) + $envArgs) 2>&1 |
        ForEach-Object { "$_" }

    if ($LASTEXITCODE -eq 0) {
        continue
    }

    if ($output -match 'not found|does not exist|Cannot find') {
        Write-Host "  not installed, nothing to remove."
        continue
    }

    throw "Deleting $name failed:`r`n$($output -join "`r`n")"
}

Write-Host ''
Write-Host 'Unregistered. The TestPlugins assembly and all of its steps are gone.'
