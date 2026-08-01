$pluginDir = "C:\Users\kk\Downloads\XrmToolbox\Plugins"
if (-not (Test-Path $pluginDir)) { New-Item -ItemType Directory -Path $pluginDir | Out-Null }

$source = Join-Path $PSScriptRoot "PluginDocumenter\bin\Debug\net48\PluginDocumenter.dll"
Copy-Item $source -Destination $pluginDir -Force

Write-Host "Deployed PluginDocumenter.dll to $pluginDir"
