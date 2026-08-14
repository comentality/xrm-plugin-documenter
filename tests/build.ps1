<#
.SYNOPSIS
    Builds the fixture solution zips from registrations.psd1 and the test plugin assembly.

.DESCRIPTION
    Turns the test matrix into importable solutions:

      1. builds TestPlugins.dll
      2. resolves each message name to the target environment's sdkmessageid, and the
         signed in user's full name for the impersonating step, so nothing environment
         specific has to be committed
      3. writes one SdkMessageProcessingStep xml per entry in the matrix
      4. packs two managed solutions with SolutionPackager

    Two, because of a wrinkle worth knowing about: the solution format cannot carry a
    step's state, and every step lands Disabled unless the import is run with
    --activate-plugins, which then enables all of them. So the steps the matrix marks
    Disabled go into a companion solution that register.ps1 imports without that flag.

    Managed, because deleting an unmanaged solution leaves every component behind in the
    Default solution - unregister.ps1 would unregister nothing.

    register.ps1 calls this; run it directly to inspect the zips without importing.
#>
[CmdletBinding()]
param(
    # Environment URL or id. Defaults to the active organization of the pac auth profile.
    [string] $Environment,

    # Reuse the existing TestPlugins.dll instead of rebuilding it.
    [switch] $SkipAssemblyBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
. (Join-Path $root 'matrix.ps1')

$manifest = Import-PowerShellDataFile (Join-Path $root 'registrations.psd1')
$obj      = Join-Path $root 'obj'
$assembly = Join-Path $root 'TestPlugins\bin\Release\net462\TestPlugins.dll'

# The folder SolutionPackager reads the assembly and its metadata from.
$assemblyFolder = 'PluginAssemblies\TestPlugins-9A5B3C10'

$envArgs = if ($Environment) { @('--environment', $Environment) } else { @() }

function Invoke-Pac {
    param([Parameter(Mandatory)] [string[]] $Arguments)

    $output = & pac @Arguments 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) {
        throw "pac $($Arguments -join ' ') failed:`r`n$($output -join "`r`n")"
    }

    $output
}

# pac has no structured output, so pull the id out of the fixed width table it prints.
function Get-IdByName {
    param([string[]] $Output, [string] $Name)

    $row = $Output | Where-Object { $_ -match '^([0-9a-fA-F-]{36})\s+(.+?)\s*$' -and $Matches[2] -eq $Name }
    if (-not $row) {
        throw "Could not find message '$Name' in the environment."
    }

    [void]($row -match '^([0-9a-fA-F-]{36})')
    $Matches[1]
}

# ---------------------------------------------------------------- assembly

if (-not $SkipAssemblyBuild) {
    Write-Host 'Building TestPlugins.dll...'
    # Out-Host, not the pipeline: this script returns the zip paths, and anything else that
    # reaches the pipeline ends up bundled in with them.
    & dotnet build (Join-Path $root 'TestPlugins\TestPlugins.csproj') -c Release -v quiet --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'TestPlugins failed to build.' }
}

if (-not (Test-Path $assembly)) {
    throw "TestPlugins.dll not found at $assembly. Run without -SkipAssemblyBuild."
}

New-Item -ItemType Directory -Path $obj -Force | Out-Null

# Plugin type ids live in the assembly metadata; the steps refer to them by class name.
$data = [xml](Get-Content (Join-Path $root "solution\src\$assemblyFolder\TestPlugins.dll.data.xml") -Raw)
$qualifier = ', ' + $data.PluginAssembly.FullName
$assemblyPath = $data.PluginAssembly.FileName.TrimStart('/')

# Unbraced, which is how the server writes them back out on export.
$typeIds = @{}
foreach ($type in $data.PluginAssembly.PluginTypes.PluginType) {
    $typeIds[$type.Name] = $type.PluginTypeId.Trim('{', '}')
}

# ---------------------------------------------------------------- environment lookups

