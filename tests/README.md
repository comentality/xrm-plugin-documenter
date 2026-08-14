# End to end tests

Empty plugins registered every way the documenter has to describe, so a run can be judged
against something other than "looks about right".

```powershell
.\register.ps1      # build the assembly, pack two solutions, import them
.\verify.ps1        # confirm the environment matches registrations.psd1
.\unregister.ps1    # take it all away again
```

All three use the active organization of the current `pac` auth profile; pass
`-Environment <url>` to target another. `register.ps1` is safe to re-run.

Then run the Plugin Documenter against the environment, pick **TestPlugins**, point it at
`tests/TestPlugins`, and compare what it writes with [Expected output](#expected-output)
below. `dotnet build tests/TestPlugins` afterwards is itself a check: the project compiles
against the same `XrmToolsMetaAttributes.cs` the tool emits, so attributes that do not
compile break the build.

## The parts

| | |
|---|---|
| `registrations.psd1` | The test matrix. One entry per step; the only file to edit to change what gets registered. |
| `TestPlugins/` | Twelve empty `IPlugin` classes, one per behaviour being pinned. Each file's summary says which. |
| `solution/src/` | The solution the steps are packed into: publisher, manifest, and the plugin assembly metadata. |
| `matrix.ps1` | Ids and generated step names, shared so `build.ps1` and `verify.ps1` cannot disagree. |
| `build.ps1` | Turns the matrix into two solution zips. `register.ps1` calls it; run it alone to inspect the zips. |

`TestPlugins.snk` is committed on purpose. The public key token is part of every
`AssemblyQualifiedName` in the fixture, so the key has to be the same everywhere. It signs
nothing anyone should trust.

## Why two solutions

Both are **managed**, and that is load bearing: deleting an unmanaged solution leaves every
component behind in the Default solution, so `unregister.ps1` would unregister nothing.

They are split because the solution format has no element for a step's state. `StateCode`
is not in the schema and the importer ignores one if you invent it. Every step lands
*disabled* unless the import is run with `--activate-plugins`, which then enables all of
them. So the steps the matrix marks `Disabled` go into a companion solution imported
without that flag:

| Solution | Contents | Imported |
|---|---|---|
| `PluginDocumenterE2E` | assembly, plugin types, 17 steps | `--activate-plugins` |
| `PluginDocumenterE2EDisabled` | 1 step | plain, so it stays off |

## Expected output

Eighteen steps across twelve registered plugin types, of which the documenter should list
**ten**: `NeverRegistered` and `Beta.Duplicate` have no steps and must not appear at all.

Steps are written in execution order: stage, then rank, then message name. `[Image]` binds
to the nearest preceding `[Step]`, so that order is load bearing.

### SimpleCreate

Everything at its default, so everything optional is suppressed: rank 1, the step name
Dataverse generated, no filter, no description.

```csharp
[Plugin]
[Step("Create", "account", Stages.PostOperation, ExecutionMode.Synchronous)]
```
```csharp
/// <remarks>
/// Register:
/// Sync Post-Create of account (order 1)
/// </remarks>
```

### FilteredUpdate

Filtering attributes as the third positional argument; the type description reaches
`[Plugin]`. The image's name, alias and message property are all defaults, so only its
columns are written.

```csharp
[Plugin(Description = "Keeps the contact name fields in step with the parent account.")]
[Step("Update", "contact", "firstname,lastname,emailaddress1", Stages.PreOperation, ExecutionMode.Synchronous)]
[Image(ImageTypes.PreImage, "firstname,lastname")]
```
```csharp
/// Sync Pre-Update of contact (order 1): firstname, lastname, emailaddress1
///     PreImage: firstname, lastname
```

### AsyncWorker

Every named argument at once, on a line long enough to force the wrap.

```csharp
[Plugin]
[Step("Update", "account", "name,telephone1", Stages.PostOperation, ExecutionMode.Asynchronous,
    Name = "Recalculate rollups",
    ExecutionOrder = 25,
    Description = "Runs after the write completes.",
    AsyncAutoDelete = true)]
```
```csharp
/// Async Post-Update of account (order 25): name, telephone1
```

### GlobalMessageHandler

No primary entity: the constructor overload without one, and a summary line with no
`of <entity>`.

```csharp
[Plugin]
[Step("Associate", Stages.PreValidation, ExecutionMode.Synchronous)]
```
```csharp
/// Sync PreValidation-Associate (order 1)
```

### ImageShapes

A post image with no columns at all and a message property that is not `Target`; two images
on one step; and image type 2. Images within a step come out pre before post.

```csharp
[Plugin]
[Step("Create", "account", Stages.PostOperation, ExecutionMode.Synchronous)]
[Image(ImageTypes.PostImage, MessagePropertyName = "Id")]
[Step("Update", "account", Stages.PostOperation, ExecutionMode.Synchronous, ExecutionOrder = 5)]
[Image(ImageTypes.PreImage, "name,telephone1", Name = "Before", EntityAlias = "Before")]
[Image(ImageTypes.PostImage, "name")]
[Step("Update", "account", Stages.PostOperation, ExecutionMode.Synchronous, ExecutionOrder = 7)]
[Image(ImageTypes.Both, "name", Name = "Snapshot", EntityAlias = "Snapshot")]
```
```csharp
/// Sync Post-Create of account (order 1)
///     PostImage: (all columns)
/// Sync Post-Update of account (order 5)
///     PreImage: name, telephone1
///     PostImage: name
/// Sync Post-Update of account (order 7)
///     PreImage and PostImage: name
```

### DisabledAndImpersonated

The two facts only the comment can carry. In attribute mode both steps have to look
completely ordinary. `As <name>` is whoever ran `register.ps1`.

```csharp
[Plugin]
[Step("Delete", "task", Stages.PreOperation, ExecutionMode.Synchronous)]
[Step("Update", "task", Stages.PostOperation, ExecutionMode.Synchronous)]
```
```csharp
/// Sync Pre-Delete of task (order 1, disabled)
/// Sync Post-Update of task (order 1, As Kosta Koniev)
```

### EscapedText

Quote, backslash, tab and newline through the C# literal writer. The step's description
arrives with `\n` rather than `\r\n`: XML normalises line endings inside an element, so
what went into the solution as CRLF comes back as LF.

The summary comment says none of this — names, descriptions and configuration are
deliberately left out of it - which is also what keeps the doc comment well formed.

```csharp
[Plugin(Description = "Quote \" backslash \\ ampersand & angle <tag> all in one description.")]
[Step("Update", "account", Stages.PostOperation, ExecutionMode.Synchronous,
    Name = "Quote \" backslash \\ ampersand & angle <tag>",
    Description = "First line, with a tab\there.\nSecond line.",
    Configuration = "C:\\path\\to \"somewhere\" & back")]
```
```csharp
/// Sync Post-Update of account (order 1)
```

### WideRegistration

Six steps registered scrambled. Two pairs tie on stage and rank and are separated only by
message name. The long filter list stays on one line as an attribute - the emitter only
wraps when there are named arguments to wrap onto - and wraps in the comment, where the
limit is the line width.

```csharp
[Plugin]
[Step("Update", "account", Stages.PreValidation, ExecutionMode.Synchronous, ExecutionOrder = 3)]
[Step("Create", "account", Stages.PreOperation, ExecutionMode.Synchronous)]
[Step("Delete", "account", Stages.PreOperation, ExecutionMode.Synchronous)]
[Step("Update", "account", "accountcategorycode,accountnumber,address1_city,address1_line1,creditlimit,description,emailaddress1,name,telephone1,websiteurl", Stages.PostOperation, ExecutionMode.Synchronous)]
[Step("Create", "account", Stages.PostOperation, ExecutionMode.Asynchronous, ExecutionOrder = 10)]
[Step("Update", "contact", Stages.PostOperation, ExecutionMode.Synchronous, ExecutionOrder = 10)]
```
```csharp
/// Sync PreValidation-Update of account (order 3)
/// Sync Pre-Create of account (order 1)
/// Sync Pre-Delete of account (order 1)
/// Sync Post-Update of account (order 1):
///     accountcategorycode, accountnumber, address1_city, address1_line1, creditlimit, description,
///     emailaddress1, name, telephone1, websiteurl
/// Async Post-Create of account (order 10)
/// Sync Post-Update of contact (order 10)
```

### HandWritten

The file arrives already carrying a summary, a hand written `<remarks>`, an `[Obsolete]`,
a stale `[Step]` and a stale `Register:` block, all describing a registration that no longer
exists. Afterwards:

- the summary, the hand written `<remarks>` and the `[Obsolete]` are untouched
- the stale `[Step]` and the stale `Register:` block are replaced, not appended to
- a second run changes nothing, and leaves no second `.bak`

```csharp
[Plugin]
[Step("Update", "annotation", Stages.PostOperation, ExecutionMode.Synchronous,
    ExecutionOrder = 2,
    Description = "What the file should end up saying, not what it says now.")]
```
```csharp
/// Sync Post-Update of annotation (order 2)
```

### Alpha.Duplicate

Registered, and reported as **ambiguous**: `Duplicate` is declared by both
`Plugins/Duplicates/AlphaDuplicate.cs` and `Plugins/Duplicates/BetaDuplicate.cs`, and the
documenter matches files by short name. Neither file may be modified.

### NeverRegistered and Beta.Duplicate

Plugin types with no steps. They must not appear in the list at all, and their files must
come back from a run byte for byte identical.

## Notes from building this

Things that cost time, in case they cost it again:

- `FullName` and `SourceType` are **attributes** on `PluginAssembly`, and `Name` /
  `AssemblyQualifiedName` are attributes on `PluginType`. Get any of them wrong and the
  import fails with a bare `NullReferenceException` out of `GetPluginAssembliesTable`.
- `SourceType` also decides whether SolutionPackager carries the DLL into the zip. Without
  it the zip packs happily and the import fails the same unhelpful way, so `build.ps1`
  checks the zip for the assembly before handing it over.
- The child elements of both are an `xs:sequence`. Order is not a matter of taste. The
  [solution file schema](https://learn.microsoft.com/power-apps/developer/model-driven-apps/customization-solutions-file-schema)
  is the authority.
- A step needs `EventHandler` and `EventHandlerTypeCode` (4602 for a plugin type). Without
  them the import fails with nothing but the word `EventHandlerTypeCode`.
- `PrimaryEntity` must be omitted for a step on a global message. Writing `none`, which is
  what Dataverse stores, is rejected: the importer resolves it through the metadata cache,
  where there is no entity called `none`.
- An empty `<Description>` element lands as an empty string, not null, so `build.ps1` omits
  empty elements instead of writing them.
- `sdkmessagefilter.primaryobjecttypecode` is an integer to a FetchXML condition and pac
  renders it as the table's *display* name, so `annotation` prints as `Note`. `verify.ps1`
  resolves object type codes up front and filters on those.
