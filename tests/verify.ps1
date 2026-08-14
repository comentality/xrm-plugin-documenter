<#
.SYNOPSIS
    Checks that the environment actually matches registrations.psd1.

.DESCRIPTION
    Run after register.ps1, before pointing the documenter at the environment: if this
    fails, anything the documenter writes is being judged against the wrong registration.

    Every check is a FetchXML query whose filter spells out what the step or image is
    supposed to be, asking the server to agree rather than parsing values back out of
    pac's fixed width tables. A row means the environment matches; no row means it does
    not, and the check names what it was looking for.
#>
[CmdletBinding()]
param([string] $Environment)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
. (Join-Path $root 'matrix.ps1')

$manifest = Import-PowerShellDataFile (Join-Path $root 'registrations.psd1')
$envArgs = if ($Environment) { @('--environment', $Environment) } else { @() }
$scratch = Join-Path $root 'obj\verify.fetch.xml'
New-Item -ItemType Directory -Path (Join-Path $root 'obj') -Force | Out-Null

$script:passed = 0
$script:failures = @()

function Invoke-Fetch {
    param([string] $Xml)

    Set-Content -Path $scratch -Value $Xml -Encoding UTF8
    $output = & pac @(@('env', 'fetch') + $envArgs + @('--xmlFile', $scratch)) 2>&1 |
        ForEach-Object { "$_" }

    if ($LASTEXITCODE -ne 0) {
        throw "pac env fetch failed:`r`n$($output -join "`r`n")"
    }

    $output -join "`n"
}

function Test-Fetch {
    param([string] $Description, [string] $Xml, [string[]] $Expect)

    $output = Invoke-Fetch $Xml
    $missing = @($Expect | Where-Object { $output -notmatch [regex]::Escape($_) })

    if ($missing.Count -eq 0) {
        $script:passed++
        Write-Host "  ok    $Description"
    } else {
        $script:failures += "$Description (no row with $($missing -join ', '))"
        Write-Host "  FAIL  $Description" -ForegroundColor Red
    }
}

# A condition on a value that may legitimately be empty. Dataverse stores an empty string
# as null, so "no filtering attributes" has to be asked for as null, not as "".
function New-Condition {
    param([string] $Attribute, [string] $Value)

    if ([string]::IsNullOrEmpty($Value)) {
        "      <condition attribute=`"$Attribute`" operator=`"null`" />"
    } else {
        "      <condition attribute=`"$Attribute`" operator=`"eq`" value=`"$(ConvertTo-XmlText $Value)`" />"
    }
}

# XML normalises tabs and newlines inside an attribute value into spaces, so free text that
# contains either cannot be matched exactly. Match the part before the first one instead.
function New-TextCondition {
    param([string] $Attribute, [string] $Value)

    if ([string]::IsNullOrEmpty($Value)) {
        return New-Condition $Attribute $Value
    }

    $head = ($Value -split "[`t`r`n]")[0]
    if ($head -eq $Value) {
        New-Condition $Attribute $Value
    } else {
        "      <condition attribute=`"$Attribute`" operator=`"like`" value=`"$(ConvertTo-XmlText $head)%`" />"
    }
}

# A step's filtered entity lives on sdkmessagefilter as primaryobjecttypecode, which is an
# integer as far as a condition is concerned, and which pac renders as the entity's display
# name ("Note" for annotation) rather than its logical name. So neither the obvious filter
# nor the obvious string comparison works: resolve the codes first and filter on those.
$entities = @($manifest.Steps | ForEach-Object { Get-StepEntity $_ } | Where-Object { $_ -ne 'none' } | Sort-Object -Unique)
$objectTypeCodes = @{}
if ($entities.Count -gt 0) {
    $rows = Invoke-Fetch @"
<fetch>
  <entity name="entity">
    <attribute name="logicalname" />
    <attribute name="objecttypecode" />
    <filter>
      <condition attribute="logicalname" operator="in">
$($entities | ForEach-Object { "        <value>$_</value>" } | Out-String)      </condition>
    </filter>
  </entity>
</fetch>
"@

    foreach ($line in $rows -split "`n") {
        # pac prints large codes with a thousands separator, hence the comma strip.
        if ($line -match '^(\S+)\s+([\d,]+)\s+[0-9a-fA-F-]{36}') {
            $objectTypeCodes[$Matches[1]] = $Matches[2].Replace(',', '')
        }
    }

    foreach ($name in $entities) {
        if (-not $objectTypeCodes.ContainsKey($name)) {
            throw "Could not resolve an object type code for '$name'. Is the table in this environment?"
        }
    }
}