Write-Host 'Resolving message ids...'
$wanted = $manifest.Steps.Message | Sort-Object -Unique
$fetchFile = Join-Path $obj 'messages.fetch.xml'
Set-Content -Path $fetchFile -Encoding UTF8 -Value @"
<fetch>
  <entity name="sdkmessage">
    <attribute name="sdkmessageid" />
    <attribute name="name" />
    <filter>
      <condition attribute="name" operator="in">
$($wanted | ForEach-Object { "        <value>$_</value>" } | Out-String)      </condition>
    </filter>
  </entity>
</fetch>
"@

$messageIds = @{}
$rows = Invoke-Pac (@('env', 'fetch') + $envArgs + @('--xmlFile', $fetchFile))
foreach ($name in $wanted) { $messageIds[$name] = Get-IdByName -Output $rows -Name $name }

# The schema carries impersonation as ImpersonatingUserIdName, so what is needed is the
# user's full name - which is also exactly what the documenter reads back and prints.
$impersonatedUserName = $null
if ($manifest.Steps | Where-Object { $_.ContainsKey('Impersonate') -and $_.Impersonate }) {
    $who = Invoke-Pac (@('env', 'who') + $envArgs)
    $line = $who | Where-Object { $_ -match 'User ID:\s+([0-9a-fA-F-]{36})' }
    if (-not $line) { throw 'Could not read the signed in user id from pac env who.' }
    [void]($line -match '([0-9a-fA-F-]{36})')
    $userId = $Matches[1]

    $userFetch = Join-Path $obj 'user.fetch.xml'
    Set-Content -Path $userFetch -Encoding UTF8 -Value @"
<fetch>
  <entity name="systemuser">
    <attribute name="fullname" />
    <filter>
      <condition attribute="systemuserid" operator="eq" value="$userId" />
    </filter>
  </entity>
</fetch>
"@

    $userRows = Invoke-Pac (@('env', 'fetch') + $envArgs + @('--xmlFile', $userFetch))
    # fetch always prints the primary key alongside whatever was asked for, so the row is
    # the name with the id stuck on one end of it.
    $impersonatedUserName = (($userRows |
        Where-Object { $_ -match '[0-9a-fA-F-]{36}' -and $_ -notmatch '^(Connected|Microsoft|Version)' } |
        Select-Object -Last 1) -replace '[0-9a-fA-F-]{36}', '').Trim()
    if (-not $impersonatedUserName) { throw "Could not read the full name of user $userId." }

    Write-Host "Impersonating step will run as '$impersonatedUserName'."
}

# ---------------------------------------------------------------- step xml

