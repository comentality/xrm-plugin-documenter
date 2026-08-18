$pluginDir = "C:\Users\kk\Downloads\XrmToolbox\Plugins"
if (-not (Test-Path $pluginDir)) { New-Item -ItemType Directory -Path $pluginDir | Out-Null }

$source = Join-Path $PSScriptRoot "PluginStepCodegen\bin\Debug\net48\PluginStepCodegen.dll"
Copy-Item $source -Destination $pluginDir -Force

Write-Host "Deployed PluginStepCodegen.dll to $pluginDir"