Write-Host "Checking $($manifest.Steps.Count) steps against the environment..."

# ---------------------------------------------------------------- the assembly

Test-Fetch -Description 'assembly TestPlugins is visible to the documenter' -Expect $manifest.AssemblyId -Xml @"
<fetch>
  <entity name="pluginassembly">
    <attribute name="pluginassemblyid" />
    <filter>
      <condition attribute="name" operator="eq" value="$($manifest.Assembly)" />
      <condition attribute="pluginassemblyid" operator="eq" value="$($manifest.AssemblyId)" />
      <condition attribute="isolationmode" operator="eq" value="2" />
      <!-- The two conditions the documenter's own assembly query uses. -->
      <condition attribute="ishidden" operator="eq" value="false" />
      <condition attribute="customizationlevel" operator="eq" value="1" />
    </filter>
  </entity>
</fetch>
"@

# ---------------------------------------------------------------- steps

foreach ($step in $manifest.Steps) {
    $stepId = Get-StepId $step
    $entity = Get-StepEntity $step
    $state = if (Get-StepValue $step 'Disabled' $false) { 1 } else { 0 }

    $conditions = @(
        "      <condition attribute=`"sdkmessageprocessingstepid`" operator=`"eq`" value=`"$stepId`" />"
        "      <condition attribute=`"stage`" operator=`"eq`" value=`"$($step.Stage)`" />"
        "      <condition attribute=`"mode`" operator=`"eq`" value=`"$($step.Mode)`" />"
        "      <condition attribute=`"rank`" operator=`"eq`" value=`"$($step.Rank)`" />"
        "      <condition attribute=`"statecode`" operator=`"eq`" value=`"$state`" />"
        "      <condition attribute=`"name`" operator=`"eq`" value=`"$(ConvertTo-XmlText (Get-StepName $step))`" />"
        "      <condition attribute=`"asyncautodelete`" operator=`"eq`" value=`"$(([bool](Get-StepValue $step 'AsyncAutoDelete' $false)).ToString().ToLower())`" />"
        (New-Condition 'filteringattributes' (Get-StepValue $step 'Filter'))
        (New-TextCondition 'description' (Get-StepValue $step 'Description'))
        (New-TextCondition 'configuration' (Get-StepValue $step 'Configuration'))
    )

    # The message name and the filtered entity live on linked records, not on the step.
    $links = @"
    <link-entity name="sdkmessage" from="sdkmessageid" to="sdkmessageid" alias="m">
      <filter>
        <condition attribute="name" operator="eq" value="$($step.Message)" />
      </filter>
    </link-entity>
    <link-entity name="plugintype" from="plugintypeid" to="plugintypeid" alias="t">
      <filter>
        <condition attribute="typename" operator="eq" value="$($step.Type)" />
      </filter>
    </link-entity>
"@

    if ($entity -eq 'none') {
        # A step on a global message has no filter at all.
        $conditions += '      <condition attribute="sdkmessagefilterid" operator="null" />'
    } else {
        $links += @"
    <link-entity name="sdkmessagefilter" from="sdkmessagefilterid" to="sdkmessagefilterid" alias="f">
      <filter>
        <condition attribute="primaryobjecttypecode" operator="eq" value="$($objectTypeCodes[$entity])" />
      </filter>
    </link-entity>
"@
    }

    if (Get-StepValue $step 'Impersonate' $false) {
        # Whoever registered is whoever the step impersonates, so there is no fixed name to
        # assert; that the link resolves at all is the fact worth checking.
        $links += @"
    <link-entity name="systemuser" from="systemuserid" to="impersonatinguserid" alias="u" link-type="inner">
      <attribute name="fullname" />
    </link-entity>
"@
    } else {
        $conditions += '      <condition attribute="impersonatinguserid" operator="null" />'
    }

    $what = '{0} {1} {2}{3} rank {4}' -f $step.Id, $step.Type.Replace('TestPlugins.', ''),
        $step.Message, $(if ($entity -eq 'none') { '' } else { " of $entity" }), $step.Rank

    Test-Fetch -Description $what -Expect $stepId -Xml @"
<fetch>
  <entity name="sdkmessageprocessingstep">
    <attribute name="sdkmessageprocessingstepid" />
    <filter type="and">
$($conditions -join "`r`n")
    </filter>
$links
  </entity>
</fetch>
"@

    $images = Get-StepImages $step
    for ($i = 0; $i -lt $images.Count; $i++) {
        $image = $images[$i]
        $imageId = Get-ImageId $step ($i + 1)

        $imageConditions = @(
            "      <condition attribute=`"sdkmessageprocessingstepimageid`" operator=`"eq`" value=`"$imageId`" />"
            "      <condition attribute=`"sdkmessageprocessingstepid`" operator=`"eq`" value=`"$stepId`" />"
            "      <condition attribute=`"imagetype`" operator=`"eq`" value=`"$($image.Type)`" />"
            "      <condition attribute=`"name`" operator=`"eq`" value=`"$(ConvertTo-XmlText $image.Name)`" />"
            "      <condition attribute=`"entityalias`" operator=`"eq`" value=`"$(ConvertTo-XmlText $image.Alias)`" />"
            "      <condition attribute=`"messagepropertyname`" operator=`"eq`" value=`"$(Get-StepValue $image 'Property' 'Target')`" />"
            (New-Condition 'attributes' (Get-StepValue $image 'Attributes'))
        )

        Test-Fetch -Description "  image $($image.Name) on $($step.Id)" -Expect $imageId -Xml @"
<fetch>
  <entity name="sdkmessageprocessingstepimage">
    <attribute name="sdkmessageprocessingstepimageid" />
    <filter type="and">
$($imageConditions -join "`r`n")
    </filter>
  </entity>
</fetch>
"@
    }
}

