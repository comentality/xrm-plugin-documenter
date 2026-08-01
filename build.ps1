$project = Join-Path $PSScriptRoot "PluginDocumenter\PluginDocumenter.csproj"

dotnet build $project -c Debug
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$pluginDir = "C:\Users\kk\Downloads\XrmToolbox\Plugins"
if (-not (Test-Path $pluginDir)) { New-Item -ItemType Directory -Path $pluginDir | Out-Null }

$source = Join-Path $PSScriptRoot "PluginDocumenter\bin\Debug\net48\PluginDocumenter.dll"
Copy-Item $source -Destination $pluginDir -Force

Write-Host "Built and deployed PluginDocumenter.dll to $pluginDir"
