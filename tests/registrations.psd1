<#
    The test matrix.

    Six plugin assemblies, three solutions, two publishers and two strong name keys, so
    that the things the tool has to get right about *several* assemblies - which
    ones it hides, which ones it groups a class under, which class a file belongs to when
    two assemblies claim the same name - are pinned by something other than opinion.

    Two of the six are in no solution at all, because that is what the tool is
    pointed at: an assembly somebody is in the middle of writing, built and registered
    straight into a development environment. Those are the rows the tool shows by default.
    The other four arrived in managed solutions - shipped by somebody, source elsewhere -
    and are behind the Managed switch, which is where the vendor, orphan and stepless
    cases now live.

    build.ps1 turns this file into the assembly metadata, the solution manifests and one
    SdkMessageProcessingStep xml per step, so it is the single place that says what the
    environment will look like when register.ps1 has finished.

    Publishers
      Keyed by name and referenced from a solution. A publisher is not something the
      tool reads - that is the point of there being two of them.

    Solutions
      One per publisher, plus the companion that exists only to leave its steps disabled.

    Assemblies
      Name        assembly name, which is also the pluginassembly record's name.
      Block       one hex digit. Every id in the fixture carries it, so a record can be
                  traced back to its assembly by eye.
      Namespace   stripped from a type name to make its FriendlyName. Nothing more.
      Project     the csproj, relative to tests\.
      Solution    which solution the assembly and its steps are packed into, or $null to
                  register it record by record instead, the way the plugin registration
                  tool does in a development environment. Those records are unmanaged and
                  belong to no solution, so register.ps1 writes them over the Web API and
                  unregister.ps1 deletes them one at a time.
      Source      where the tool would find the source, or $null when it must not
                  find any. Documentation only; what decides it is whether the project
                  lives under tests\src or tests\nosource.
      Types       every plugin type in the assembly, steps or no steps. Id is two hex
                  digits, unique within the assembly.
      Steps       one entry per registered step.

    Step fields:
      Id            two hex digits, unique within the assembly, and the sort key for
                    nothing at all - Dataverse orders by stage and rank.
      Type          the plugin type the step runs, by name, from this assembly.
      Message       SDK message name. Resolved to an sdkmessageid at register time, so
                    the fixture does not carry environment specific GUIDs.
      Entity        primary entity, or omitted for a step on a global message.
      Stage         10 PreValidation, 20 PreOperation, 40 PostOperation.
      Mode          0 synchronous, 1 asynchronous.
      Rank          execution order.
      Name          step name. Omit to get the name Dataverse generates, which the
                    tool is supposed to recognise as a default and not emit.
      Impersonate   run in the context of the user register.ps1 is signed in as.
      Disabled      leave the step registered but switched off. In a solution these go
                    into a separate one, because the format has no element for a step's
                    state and the only lever is whether the import is run with
                    --activate-plugins. An unmanaged step is simply written disabled.
      Images        Type 0 PreImage, 1 PostImage, 2 both. Attributes omitted means every
                    column, which the summary comment has to flag.

    Two of the filter spellings are dynamic, because "nearly every column of the table"
    cannot be a literal - the list depends on the environment. Both are expanded at
    register time against the live table's updatable columns (and by write.ps1 against a
    declared stand-in), and both are only reachable on the unmanaged route; the solution
    route refuses them, since a zip is packed before any environment is in sight.

      FilterAll         = $true      every updatable column, spelled out. Pinned to the
                                     day it was registered, unlike an empty filter, which
                                     is the distinction the tool's experimental
                                     "(all N columns, written out)" phrasing exists for.
      FilterAllExcept   = 'a,b'      every updatable column but these. The names must be
                                     real columns of the table, or registering throws -
                                     the fixture's claim is that the exceptions are
                                     exactly these.
      AttributesAllExcept = 'a'      the same, on an image, expanded against every real
                                     column rather than only the updatable ones.

    Columns are read once at register time, so a table that gains a column afterwards
    drifts out from under the near-complete lists; re-running register.ps1 catches up.
