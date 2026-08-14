# Builds the tool and launches a private XrmToolBox instance that contains nothing but it.
#
# The instance lives in tests\.xtb and is created from scratch, so it cannot disturb the
# XrmToolBox you use for real work: its own Plugins folder, its own settings, its own
# connection list. Delete the folder to undo everything this script did.
#
#   .\xtb.ps1              # build, wire up, launch
#   .\xtb.ps1 -Reset       # throw the instance away and rebuild it
#   .\xtb.ps1 -ResetAuth   # forget the cached sign in, which -Reset leaves alone
#   .\xtb.ps1 -NoLaunch    # set it up without starting XrmToolBox
#
# The connection points at the active organization of the current pac auth profile, which
# is the same environment register.ps1 and verify.ps1 talk to. Pass -Environment <url> to
# aim somewhere else.
#
# Everything that is XrmToolBox rather than Plugin Documenter now lives in the XtbSandbox
# module (github.com/comentality/xrmtoolbox-sandbox), shared with the other tools.

param(
    [string]$Environment,
    [string]$XrmToolBoxPath,
    [switch]$Reset,
    [switch]$ResetAuth,
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"

if (-not (Get-Module -ListAvailable XtbSandbox)) {
    throw "XtbSandbox is not installed. Run: Install-Module XtbSandbox -Scope CurrentUser`n(see https://github.com/comentality/xrmtoolbox-sandbox)"
}
Import-Module XtbSandbox

Start-XtbSandbox @PSBoundParameters `
    -InstanceRoot   (Join-Path $PSScriptRoot ".xtb") `
    -ProjectPath    (Join-Path $PSScriptRoot "..\PluginDocumenter\PluginDocumenter.csproj") `
    -ToolName       "Plugin Documenter" `
    -ConnectionName "PluginDocumenter E2E" `
    -Clipboard      (Join-Path $PSScriptRoot "TestPlugins")
