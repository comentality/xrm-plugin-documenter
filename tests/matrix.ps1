<#
    Shared derivations from registrations.psd1.

    build.ps1 writes the registration and verify.ps1 reads it back, so both have to agree
    exactly on ids, on the assembly's full name and on the step name Dataverse would have
    generated. That agreement lives here rather than in two places that can drift apart.

    Dot-source it: . (Join-Path $PSScriptRoot 'matrix.ps1')
#>

# All fixture ids share a prefix, which is also how every query in the suite tells the
# test registration apart from anything else in the environment.
$script:IdPrefix = '9a5b3c10-0000-4a00-9000-'

<#
    Ids are laid out so a record can be read back to its assembly by eye. The last twelve
    hex digits of every fixture GUID are:

      assembly     00000000 B 000
      plugin type  00000000 B 1 II
      step         00000000 B 2 II
      image        0000000  B 3 II J

    where B is the assembly's Block, II its two digit Id within that assembly and J the
    one based index of the image on its step. The kind digit keeps the four apart, so
    nothing has to be counted and an image keeps its id however the steps are grouped
    into solutions.
#>
function Get-AssemblyId {
    param([hashtable] $Assembly)

    "$($script:IdPrefix)00000000$($Assembly.Block)000"
}

function Get-TypeId {
    param([hashtable] $Assembly, [hashtable] $Type)

    "$($script:IdPrefix)00000000$($Assembly.Block)1$($Type.Id)"
}

function Get-StepId {
    param([hashtable] $Assembly, [hashtable] $Step)

    "$($script:IdPrefix)00000000$($Assembly.Block)2$($Step.Id)"
}

function Get-ImageId {
    param([hashtable] $Assembly, [hashtable] $Step, [int] $Index)

    # One digit of index is plenty; no fixture has nine images on a step.
    if ($Index -lt 1 -or $Index -gt 9) {
        throw "Image index $Index is out of range for step $($Assembly.Name)/$($Step.Id)."
    }

    "$($script:IdPrefix)0000000$($Assembly.Block)3$($Step.Id)$Index"
}

# Almost everything Dataverse knows about an assembly is read out of this string rather
# than declared separately, so it is built once and used by the metadata, the step's
# PluginTypeName and the solution's root component alike.
function Get-AssemblyFullName {
    param([hashtable] $Assembly, [string] $PublicKeyToken)

    "$($Assembly.Name), Version=1.0.0.0, Culture=neutral, PublicKeyToken=$PublicKeyToken"
}

# What the plugin registration tool would show in its tree: the type name with the
# assembly's own namespace taken off the front. Cosmetic, and nothing the tool reads.
function Get-FriendlyName {
    param([hashtable] $Assembly, [string] $TypeName)

    $prefix = "$($Assembly.Namespace)."
    if ($TypeName.StartsWith($prefix)) { $TypeName.Substring($prefix.Length) } else { $TypeName }
}

function Get-StepEntity {
    param([hashtable] $Step)

    if ($Step.ContainsKey('Entity')) { $Step.Entity } else { 'none' }
}

function Get-StepName {
    param([hashtable] $Step)

    if ($Step.ContainsKey('Name')) {
        return $Step.Name
    }

    # The name Dataverse generates when nobody supplies one. The tool is supposed to
    # recognise it as a default and leave it out of the emitted attribute, which is the
    # whole reason most fixtures do not set Name.
    $entity = Get-StepEntity $Step
    if ($entity -eq 'none') {
        "$($Step.Type): $($Step.Message)"
    } else {
        "$($Step.Type): $($Step.Message) of $entity"
    }
}

function Get-StepImages {
    param([hashtable] $Step)

    # @() because a one image step comes back from the data file as a bare hashtable, and
    # the leading comma because returning an array from a function otherwise unrolls it
    # back into one again.
    $images = if ($Step.ContainsKey('Images')) { $Step.Images } else { @() }
    , @($images)
}

function Get-StepValue {
    param([hashtable] $Step, [string] $Key, $Default = '')

    if ($Step.ContainsKey($Key)) { $Step[$Key] } else { $Default }
}

<#
    The dynamic column lists. A step that wants "every updatable column of the table but
    these two" cannot say so as a literal - the list depends on the environment - so the
    matrix spells the intent (FilterAll, FilterAllExcept, AttributesAllExcept) and the
    caller supplies the columns to expand against: the live table when registering and
    verifying, a declared stand-in when write.ps1 runs headlessly.

    $Columns maps an entity's logical name to @{ Filter = [string[]]; Image = [string[]] },
    split the way the tool splits them in RegistrationQuery.GetEntityColumns: Image is
    every real column (companion attributes already dropped), Filter the updatable subset.