# ---------------------------------------------------------------- nothing extra

# The suite is only meaningful if the environment holds exactly the matrix and no more, so
# count what is there rather than only checking that each expected step exists.
$all = Invoke-Fetch @"
<fetch>
  <entity name="sdkmessageprocessingstep">
    <attribute name="sdkmessageprocessingstepid" />
    <link-entity name="plugintype" from="plugintypeid" to="plugintypeid" alias="t">
      <link-entity name="pluginassembly" from="pluginassemblyid" to="pluginassemblyid" alias="a">
        <filter>
          <condition attribute="name" operator="eq" value="$($manifest.Assembly)" />
        </filter>
      </link-entity>
    </link-entity>
  </entity>
</fetch>
"@

$found = ([regex]::Matches($all, '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}')).Count
if ($found -eq $manifest.Steps.Count) {
    $script:passed++
    Write-Host "  ok    exactly $found steps registered against TestPlugins"
} else {
    $script:failures += "step count is $found, expected $($manifest.Steps.Count)"
    Write-Host "  FAIL  step count is $found, expected $($manifest.Steps.Count)" -ForegroundColor Red
}

# NeverRegistered and Beta.Duplicate exist as plugin types but must have no steps, so the
# documenter has something it is required to leave out of its list.
foreach ($stepless in @('TestPlugins.NeverRegistered', 'TestPlugins.Beta.Duplicate')) {
    $rows = Invoke-Fetch @"
<fetch>
  <entity name="plugintype">
    <attribute name="plugintypeid" />
    <filter>
      <condition attribute="typename" operator="eq" value="$stepless" />
    </filter>
    <link-entity name="sdkmessageprocessingstep" from="plugintypeid" to="plugintypeid" alias="s" link-type="inner">
      <attribute name="sdkmessageprocessingstepid" />
    </link-entity>
  </entity>
</fetch>
"@

    if ($rows -match 'No results returned') {
        $script:passed++
        Write-Host "  ok    $stepless is registered with no steps"
    } else {
        $script:failures += "$stepless has steps and should not"
        Write-Host "  FAIL  $stepless has steps and should not" -ForegroundColor Red
    }
}

# ---------------------------------------------------------------- result

Write-Host ''
if ($script:failures.Count -eq 0) {
    Write-Host "All $($script:passed) checks passed." -ForegroundColor Green
    exit 0
}

Write-Host "$($script:failures.Count) of $($script:passed + $script:failures.Count) checks failed:" -ForegroundColor Red
$script:failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
exit 1