#>
@{
    Publishers = @{
        # The publisher the tool's own fixtures have always been imported under.
        Comentality = @{
            UniqueName        = 'Comentality'
            Name              = 'Comentality'
            Prefix            = 'cmtl'
            OptionValuePrefix = '34429'
        }
        # A second vendor, so that "whose assembly is this" has an answer the tool
        # cannot reach: publisher is not on the pluginassembly record.
        Contoso = @{
            UniqueName        = 'PluginStepCodegenContoso'
            Name              = 'Contoso Ltd'
            Prefix            = 'dpcon'
            OptionValuePrefix = '34431'
        }
    }

    Solutions = @(
        @{
            Name      = 'PluginStepCodegenE2E'
            Title     = 'Plugin Step Codegen E2E Fixtures'
            Publisher = 'Comentality'
        }
        @{
            Name      = 'PluginStepCodegenE2EContoso'
            Title     = 'Plugin Step Codegen E2E Fixtures (Contoso)'
            Publisher = 'Contoso'
        }
    )

    # Every step marked Disabled *in a solution* lands here instead. An unmanaged step is
    # simply written disabled, so only the managed assemblies reach this. Its publisher is
    # Contoso's because the one step it carries runs against a Contoso plugin type, and a
    # solution that installs nothing has no reason to belong to a second publisher.
    DisabledSolution = @{
        Name      = 'PluginStepCodegenE2EDisabled'
        Title     = 'Plugin Step Codegen E2E Fixtures (steps left disabled)'
        Publisher = 'Contoso'
    }

    Assemblies = @(

        # ================================================================= TestPlugins
        # The original fixture: every shape of step, image and free text the emitters have
        # to describe. Signed with its own key, which is what makes it a different vendor
        # from the Contoso assemblies as far as anything visible is concerned.
        #
        # In no solution, and deliberately so: this is the assembly with everything the
        # emitters have to describe, and the tool's own default view is the unmanaged
        # one. Registering it any other way would have put every interesting case behind a
        # switch nobody turns on to do their actual work.
        @{
            Name      = 'TestPlugins'
            Block     = '1'
            Namespace = 'TestPlugins'
            Project   = 'src\TestPlugins\TestPlugins.csproj'
            Solution  = $null
            Source    = 'src\TestPlugins'

            Types = @(
                @{ Id = '01'; Name = 'TestPlugins.SimpleCreate' }
                @{ Id = '02'; Name = 'TestPlugins.FilteredUpdate'
                   Description = 'Keeps the contact name fields in step with the parent account.' }
                @{ Id = '03'; Name = 'TestPlugins.AsyncWorker' }
                @{ Id = '04'; Name = 'TestPlugins.GlobalMessageHandler' }
                @{ Id = '05'; Name = 'TestPlugins.ImageShapes' }
                @{ Id = '06'; Name = 'TestPlugins.DisabledAndImpersonated' }
                # Description carries the characters the C# literal writer has to escape.
                @{ Id = '07'; Name = 'TestPlugins.EscapedText'
                   Description = 'Quote " backslash \ ampersand & angle <tag> all in one description.' }
                @{ Id = '08'; Name = 'TestPlugins.WideRegistration' }
                @{ Id = '09'; Name = 'TestPlugins.HandWritten' }
                # Registered as a type, but deliberately given no steps.
                @{ Id = '0a'; Name = 'TestPlugins.NeverRegistered' }
                @{ Id = '0b'; Name = 'TestPlugins.Alpha.Duplicate' }
                # Also stepless: it exists only so the short name Duplicate matches two
                # files and the registered namespace has a tie to settle.
                @{ Id = '0c'; Name = 'TestPlugins.Beta.Duplicate' }
                # Same short name as Contoso.Crm.Rival, in a file of its own.
                @{ Id = '0d'; Name = 'TestPlugins.Rival' }
                # Declared by src\Shared\Twin.cs, which Contoso.Crm.Plugins links as well.
                @{ Id = '0e'; Name = 'Shared.Twin' }
                # The three shapes of a column list the "(all columns except ...)"
                # experiment has to tell apart.
                @{ Id = '0f'; Name = 'TestPlugins.NearlyAllColumns' }
            )

            Steps = @(

                # --- Baseline: everything at its default, so the emitter has nothing to add. ---
                @{
                    Id = '01'; Type = 'TestPlugins.SimpleCreate'
                    Message = 'Create'; Entity = 'account'; Stage = 40; Mode = 0; Rank = 1
                }

                # --- Filtering attributes, and an image that names its columns. ---
                @{
                    Id = '02'; Type = 'TestPlugins.FilteredUpdate'
                    Message = 'Update'; Entity = 'contact'; Stage = 20; Mode = 0; Rank = 1
                    Filter = 'firstname,lastname,emailaddress1'
                    Images = @(
                        @{ Type = 0; Name = 'PreImage'; Alias = 'PreImage'; Attributes = 'firstname,lastname' }
                    )
                }

                # --- Every named argument at once, on a step long enough to force wrapping. ---
                @{
                    Id = '03'; Type = 'TestPlugins.AsyncWorker'
                    Message = 'Update'; Entity = 'account'; Stage = 40; Mode = 1; Rank = 25
                    Filter = 'name,telephone1'
                    Name = 'Recalculate rollups'
                    Description = 'Runs after the write completes.'
                    AsyncAutoDelete = $true
                }

                # --- Global message: no primary entity anywhere in the output. ---
                @{
                    Id = '04'; Type = 'TestPlugins.GlobalMessageHandler'
                    Message = 'Associate'; Stage = 10; Mode = 0; Rank = 1
                }

                # --- Images. Post image with no columns at all, and a non-Target property. ---
                @{
                    Id = '05'; Type = 'TestPlugins.ImageShapes'
                    Message = 'Create'; Entity = 'account'; Stage = 40; Mode = 0; Rank = 1
                    Images = @(
                        @{ Type = 1; Name = 'PostImage'; Alias = 'PostImage'; Property = 'Id' }
                    )
                }
                # --- Two images on one step, one of them renamed. ---
                @{
                    Id = '06'; Type = 'TestPlugins.ImageShapes'
                    Message = 'Update'; Entity = 'account'; Stage = 40; Mode = 0; Rank = 5
                    Images = @(
                        @{ Type = 0; Name = 'Before'; Alias = 'Before'; Attributes = 'name,telephone1' }
                        @{ Type = 1; Name = 'PostImage'; Alias = 'PostImage'; Attributes = 'name' }
                    )
                }
                # --- Image type 2, the pre and post pair. ---
                @{
                    Id = '07'; Type = 'TestPlugins.ImageShapes'
                    Message = 'Update'; Entity = 'account'; Stage = 40; Mode = 0; Rank = 7
                    Images = @(
                        @{ Type = 2; Name = 'Snapshot'; Alias = 'Snapshot'; Attributes = 'name' }
                    )
                }

                # --- The two facts only the summary comment can carry. ---
                @{
                    Id = '08'; Type = 'TestPlugins.DisabledAndImpersonated'
                    Message = 'Delete'; Entity = 'task'; Stage = 20; Mode = 0; Rank = 1
                    Disabled = $true
                }
                @{
                    Id = '09'; Type = 'TestPlugins.DisabledAndImpersonated'
                    Message = 'Update'; Entity = 'task'; Stage = 40; Mode = 0; Rank = 1
                    Impersonate = $true
                }

                # --- Free text that has to survive being written into a C# string literal. ---
                @{
                    Id = '0a'; Type = 'TestPlugins.EscapedText'
                    Message = 'Update'; Entity = 'account'; Stage = 40; Mode = 0; Rank = 1
                    Name = 'Quote " backslash \ ampersand & angle <tag>'
                    Description = "First line, with a tab`there.`r`nSecond line."
                    Configuration = 'C:\path\to "somewhere" & back'
                }

                # --- Ordering. Registered scrambled; the tool has to sort them. ---
                @{
                    Id = '0b'; Type = 'TestPlugins.WideRegistration'
                    Message = 'Update'; Entity = 'account'; Stage = 10; Mode = 0; Rank = 3
                }
                @{
                    Id = '0c'; Type = 'TestPlugins.WideRegistration'
                    Message = 'Create'; Entity = 'account'; Stage = 20; Mode = 0; Rank = 1
                }
                # Same stage and rank as the one above, so only the message name separates them.
                @{
                    Id = '0d'; Type = 'TestPlugins.WideRegistration'
                    Message = 'Delete'; Entity = 'account'; Stage = 20; Mode = 0; Rank = 1
                }
                # Long enough a filter list to wrap onto continuation lines in the summary.
                @{
                    Id = '0e'; Type = 'TestPlugins.WideRegistration'
                    Message = 'Update'; Entity = 'account'; Stage = 40; Mode = 0; Rank = 1
                    Filter = 'accountcategorycode,accountnumber,address1_city,address1_line1,creditlimit,description,emailaddress1,name,telephone1,websiteurl'
                }
                @{
                    Id = '0f'; Type = 'TestPlugins.WideRegistration'
                    Message = 'Create'; Entity = 'account'; Stage = 40; Mode = 1; Rank = 10
                }
                # Ties with the one above on stage and rank; again the message name decides.
                @{
                    Id = '10'; Type = 'TestPlugins.WideRegistration'
                    Message = 'Update'; Entity = 'contact'; Stage = 40; Mode = 0; Rank = 10
                }

                # --- Deliberately disagrees with the stale [Step] already in HandWritten.cs. ---
                @{
                    Id = '11'; Type = 'TestPlugins.HandWritten'
                    Message = 'Update'; Entity = 'annotation'; Stage = 40; Mode = 0; Rank = 2
                    Description = 'What the file should end up saying, not what it says now.'
                }

                # --- Its short name resolves to two files; the namespace settles it. ---
                @{
                    Id = '12'; Type = 'TestPlugins.Alpha.Duplicate'
                    Message = 'Create'; Entity = 'annotation'; Stage = 40; Mode = 0; Rank = 1
                }

                # --- Collides across assemblies: Contoso.Crm.Rival is the other half. ---
                @{
                    Id = '13'; Type = 'TestPlugins.Rival'
                    Message = 'Delete'; Entity = 'task'; Stage = 20; Mode = 0; Rank = 1
                }

                # --- One file, two assemblies. Contoso.Crm.Plugins registers Shared.Twin too. ---
                @{
                    Id = '14'; Type = 'Shared.Twin'
                    Message = 'Create'; Entity = 'account'; Stage = 40; Mode = 0; Rank = 3
                }

                # --- The dynamic column lists, one shape per step, ranks kept distinct so
                # --- the order the steps come back in never rests on a tie.
                # Nearly every updatable column of annotation, and an image that is every
                # real column but the blob. With the experiment on, both collapse to their
                # exceptions; off, both are recited as registered.
                @{
                    Id = '15'; Type = 'TestPlugins.NearlyAllColumns'
                    Message = 'Update'; Entity = 'annotation'; Stage = 40; Mode = 0; Rank = 1
                    FilterAllExcept = 'notetext,subject'
                    Images = @(
                        @{ Type = 0; Name = 'PreImage'; Alias = 'PreImage'; AttributesAllExcept = 'documentbody' }
                    )
                }
                # Every updatable column of task, spelled out - which is not the same
                # registration as no filter at all, and the comment has to say so.
                @{
                    Id = '16'; Type = 'TestPlugins.NearlyAllColumns'
                    Message = 'Update'; Entity = 'task'; Stage = 40; Mode = 0; Rank = 2
                    FilterAll = $true
                }
                # A filter carrying a name outside the universe it is measured against:
                # createdon is a real column - the Web API writes a dependency per name and
                # refuses one that does not exist, so a deleted column's leftover name
                # cannot be registered from here - but it is not updatable, so it is not in
                # the updatable set the tool diffs a filter against. The odd name out is
                # the finding, and the list must stay verbatim however near-complete the
                # rest of it is; a stale name left behind by a managed layer gets the same
                # treatment through the same code.
                @{
                    Id = '17'; Type = 'TestPlugins.NearlyAllColumns'
                    Message = 'Update'; Entity = 'contact'; Stage = 40; Mode = 0; Rank = 3
                    Filter = 'createdon,firstname,lastname'
                }

                # TestPlugins.NeverRegistered and TestPlugins.Beta.Duplicate are deliberately
                # absent: they are plugin types with no steps, and must not be listed at all.
            )
        }

        # ========================================================= Contoso.Crm.Plugins
        # The second vendor's assembly: its own publisher, its own key, and source sitting
        # in the same folder the tool searches for TestPlugins.
        @{
            Name      = 'Contoso.Crm.Plugins'
            Block     = '2'
            Namespace = 'Contoso.Crm'
            Project   = 'src\ContosoPlugins\ContosoPlugins.csproj'
            Solution  = 'PluginStepCodegenE2EContoso'
            Source    = 'src\ContosoPlugins'

            Types = @(
                @{ Id = '01'; Name = 'Contoso.Crm.Alpha' }
                @{ Id = '02'; Name = 'Contoso.Crm.Charlie' }
                @{ Id = '03'; Name = 'Contoso.Crm.Rival' }
                @{ Id = '04'; Name = 'Shared.Twin' }
            )

            Steps = @(
                # Sorted by type name, Alpha and Charlie sit either side of
                # Contoso.Crm.Bravo, which belongs to Contoso.Crm.Orphan.
                @{
                    Id = '01'; Type = 'Contoso.Crm.Alpha'
                    Message = 'Create'; Entity = 'contact'; Stage = 40; Mode = 0; Rank = 1
                }
                # Everything about this step that only the managed route can say. It is
                # disabled, which a solution has no element for, so it is imported in the
                # companion without --activate-plugins - the one thing the third solution
                # exists to prove. And its description arrives carrying CRLF, which the
                # solution route does not keep: XML normalises line endings inside an
                # element, so the tool emits \n here and \r\n for the same text
                # written over the Web API into TestPlugins.EscapedText.
                @{
                    Id = '02'; Type = 'Contoso.Crm.Charlie'
                    Message = 'Update'; Entity = 'contact'; Stage = 40; Mode = 0; Rank = 1
                    Filter = 'jobtitle'
                    Description = "Held back until Contoso ships it.`r`nSecond line."
                    Disabled = $true
                }
                # The other TestPlugins.Rival. Two files, two assemblies, one short name.
                @{
                    Id = '03'; Type = 'Contoso.Crm.Rival'
                    Message = 'Delete'; Entity = 'contact'; Stage = 20; Mode = 0; Rank = 1
                }
                # The same src\Shared\Twin.cs TestPlugins registers, with different steps.
                @{
                    Id = '04'; Type = 'Shared.Twin'
                    Message = 'Update'; Entity = 'account'; Stage = 20; Mode = 0; Rank = 4
                    Filter = 'name'
                }
            )
        }

        # ========================================================== Contoso.Crm.Orphan
        # Registered, with steps, and with no source in the folder the tool searches.
        @{
            Name      = 'Contoso.Crm.Orphan'
            Block     = '3'
            Namespace = 'Contoso.Crm'
            Project   = 'nosource\OrphanPlugins\OrphanPlugins.csproj'
            Solution  = 'PluginStepCodegenE2EContoso'
            Source    = $null

            Types = @(
                @{ Id = '01'; Name = 'Contoso.Crm.Bravo' }
                @{ Id = '02'; Name = 'Contoso.Crm.Ghost' }
            )

            Steps = @(
                @{
                    Id = '01'; Type = 'Contoso.Crm.Bravo'
                    Message = 'Create'; Entity = 'task'; Stage = 40; Mode = 0; Rank = 1
                }
                # Impersonated, because the solution route carries that as a user's full
                # name and the unmanaged route as an id, and both have to end up saying the
                # same thing. Ghost has no source, so the fact shows up in the preview and
                # in no file - which is all this needs to keep the name route exercised.
                @{
                    Id = '02'; Type = 'Contoso.Crm.Ghost'
                    Message = 'Update'; Entity = 'task'; Stage = 40; Mode = 0; Rank = 2
                    Impersonate = $true
                }
            )
        }

        # =========================================================== Contoso.Crm.Empty
        # Plugin types, no steps anywhere in the assembly. Ticking it must add nothing to
        # the class list and be accounted for on the status line instead.
        @{
            Name      = 'Contoso.Crm.Empty'
            Block     = '4'
            Namespace = 'Contoso.Crm.Empty'
            Project   = 'nosource\EmptyPlugins\EmptyPlugins.csproj'
            Solution  = 'PluginStepCodegenE2EContoso'
            Source    = $null

            Types = @(
                @{ Id = '01'; Name = 'Contoso.Crm.Empty.Idle' }
                @{ Id = '02'; Name = 'Contoso.Crm.Empty.Spare' }
            )

            Steps = @()
        }

        # ================================================ Microsoft.Contoso.Extensions
        # Named Microsoft, signed with the Contoso key. IsMicrosoft asks the signature
        # first and falls back to the name, so this is the assembly the fallback gets
        # wrong: hidden until the Microsoft switch is on, and perfectly documentable once
        # it is.
        #
        # Shipped by Comentality, which makes all three disagree - the name says Microsoft,
        # the signature says Contoso, the publisher says Comentality - and the tool
        # reads only the first two. It is also the only assembly this solution owns, so
        # the two publishers survive TestPlugins leaving the solution route.
        #
        # Managed, on purpose: it is Microsoft's by name and shipped by somebody, and
        # because IsMicrosoft is asked before IsManaged it appears the moment the Microsoft
        # switch goes on however the Managed switch is set. That order is the reason
        # neither switch can be made to show nothing, and this row is what proves it.
        @{
            Name      = 'Microsoft.Contoso.Extensions'
            Block     = '5'
            Namespace = 'Microsoft.Contoso'
            Project   = 'src\MsContosoExtensions\MsContosoExtensions.csproj'
            Solution  = 'PluginStepCodegenE2E'
            Source    = 'src\MsContosoExtensions'

            Types = @(
                @{ Id = '01'; Name = 'Microsoft.Contoso.Renamed' }
            )

            Steps = @(
                @{
                    Id = '01'; Type = 'Microsoft.Contoso.Renamed'
                    Message = 'Update'; Entity = 'account'; Stage = 40; Mode = 0; Rank = 9
                    Filter = 'websiteurl'
                }
            )
        }

        # ======================================================= WorkInProgress.Plugins
        # The second assembly in no solution, and the reason there are two: a developer's
        # environment holds more than one thing they are working on, and the default view
        # has to group classes under two headings without a switch being touched.
        # TestPlugins is the one with every shape of step; this is the one whose classes
        # read like a half finished feature, down to a step name somebody typed by hand.
        @{
            Name      = 'WorkInProgress.Plugins'
            Block     = '6'
            Namespace = 'WorkInProgress'
            Project   = 'src\WorkInProgressPlugins\WorkInProgressPlugins.csproj'
            Solution  = $null
            Source    = 'src\WorkInProgressPlugins'

            Types = @(
                @{ Id = '01'; Name = 'WorkInProgress.NewFeature' }
                @{ Id = '02'; Name = 'WorkInProgress.HalfFinished'
                   Description = 'Not finished, and switched off until it is.' }
                @{ Id = '03'; Name = 'WorkInProgress.Scratch' }
            )

            Steps = @(
                # The plain case on the unmanaged route: everything at its default, so the
                # emitter has nothing to add and nothing to leave out.
                @{
                    Id = '01'; Type = 'WorkInProgress.NewFeature'
                    Message = 'Create'; Entity = 'account'; Stage = 40; Mode = 0; Rank = 1
                }
                # Disabled on the record, not by being imported into another solution.
                @{
                    Id = '02'; Type = 'WorkInProgress.HalfFinished'
                    Message = 'Update'; Entity = 'contact'; Stage = 20; Mode = 0; Rank = 1
                    Filter = 'firstname'
                    Disabled = $true
                }
                # The only step in the fixture whose name was typed by a person into the
                # registration tool, beside two that kept the name it offered. Its image is
                # the ordinary one - a pre image on a delete, taken before the record goes -
                # written through an API that, unlike the solution importer, refuses an
                # image the message cannot supply: a pre image on Create is rejected, and so
                # is a post image on Create whose property is Target rather than Id.
                @{
                    Id = '03'; Type = 'WorkInProgress.Scratch'
                    Message = 'Delete'; Entity = 'task'; Stage = 20; Mode = 0; Rank = 1
                    Name = 'Tidy up after a deleted task'
                    Description = 'Temporary. Remove before this goes anywhere near production.'
                    Images = @(
                        @{ Type = 0; Name = 'PreImage'; Alias = 'PreImage'; Attributes = 'subject,regardingobjectid' }
                    )
                }
            )
        }
    )
}
