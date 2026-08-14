<#
    Shared derivations from registrations.psd1.

    build.ps1 writes the registration and verify.ps1 reads it back, so both have to agree
    exactly on ids and on the step name Dataverse would have generated. That agreement
    lives here rather than in two places that can drift apart.

    Dot-source it: . (Join-Path $PSScriptRoot 'matrix.ps1')
#>

# All fixture ids share a prefix, which is also how every query in the suite tells the
# test registration apart from anything else in the environment.
$script:IdPrefix = '9a5b3c10-0000-4a00-9000-'

function Get-StepId {
    param([hashtable] $Step)

    "$($script:IdPrefix)000000000$($Step.Id)"
}

function Get-ImageId {
    param([hashtable] $Step, [int] $Index)

    # Derived rather than counted, so an image keeps its id however the steps are grouped
    # into solutions. One digit of index is plenty; no fixture has nine images on a step.
    if ($Index -lt 1 -or $Index -gt 9) {
        throw "Image index $Index is out of range for step $($Step.Id)."
    }

    "$($script:IdPrefix)00000003$($Step.Id)$Index"
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

function ConvertTo-XmlText {
    param([string] $Value)

    if ($null -eq $Value) { return '' }
    $Value.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;').Replace('"', '&quot;')
}
