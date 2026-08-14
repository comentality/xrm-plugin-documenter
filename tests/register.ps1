<#
.SYNOPSIS
    Registers the whole test matrix in a Dataverse environment.

.DESCRIPTION
    Builds the fixture solutions and imports them:

      PluginDocumenterE2E          the assembly, its plugin types and every enabled step,
                                   imported with --activate-plugins
      PluginDocumenterE2EDisabled  the steps that are meant to stay switched off,
                                   imported without it

    Both are managed, so unregister.ps1 can take them away again completely.

    Safe to run repeatedly: importing the same solution again updates it in place.

.EXAMPLE
    .\register.ps1
    Registers against the active organization of the current pac auth profile.

.EXAMPLE
    .\register.ps1 -Environment https://contoso.crm.dynamics.com
#>
[CmdletBinding()]
param(
    [string] $Environment,
    [switch] $SkipAssemblyBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$envArgs = if ($Environment) { @('--environment', $Environment) } else { @() }

$zips = & (Join-Path $root 'build.ps1') -Environment $Environment -SkipAssemblyBuild:$SkipAssemblyBuild

function Import-Fixture {
    param([string] $Path, [switch] $Activate)

    $arguments = @('solution', 'import', '--path', $Path) + $envArgs
    if ($Activate) { $arguments += '--activate-plugins' }

    Write-Host "Importing $(Split-Path $Path -Leaf)$(if ($Activate) { ' (activating steps)' })..."
    $output = & pac @arguments 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) {
        throw "Import of $Path failed:`r`n$($output -join "`r`n")"
    }
}

Import-Fixture -Path $zips.Main -Activate
if ($zips.Companion) {
    Import-Fixture -Path $zips.Companion
}

Write-Host ''
Write-Host 'Registered. Point the Plugin Documenter at the TestPlugins assembly and the folder:'
Write-Host "  $(Join-Path $root 'TestPlugins')"
Write-Host 'Run verify.ps1 to confirm the environment matches registrations.psd1.'
