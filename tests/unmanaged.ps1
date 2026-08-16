<#
    The half of the fixture that is not a solution.

    An assembly whose matrix entry has no Solution is registered the way somebody working
    in a development environment registers one: the assembly, its plugin types, its steps
    and their images written straight into the organization as unmanaged records that
    belong to no solution but the Default one. That is where the documenter is actually
    pointed - at a plugin being written, not at a solution being shipped - and nothing
    else in this suite reaches it.

    It is also the only shape the suite cannot import and delete in one move, which is why
    it is here rather than in build.ps1: there is no zip, and taking it away again means
    deleting every record by hand, children first.

    Dot-source it: . (Join-Path $PSScriptRoot 'unmanaged.ps1')
#>

Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'matrix.ps1')
. (Join-Path $PSScriptRoot 'dataverse.ps1')

function Get-UnmanagedAssemblies {
    param([Parameter(Mandatory)] [hashtable] $Manifest)

    , @($Manifest.Assemblies | Where-Object { -not $_.Solution })
}

# Every message the unmanaged steps use, resolved once. A step needs the message it runs
# on and, unless it is on a global message, the filter that ties that message to an
# entity - which is a record in its own right and not something a name can stand in for.
function Resolve-StepTargets {
    param([Parameter(Mandatory)] [object[]] $Steps)

    $messages = @{}
    foreach ($name in @($Steps.Step.Message | Sort-Object -Unique)) {
        $rows = Get-DataverseRecords "sdkmessages?`$select=sdkmessageid&`$filter=name eq '$name'"
        if ($rows.Count -eq 0) {
            throw "The environment has no SDK message called '$name'."
        }
        $messages[$name] = $rows[0].sdkmessageid
    }

    $filters = @{}
    foreach ($entry in $Steps) {
        $entity = Get-StepEntity $entry.Step
        if ($entity -eq 'none') { continue }

        $key = "$($entry.Step.Message)/$entity"
        if ($filters.ContainsKey($key)) { continue }

        $messageId = $messages[$entry.Step.Message]
        $rows = Get-DataverseRecords ("sdkmessagefilters?`$select=sdkmessagefilterid" +
            "&`$filter=_sdkmessageid_value eq $messageId and primaryobjecttypecode eq '$entity'")
        if ($rows.Count -eq 0) {
            throw "The environment has no $($entry.Step.Message) filter for '$entity'."
        }
        $filters[$key] = $rows[0].sdkmessagefilterid
    }

    [pscustomobject]@{ Messages = $messages; Filters = $filters }
}

<#
    Registers every unmanaged assembly in the matrix. $Built is build.ps1's record of what
    each assembly turned out to be - where its DLL is and what it is signed with - so the
    public key token written here is the one the file really carries.

    Every write is a PATCH at the fixture's own id, which creates the record if it is not
    there and brings it back into line if it is, so this is as safe to re-run as importing
    the same solution twice.
