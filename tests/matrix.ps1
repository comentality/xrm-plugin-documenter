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
# assembly's own namespace taken off the front. Cosmetic, and nothing the documenter reads.
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

    # The name Dataverse generates when nobody supplies one. The documenter is supposed to
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