function New-StepXml {
    param([hashtable] $Step)

    $typeName = $Step.Type
    if (-not $typeIds.ContainsKey($typeName)) {
        throw "$typeName is in registrations.psd1 but not in TestPlugins.dll.data.xml."
    }

    $stepId = Get-StepId $Step
    $entity = Get-StepEntity $Step
    $name = Get-StepName $Step

    $filter = Get-StepValue $Step 'Filter'
    $description = Get-StepValue $Step 'Description'
    $configuration = Get-StepValue $Step 'Configuration'
    $autoDelete = if (Get-StepValue $Step 'AsyncAutoDelete' $false) { 1 } else { 0 }

    # Child elements are an xs:sequence, so the order below is the schema's and not a
    # matter of taste. EventHandler is the plugin type the step runs and 4602 is the object
    # type code that says so; leave the pair out and the import fails with nothing but the
    # word "EventHandlerTypeCode".
    $xml = New-Object System.Text.StringBuilder
    [void]$xml.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
    [void]$xml.AppendLine("<SdkMessageProcessingStep SdkMessageProcessingStepId=`"{$stepId}`" Name=`"$(ConvertTo-XmlText $name)`">")
    [void]$xml.AppendLine("  <PluginTypeName>$(ConvertTo-XmlText ($typeName + $qualifier))</PluginTypeName>")
    [void]$xml.AppendLine("  <PluginTypeId>$($typeIds[$typeName])</PluginTypeId>")

    # "none" is how Dataverse stores "no entity" on the filter, but the importer resolves
    # PrimaryEntity through the metadata cache, where there is no entity called none. A
    # step on a global message simply leaves it out.
    if ($entity -ne 'none') {
        [void]$xml.AppendLine("  <PrimaryEntity>$entity</PrimaryEntity>")
    }

    [void]$xml.AppendLine("  <AsyncAutoDelete>$autoDelete</AsyncAutoDelete>")

    # Omitted rather than written empty. An empty element lands as an empty string, and a
    # step with a zero length description is not the same thing as a step with none.
    if ($configuration) {
        [void]$xml.AppendLine("  <Configuration>$(ConvertTo-XmlText $configuration)</Configuration>")
    }
    if ($description) {
        [void]$xml.AppendLine("  <Description>$(ConvertTo-XmlText $description)</Description>")
    }
    if ($filter) {
        [void]$xml.AppendLine("  <FilteringAttributes>$(ConvertTo-XmlText $filter)</FilteringAttributes>")
    }

    # Impersonation travels by name, not by id, so it survives a move between environments.
    if (Get-StepValue $Step 'Impersonate' $false) {
        [void]$xml.AppendLine("  <ImpersonatingUserIdName>$(ConvertTo-XmlText $impersonatedUserName)</ImpersonatingUserIdName>")
    }

    [void]$xml.AppendLine('  <InvocationSource>0</InvocationSource>')
    [void]$xml.AppendLine("  <Mode>$($Step.Mode)</Mode>")
    [void]$xml.AppendLine("  <Rank>$($Step.Rank)</Rank>")
    [void]$xml.AppendLine("  <SdkMessageId>$($messageIds[$Step.Message])</SdkMessageId>")
    [void]$xml.AppendLine("  <EventHandler>$($typeIds[$typeName])</EventHandler>")
    [void]$xml.AppendLine('  <EventHandlerTypeCode>4602</EventHandlerTypeCode>')
    [void]$xml.AppendLine("  <Stage>$($Step.Stage)</Stage>")
    [void]$xml.AppendLine('  <IsCustomizable>1</IsCustomizable>')
    [void]$xml.AppendLine('  <IsHidden>0</IsHidden>')
    [void]$xml.AppendLine('  <SupportedDeployment>0</SupportedDeployment>')
    [void]$xml.AppendLine('  <IntroducedVersion>1.0</IntroducedVersion>')

    # There is deliberately no StateCode here. The schema has no element for it and the
    # importer ignores one if you invent it; which solution a step lands in is what decides
    # its state.

    $images = Get-StepImages $Step
    if ($images.Count -eq 0) {
        [void]$xml.AppendLine('  <SdkMessageProcessingStepImages />')
    } else {
        [void]$xml.AppendLine('  <SdkMessageProcessingStepImages>')
        for ($i = 0; $i -lt $images.Count; $i++) {
            $image = $images[$i]
            $imageId = Get-ImageId $Step ($i + 1)
            $attributes = Get-StepValue $image 'Attributes'
            $property = Get-StepValue $image 'Property' 'Target'

            [void]$xml.AppendLine("    <SdkMessageProcessingStepImage Name=`"$(ConvertTo-XmlText $image.Name)`">")
            [void]$xml.AppendLine("      <SdkMessageProcessingStepImageId>{$imageId}</SdkMessageProcessingStepImageId>")
            [void]$xml.AppendLine("      <Attributes>$(ConvertTo-XmlText $attributes)</Attributes>")
            [void]$xml.AppendLine("      <EntityAlias>$(ConvertTo-XmlText $image.Alias)</EntityAlias>")
            [void]$xml.AppendLine("      <ImageType>$($image.Type)</ImageType>")
            [void]$xml.AppendLine("      <MessagePropertyName>$property</MessagePropertyName>")
            [void]$xml.AppendLine('      <IsCustomizable>1</IsCustomizable>')
            [void]$xml.AppendLine('      <IntroducedVersion>1.0</IntroducedVersion>')
            [void]$xml.AppendLine('    </SdkMessageProcessingStepImage>')
        }
        [void]$xml.AppendLine('  </SdkMessageProcessingStepImages>')
    }

    [void]$xml.AppendLine('</SdkMessageProcessingStep>')
    $xml.ToString()
}

# ---------------------------------------------------------------- packing

function New-FixtureSolution {
    param(
        [string] $UniqueName,
        [string] $Title,
        [hashtable[]] $Steps,
        # Only the main solution owns the assembly. The companion just registers steps
        # against the plugin types the main solution already installed.
        [switch] $WithAssembly
    )

    $stage = Join-Path $obj "src-$UniqueName"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    Copy-Item (Join-Path $root 'solution\src\*') $stage -Recurse -Force

    if ($WithAssembly) {
        Copy-Item $assembly (Join-Path $stage $assemblyFolder) -Force
    } else {
        Remove-Item (Join-Path $stage 'PluginAssemblies') -Recurse -Force
    }

    $stepFolder = Join-Path $stage 'SdkMessageProcessingSteps'
    New-Item -ItemType Directory -Path $stepFolder -Force | Out-Null

    $rootComponents = New-Object System.Text.StringBuilder
    foreach ($step in $Steps) {
        $fileName = '{0}-{1}.xml' -f $step.Id, ($step.Type -replace '^TestPlugins\.', '')
        Set-Content -Path (Join-Path $stepFolder $fileName) -Value (New-StepXml $step) -Encoding UTF8 -NoNewline
        [void]$rootComponents.AppendLine(
            "      <RootComponent type=`"92`" id=`"{$(Get-StepId $step)}`" behavior=`"0`" />")
    }

    $solutionFile = Join-Path $stage 'Other\Solution.xml'
    $solution = Get-Content $solutionFile -Raw

    $marker = '<!-- STEP-ROOT-COMPONENTS: one type="92" entry per step in registrations.psd1, written by build.ps1. -->'
    if (-not $solution.Contains($marker)) {
        throw 'Solution.xml no longer carries the RootComponents marker build.ps1 writes into.'
    }
    $solution = $solution.Replace('      ' + $marker, $rootComponents.ToString().TrimEnd())

    if (-not $WithAssembly) {
        $solution = $solution -replace '(?m)^\s*<RootComponent type="91".*\r?\n', ''
    }

    $solution = $solution.Replace("<UniqueName>$($manifest.SolutionName)</UniqueName>", "<UniqueName>$UniqueName</UniqueName>")
    $solution = $solution.Replace('description="Plugin Documenter E2E Fixtures"', "description=`"$(ConvertTo-XmlText $Title)`"")
    Set-Content -Path $solutionFile -Value $solution -Encoding UTF8 -NoNewline

    $zip = Join-Path $obj "$UniqueName.zip"
    Write-Host "Packing $($Steps.Count) step(s) into $UniqueName.zip..."
    Invoke-Pac @('solution', 'pack', '--zipfile', $zip, '--folder', $stage, '--packagetype', 'Managed') | Out-Null

    # SolutionPackager only carries the assembly into the zip if it could make sense of the
    # metadata, and says nothing when it could not - the import is then what fails, with a
    # NullReferenceException that names nothing. Catch it here instead.
    if ($WithAssembly) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [IO.Compression.ZipFile]::OpenRead($zip)
        try {
            if (-not ($archive.Entries | Where-Object { $_.FullName -eq $assemblyPath })) {
                throw "SolutionPackager left $assemblyPath out of $zip. Check TestPlugins.dll.data.xml against the solution file schema."
            }
        } finally {
            $archive.Dispose()
        }
    }

    $zip
}

$enabled  = @($manifest.Steps | Where-Object { -not (Get-StepValue $_ 'Disabled' $false) })
$disabled = @($manifest.Steps | Where-Object { Get-StepValue $_ 'Disabled' $false })

$main = New-FixtureSolution -UniqueName $manifest.SolutionName -WithAssembly `
    -Title 'Plugin Documenter E2E Fixtures' -Steps $enabled

$companion = $null
if ($disabled.Count -gt 0) {
    $companion = New-FixtureSolution -UniqueName $manifest.DisabledSolutionName `
        -Title 'Plugin Documenter E2E Fixtures (steps left disabled)' -Steps $disabled
}

[pscustomobject]@{
    Main      = $main
    Companion = $companion
}