#>
function Get-EntityColumnList {
    param([hashtable] $Columns, [string] $Entity, [string] $View, [string] $Context)

    if ($null -eq $Columns -or -not $Columns.ContainsKey($Entity)) {
        throw "$Context expands against the columns of '$Entity', which were not provided. " +
            'Only the unmanaged route and write.ps1 supply them; the solution route cannot carry a dynamic list.'
    }
    , @($Columns[$Entity][$View])
}

function Expand-ColumnList {
    param([string[]] $Universe, [string[]] $Except, [string] $Context)

    foreach ($name in $Except) {
        # An except that is not in the universe would not shorten the list, it would
        # change its meaning - and the fixture's whole claim is that the exceptions are
        # exactly these. Refuse rather than register something else.
        if ($Universe -notcontains $name) {
            throw "$Context excepts '$name', which is not in the column list it expands against."
        }
    }
    (@($Universe | Where-Object { $Except -notcontains $_ })) -join ','
}

# A step's filtering attributes: the literal Filter unless the step spells one of the two
# dynamic forms, which expand against the entity's updatable columns.
function Get-StepFilter {
    param([hashtable] $Step, [hashtable] $Columns = $null)

    $except = $null
    if ([bool](Get-StepValue $Step 'FilterAll' $false)) { $except = @() }
    if ($Step.ContainsKey('FilterAllExcept')) {
        $except = @($Step.FilterAllExcept -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }
    if ($null -eq $except) {
        return Get-StepValue $Step 'Filter' $null
    }

    $context = "Step $($Step.Id) ($($Step.Type))"
    $universe = Get-EntityColumnList $Columns (Get-StepEntity $Step) 'Filter' $context
    Expand-ColumnList $universe $except $context
}

# An image's attributes: the literal list unless it says AttributesAllExcept, which
# expands against every real column of the step's entity - an image can carry the
# read-only ones a filter cannot.
function Get-ImageAttributes {
    param([hashtable] $Step, [hashtable] $Image, [hashtable] $Columns = $null)

    if (-not $Image.ContainsKey('AttributesAllExcept')) {
        return Get-StepValue $Image 'Attributes' $null
    }

    $except = @($Image.AttributesAllExcept -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $context = "Image $($Image.Name) on step $($Step.Id) ($($Step.Type))"
    $universe = Get-EntityColumnList $Columns (Get-StepEntity $Step) 'Image' $context
    Expand-ColumnList $universe $except $context
}

# The entities whose current columns are needed before the matrix can be written or
# checked: any entity a step or image expands a dynamic list against.
function Get-DynamicColumnEntities {
    param([hashtable] $Manifest)

    # Assigned before the loop: the comma-wrapped return survives a foreach expression
    # un-unrolled, which would hand the whole list to one iteration.
    $allSteps = Get-AllSteps $Manifest
    $entities = @(foreach ($entry in $allSteps) {
        $images = Get-StepImages $entry.Step
        $dynamic = $entry.Step.ContainsKey('FilterAll') -or $entry.Step.ContainsKey('FilterAllExcept') -or
            @($images | Where-Object { $_.ContainsKey('AttributesAllExcept') }).Count -gt 0
        if ($dynamic) { Get-StepEntity $entry.Step }
    })
    , @($entities | Sort-Object -Unique)
}

# Every step of every assembly, each paired with the assembly it belongs to, so the
# scripts that do not care which assembly a step came from can say so.
function Get-AllSteps {
    param([hashtable] $Manifest)

    , @(foreach ($assembly in $Manifest.Assemblies) {
        foreach ($step in $assembly.Steps) {
            [pscustomobject]@{ Assembly = $assembly; Step = $step }
        }
    })
}

# The folder SolutionPackager reads an assembly and its metadata from. The suffix is the
# first eight digits of the id, which every fixture assembly shares.
function Get-AssemblyFolder {
    param([hashtable] $Assembly)

    "PluginAssemblies\$($Assembly.Name)-9A5B3C10"
}

function ConvertTo-XmlText {
    param([string] $Value)

    if ($null -eq $Value) { return '' }
    $Value.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;').Replace('"', '&quot;')
}