#>
function Register-Unmanaged {
    param(
        [Parameter(Mandatory)] [hashtable] $Manifest,
        [Parameter(Mandatory)] [hashtable] $Built,
        [string] $Environment
    )

    $assemblies = Get-UnmanagedAssemblies $Manifest
    if ($assemblies.Count -eq 0) { return }

    Connect-Dataverse -Environment $Environment | Out-Null

    $steps = @(foreach ($assembly in $assemblies) {
        foreach ($step in $assembly.Steps) { [pscustomobject]@{ Assembly = $assembly; Step = $step } }
    })
    $targets = Resolve-StepTargets $steps

    # Impersonation is a lookup, and the two routes reach it differently: a solution
    # carries ImpersonatingUserIdName and lets the importer resolve the name, while a
    # record written by hand has to name the id. WhoAmI answers with the caller's, which is
    # the same user the solution route would have named - whoever ran register.ps1.
    $impersonatedUserId = $null
    if (@($steps | Where-Object { Get-StepValue $_.Step 'Impersonate' $false }).Count -gt 0) {
        $impersonatedUserId = (Invoke-Dataverse -Method GET -Path 'WhoAmI').UserId
        if (-not $impersonatedUserId) { throw 'WhoAmI did not answer with a user id.' }
    }

    foreach ($assembly in $assemblies) {
        Write-Host "Registering $($assembly.Name) unmanaged..."

        $info = $Built[$assembly.Name]
        $assemblyId = Get-AssemblyId $assembly

        Set-DataverseRecord -Set 'pluginassemblies' -Id $assemblyId -Body @{
            name           = $assembly.Name
            version        = '1.0.0.0'
            culture        = 'neutral'
            publickeytoken = $info.Token
            # 0 is "stored in the database", which is what the registration tool uploads;
            # 2 is sandbox isolation, which online requires of anything not Microsoft's.
            sourcetype     = 0
            isolationmode  = 2
            content        = [Convert]::ToBase64String([IO.File]::ReadAllBytes($info.Dll))
            description    = 'Registered by hand, in no solution, by the Plugin Documenter end to end suite.'
        }

        foreach ($type in $assembly.Types) {
            Set-DataverseRecord -Set 'plugintypes' -Id (Get-TypeId $assembly $type) -Body @{
                name                          = $type.Name
                typename                      = $type.Name
                friendlyname                  = Get-FriendlyName $assembly $type.Name
                description                   = Get-StepValue $type 'Description' $null
                isworkflowactivity            = $false
                'pluginassemblyid@odata.bind' = "/pluginassemblies($assemblyId)"
            }
        }

        foreach ($step in $assembly.Steps) {
            $stepId = Get-StepId $assembly $step
            $entity = Get-StepEntity $step

            $type = @($assembly.Types | Where-Object { $_.Name -eq $step.Type })
            if ($type.Count -ne 1) {
                throw "$($step.Type) has a step in $($assembly.Name) but is not in that assembly's Types."
            }
            $typeId = Get-TypeId $assembly $type[0]

            $body = @{
                name                = Get-StepName $step
                stage               = $step.Stage
                mode                = $step.Mode
                rank                = $step.Rank
                supporteddeployment = 0
                asyncautodelete     = [bool](Get-StepValue $step 'AsyncAutoDelete' $false)
                configuration       = Get-StepValue $step 'Configuration' $null
                description         = Get-StepValue $step 'Description' $null
                filteringattributes = Get-StepValue $step 'Filter' $null
                # The plugin type goes on EventHandler, which is polymorphic - a step can
                # equally run a service endpoint - so the binding has to name which kind
                # this is. PluginTypeId, which is what the documenter's query joins on, is
                # the platform's to fill in from it.
                'eventhandler_plugintype@odata.bind' = "/plugintypes($typeId)"
                'sdkmessageid@odata.bind'            = "/sdkmessages($($targets.Messages[$step.Message]))"
            }

            if ($entity -ne 'none') {
                $body['sdkmessagefilterid@odata.bind'] =
                    "/sdkmessagefilters($($targets.Filters["$($step.Message)/$entity"]))"
            }

            # Written only when the matrix asks for it. Clearing the lookup again would be
            # a DELETE against the reference rather than a null in the body, and no step
            # here ever stops impersonating - the solution route does not clear it either.
            if (Get-StepValue $step 'Impersonate' $false) {
                $body['impersonatinguserid@odata.bind'] = "/systemusers($impersonatedUserId)"
            }

            Set-DataverseRecord -Set 'sdkmessageprocessingsteps' -Id $stepId -Body $body

            # State is its own write, and is made every time rather than only when the step
            # is meant to be off: a step somebody disabled by hand has to come back enabled
            # when the fixture is registered again.
            $disabled = [bool](Get-StepValue $step 'Disabled' $false)
            Set-DataverseRecord -Set 'sdkmessageprocessingsteps' -Id $stepId -Body @{
                statecode  = $(if ($disabled) { 1 } else { 0 })
                statuscode = $(if ($disabled) { 2 } else { 1 })
            }

            $images = Get-StepImages $step
            for ($i = 0; $i -lt $images.Count; $i++) {
                $image = $images[$i]
                Set-DataverseRecord -Set 'sdkmessageprocessingstepimages' `
                    -Id (Get-ImageId $assembly $step ($i + 1)) -Body @{
                        name                = $image.Name
                        entityalias         = $image.Alias
                        imagetype           = $image.Type
                        messagepropertyname = Get-StepValue $image 'Property' 'Target'
                        attributes          = Get-StepValue $image 'Attributes' $null
                        'sdkmessageprocessingstepid@odata.bind' = "/sdkmessageprocessingsteps($stepId)"
                    }
            }
        }

        Write-Host "  $($assembly.Types.Count) plugin type(s), $($assembly.Steps.Count) step(s)."
    }
}

<#
    Deletes everything Register-Unmanaged wrote, children before parents - an assembly
    still holding plugin types will not delete, and neither will a type still holding
    steps. A record that has already gone is not a failure; this runs at any time.
#>
function Unregister-Unmanaged {
    param(
        [Parameter(Mandatory)] [hashtable] $Manifest,
        [string] $Environment
    )

    $assemblies = Get-UnmanagedAssemblies $Manifest
    if ($assemblies.Count -eq 0) { return }

    Connect-Dataverse -Environment $Environment | Out-Null

    foreach ($assembly in $assemblies) {
        Write-Host "Deleting $($assembly.Name) and its registrations..."
        $deleted = 0

        foreach ($step in $assembly.Steps) {
            $images = Get-StepImages $step
            for ($i = 0; $i -lt $images.Count; $i++) {
                if (Remove-DataverseRecord 'sdkmessageprocessingstepimages' (Get-ImageId $assembly $step ($i + 1))) {
                    $deleted++
                }
            }
        }

        foreach ($step in $assembly.Steps) {
            if (Remove-DataverseRecord 'sdkmessageprocessingsteps' (Get-StepId $assembly $step)) {
                $deleted++
            }
        }

        foreach ($type in $assembly.Types) {
            if (Remove-DataverseRecord 'plugintypes' (Get-TypeId $assembly $type)) {
                $deleted++
            }
        }

        if (Remove-DataverseRecord 'pluginassemblies' (Get-AssemblyId $assembly)) {
            $deleted++
        }

        if ($deleted -eq 0) {
            Write-Host '  not registered, nothing to remove.'
        } else {
            Write-Host "  $deleted record(s) deleted."
        }
    }
}
