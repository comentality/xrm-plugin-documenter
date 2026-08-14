<#
    The test matrix.

    One entry per registered step. build.ps1 turns each of these into an
    SdkMessageProcessingStep in the fixture solution, so this file is the single place
    that says what the environment will look like when register.ps1 has finished - and
    therefore what the documenter has to describe. Keep it readable; that is the point
    of having it instead of eighteen near identical XML files.

    Fields:
      Id            last three hex digits of the step's GUID; unique, and the sort key
                    for nothing at all - Dataverse orders by stage and rank.
      Type          class in TestPlugins.dll the step is registered against.
      Message       SDK message name. Resolved to an sdkmessageid at register time, so
                    the fixture does not carry environment specific GUIDs.
      Entity        primary entity, or omitted for a step on a global message.
      Stage         10 PreValidation, 20 PreOperation, 40 PostOperation.
      Mode          0 synchronous, 1 asynchronous.
      Rank          execution order.
      Name          step name. Omit to get the name Dataverse generates, which the
                    documenter is supposed to recognise as a default and not emit.
      Impersonate   run in the context of the user register.ps1 is signed in as.
      Disabled      leave the step registered but switched off. These go into a separate
                    solution, because the format has no element for a step's state and the
                    only lever is whether the import is run with --activate-plugins.
      Images        Type 0 PreImage, 1 PostImage, 2 both. Attributes omitted means every
                    column, which the summary comment has to flag.
#>
@{
    Assembly             = 'TestPlugins'
    AssemblyId           = '9a5b3c10-0000-4a00-9000-000000000001'
    SolutionName         = 'PluginDocumenterE2E'
    DisabledSolutionName = 'PluginDocumenterE2EDisabled'

    Steps = @(

        # --- Baseline: everything at its default, so the emitter has nothing to add. ---
        @{
            Id = '201'; Type = 'TestPlugins.SimpleCreate'
            Message = 'Create'; Entity = 'account'; Stage = 40; Mode = 0; Rank = 1
        }

        # --- Filtering attributes, and an image that names its columns. ---
        @{
            Id = '202'; Type = 'TestPlugins.FilteredUpdate'
            Message = 'Update'; Entity = 'contact'; Stage = 20; Mode = 0; Rank = 1
            Filter = 'firstname,lastname,emailaddress1'
            Images = @(
                @{ Type = 0; Name = 'PreImage'; Alias = 'PreImage'; Attributes = 'firstname,lastname' }
            )
        }

        # --- Every named argument at once, on a step long enough to force wrapping. ---
        @{
            Id = '203'; Type = 'TestPlugins.AsyncWorker'
            Message = 'Update'; Entity = 'account'; Stage = 40; Mode = 1; Rank = 25
            Filter = 'name,telephone1'
            Name = 'Recalculate rollups'
            Description = 'Runs after the write completes.'
            AsyncAutoDelete = $true
        }

        # --- Global message: no primary entity anywhere in the output. ---
        @{
            Id = '204'; Type = 'TestPlugins.GlobalMessageHandler'
            Message = 'Associate'; Stage = 10; Mode = 0; Rank = 1
        }

        # --- Images. Post image with no columns at all, and a non-Target property. ---
        @{
            Id = '205'; Type = 'TestPlugins.ImageShapes'
            Message = 'Create'; Entity = 'account'; Stage = 40; Mode = 0; Rank = 1
            Images = @(
                @{ Type = 1; Name = 'PostImage'; Alias = 'PostImage'; Property = 'Id' }
            )
        }
        # --- Two images on one step, one of them renamed. ---
        @{
            Id = '206'; Type = 'TestPlugins.ImageShapes'
            Message = 'Update'; Entity = 'account'; Stage = 40; Mode = 0; Rank = 5
            Images = @(
                @{ Type = 0; Name = 'Before'; Alias = 'Before'; Attributes = 'name,telephone1' }
                @{ Type = 1; Name = 'PostImage'; Alias = 'PostImage'; Attributes = 'name' }
            )
        }
        # --- Image type 2, the pre and post pair. ---
        @{
            Id = '207'; Type = 'TestPlugins.ImageShapes'
            Message = 'Update'; Entity = 'account'; Stage = 40; Mode = 0; Rank = 7
            Images = @(
                @{ Type = 2; Name = 'Snapshot'; Alias = 'Snapshot'; Attributes = 'name' }
            )
        }

        # --- The two facts only the summary comment can carry. ---
        @{
            Id = '208'; Type = 'TestPlugins.DisabledAndImpersonated'
            Message = 'Delete'; Entity = 'task'; Stage = 20; Mode = 0; Rank = 1
            Disabled = $true
        }
        @{
            Id = '209'; Type = 'TestPlugins.DisabledAndImpersonated'
            Message = 'Update'; Entity = 'task'; Stage = 40; Mode = 0; Rank = 1
            Impersonate = $true
        }

        # --- Free text that has to survive being written into a C# string literal. ---
        @{
            Id = '20a'; Type = 'TestPlugins.EscapedText'
            Message = 'Update'; Entity = 'account'; Stage = 40; Mode = 0; Rank = 1
            Name = 'Quote " backslash \ ampersand & angle <tag>'
            Description = "First line, with a tab`there.`r`nSecond line."
            Configuration = 'C:\path\to "somewhere" & back'
        }

        # --- Ordering. Registered scrambled; the documenter has to sort them. ---
        @{
            Id = '20b'; Type = 'TestPlugins.WideRegistration'
            Message = 'Update'; Entity = 'account'; Stage = 10; Mode = 0; Rank = 3
        }
        @{
            Id = '20c'; Type = 'TestPlugins.WideRegistration'
            Message = 'Create'; Entity = 'account'; Stage = 20; Mode = 0; Rank = 1
        }
        # Same stage and rank as the one above, so only the message name separates them.
        @{
            Id = '20d'; Type = 'TestPlugins.WideRegistration'
            Message = 'Delete'; Entity = 'account'; Stage = 20; Mode = 0; Rank = 1
        }
        # Long enough a filter list to wrap onto continuation lines in the summary.
        @{
            Id = '20e'; Type = 'TestPlugins.WideRegistration'
            Message = 'Update'; Entity = 'account'; Stage = 40; Mode = 0; Rank = 1
            Filter = 'accountcategorycode,accountnumber,address1_city,address1_line1,creditlimit,description,emailaddress1,name,telephone1,websiteurl'
        }
        @{
            Id = '20f'; Type = 'TestPlugins.WideRegistration'
            Message = 'Create'; Entity = 'account'; Stage = 40; Mode = 1; Rank = 10
        }
        # Ties with the one above on stage and rank; again the message name decides.
        @{
            Id = '210'; Type = 'TestPlugins.WideRegistration'
            Message = 'Update'; Entity = 'contact'; Stage = 40; Mode = 0; Rank = 10
        }

        # --- Deliberately disagrees with the stale [Step] already in HandWritten.cs. ---
        @{
            Id = '211'; Type = 'TestPlugins.HandWritten'
            Message = 'Update'; Entity = 'annotation'; Stage = 40; Mode = 0; Rank = 2
            Description = 'What the file should end up saying, not what it says now.'
        }

        # --- Registered, but its short name resolves to two files. ---
        @{
            Id = '212'; Type = 'TestPlugins.Alpha.Duplicate'
            Message = 'Create'; Entity = 'annotation'; Stage = 40; Mode = 0; Rank = 1
        }

        # TestPlugins.NeverRegistered and TestPlugins.Beta.Duplicate are deliberately
        # absent: they are plugin types with no steps, and must not be listed at all.
    )
}
