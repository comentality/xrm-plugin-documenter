# End to end tests

Five plugin assemblies, two publishers and three solutions of empty plugins, registered
every way the documenter has to describe, so a run can be judged against something other
than "looks about right".

```powershell
.\register.ps1      # build the assemblies, pack three solutions, import them
.\verify.ps1        # confirm the environment matches registrations.psd1
.\unregister.ps1    # take it all away again
.\xtb.ps1           # build the tool and open it in an XrmToolBox of its own
```

All four use the active organization of the current `pac` auth profile; pass
`-Environment <url>` to target another. `register.ps1` is safe to re-run.

`xtb.ps1` leaves you looking at the tool, connected, with the source folder on the
clipboard. Compare what it writes with [Expected output](#expected-output) below.
`dotnet build tests\src\TestPlugins` afterwards is itself a check: the project compiles
against the same `XrmToolsMetaAttributes.cs` the tool emits, so attributes that do not
compile break the build.

## The source folder

The folder the documenter is pointed at is `tests\src`, and it holds the source of
**several** assemblies at once - which is the normal shape of a plugin repository and the
only way to reach the things that can only go wrong when a class name is not unique across
one:

```
tests\src\                    <- point the tool here
    TestPlugins\              Comentality's assembly
    ContosoPlugins\           Contoso's assembly
    MsContosoExtensions\      the assembly named Microsoft
    Shared\                   Twin.cs, compiled into two of them
tests\nosource\               deliberately out of reach
    OrphanPlugins\            registered, and the tool must not find its source
    EmptyPlugins\             registered, and has no steps to document anyway
```

An assembly has to be a real signed DLL before Dataverse will accept it, so everything
under `nosource` still gets built - it just does not live where the tool searches.

## The test bench

`xtb.ps1` builds a whole XrmToolBox in `tests\.xtb` and starts it with
`/overridepath`, so it has its own tools folder, its own settings and its own connection
list. Nothing it does can reach the XrmToolBox you work in, and deleting the folder undoes
all of it. The instance holds exactly one tool, so the tools list is the tool.

```powershell
.\xtb.ps1              # build, wire up, launch
.\xtb.ps1 -Reset       # throw the instance away and build it again
.\xtb.ps1 -NoLaunch    # set it up without starting anything
```

The connection is an ordinary OAuth connection against the client id XrmToolBox itself
uses, so it signs you in interactively once and reads the cached token after that. The only
thing left to type is the source folder, which the script puts on the clipboard.

`xtb.ps1` itself is fifteen lines. Everything in it that is XrmToolBox rather than Plugin
Documenter lives in the [XtbSandbox](https://github.com/comentality/xrmtoolbox-sandbox)
module, shared with the other XrmToolBox tools here, so install it once:

```powershell
Install-Module XtbSandbox -Scope CurrentUser
```

The details that cost an afternoon each — `ReplyUrl` is not `RedirectUri`, `/plugin:` and
`/connection:` are read off the raw command line, `/overridepath` does not isolate as much
as it looks — are written down in that module's README rather than here, so there is one
copy of them to be wrong.

## The parts

| | |
|---|---|
| `registrations.psd1` | The test matrix. Assemblies, publishers, solutions, plugin types and steps; the only file to edit to change what gets registered. |
| `src/`, `nosource/` | The plugin projects. Each class's summary says which behaviour it pins. |
| `keys/Contoso.snk` | The strong name key the four Contoso assemblies share. |
| `solution/` | The solution manifest template and the two static files every solution carries. |
| `matrix.ps1` | Ids, assembly full names and generated step names, shared so `build.ps1` and `verify.ps1` cannot disagree. |
| `build.ps1` | Turns the matrix into three solution zips. `register.ps1` calls it; run it alone to inspect the zips. |

`TestPlugins.snk` and `keys/Contoso.snk` are committed on purpose. The public key token is
part of every `AssemblyQualifiedName` in the fixture, and telling one vendor from another
is exactly what the documenter uses a token for, so the keys have to be the same
everywhere. They sign nothing anyone should trust.

## The assemblies

| Assembly | Publisher | Key | Source | Why it is here |
|---|---|---|---|---|
| `TestPlugins` | Comentality | its own | `src\TestPlugins` | Every shape of step, image and free text the emitters have to describe. |
| `Contoso.Crm.Plugins` | Contoso | Contoso | `src\ContosoPlugins` | A second vendor in the same source folder: different publisher, different signature, colliding class names. |
| `Contoso.Crm.Orphan` | Contoso | Contoso | none | Registered, with steps, and no `.cs` anywhere the tool will look. |
| `Contoso.Crm.Empty` | Contoso | Contoso | none | Plugin types and not one step against any of them. |
| `Microsoft.Contoso.Extensions` | Contoso | Contoso | `src\MsContosoExtensions` | Named Microsoft, signed by Contoso. The case `IsMicrosoft` gets wrong on purpose. |

Two publishers and two keys, and the two do not line up: `Microsoft.Contoso.Extensions`
carries the same signature as its plainly-not-Microsoft neighbours. Publisher is not on
the `pluginassembly` record at all, which is the point of there being one to ignore.

## Why three solutions

All three are **managed**, and that is load bearing: deleting an unmanaged solution leaves
every component behind in the Default solution, so `unregister.ps1` would unregister
nothing.

A solution has exactly one publisher, so the two vendors need one each. The third exists
because the solution format has no element for a step's state: `StateCode` is not in the
schema and the importer ignores one if you invent it. Every step lands *disabled* unless
the import is run with `--activate-plugins`, which then enables all of them. So the steps
the matrix marks `Disabled` go into a companion solution imported without that flag.

| Solution | Publisher | Contents | Imported |
|---|---|---|---|
| `PluginDocumenterE2E` | Comentality | `TestPlugins`, 14 plugin types, 19 steps | `--activate-plugins` |
| `PluginDocumenterE2EContoso` | Contoso | the four Contoso assemblies, 9 plugin types, 7 steps | `--activate-plugins` |
| `PluginDocumenterE2EDisabled` | Comentality | 1 step | plain, so it stays off |

The companion goes last, because its step runs against a plugin type the first solution
installs. `unregister.ps1` deletes it first for the same reason.

# Expected output

Twenty seven steps across twenty three registered plugin types in five assemblies.

## The assembly list

With nothing typed in the filter box and the Microsoft switch off, the list shows **four**
of the fixture's five, in name order:

```
Contoso.Crm.Empty        Sandbox
Contoso.Crm.Orphan       Sandbox
Contoso.Crm.Plugins      Sandbox
TestPlugins              Sandbox
```

`Microsoft.Contoso.Extensions` is missing, and is counted on the switch instead - the count
there is the fixture's one plus however many Microsoft really ships into the environment.
It is signed with the Contoso key, so nothing about its signature says Microsoft; it is
hidden on the strength of its name alone, and the switch is the only thing that brings it
back.

Ticking all four:

```
4 assemblies · 18 of 18 classes · 1 with no steps
```

`Contoso.Crm.Empty` is the one with no steps. It contributes no group at all to the class
list - an empty group draws nothing - so the status line is the only place it is
accounted for.

Things worth doing to the list once it is loaded:

- Type `Contoso` in the filter. Three rows, and the "All" box ticks those three and goes
  indeterminate rather than checked. Turn the Microsoft switch on and the same filter
  shows four: the switch and the filter compose rather than override each other.
- Tick `TestPlugins`, then filter it out of view. It stays ticked, its classes stay in the
  list under their own heading, and the status line says `· 1 out of view`.

## The class list and the preview

Classes are grouped under the assembly they were registered from, assemblies in name
order and classes within one in type name order. That is worth checking rather than
assuming, because the environment hands all of the types back in one list sorted by type
name, in which `Contoso.Crm.Alpha`, `Contoso.Crm.Bravo` and `Contoso.Crm.Charlie` belong
to *two different assemblies* in alternation. Both the list and the preview have to
regroup them, so each assembly's `// =====` heading appears exactly once:

```
// ===== Contoso.Crm.Orphan
// ===== Contoso.Crm.Plugins
// ===== TestPlugins
```

`Shared.Twin` sorts ahead of everything named `TestPlugins.*`, so it is the first class
under the TestPlugins heading and the last under Contoso's.

## TestPlugins

### SimpleCreate

Everything at its default, so everything optional is suppressed: rank 1, the step name
Dataverse generated, no description. No filter either, which on `Create` means every column
and is said so rather than left blank.

```csharp
[Plugin]
[Step("Create", "account", Stages.PostOperation, ExecutionMode.Synchronous)]
```
```csharp
/// <remarks>
/// Register:
/// Sync Post-Create of account (order 1): (all columns)
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

`Associate` has no columns to filter, so the header stands alone where a `Create` or
`Update` step would say `(all columns)`.

### ImageShapes

A post image with no columns at all and a message property that is not `Target`; two images
on one step; and image type 2. Images within a step come out pre before post. None of these
steps is filtered, so every header carries `(all columns)` too.

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
/// Sync Post-Create of account (order 1): (all columns)
///     PostImage: (all columns)
/// Sync Post-Update of account (order 5): (all columns)
///     PreImage: name, telephone1
///     PostImage: name
/// Sync Post-Update of account (order 7): (all columns)
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
/// Sync Post-Update of task (order 1, As Kosta Koniev): (all columns)
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
/// Sync Post-Update of account (order 1): (all columns)
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
/// Sync PreValidation-Update of account (order 3): (all columns)
/// Sync Pre-Create of account (order 1): (all columns)
/// Sync Pre-Delete of account (order 1)
/// Sync Post-Update of account (order 1):
///     accountcategorycode, accountnumber, address1_city, address1_line1, creditlimit, description,
///     emailaddress1, name, telephone1, websiteurl
/// Async Post-Create of account (order 10): (all columns)
/// Sync Post-Update of contact (order 10): (all columns)
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
/// Sync Post-Update of annotation (order 2): (all columns)
```

### Alpha.Duplicate

Registered, and reported as **ambiguous**: `Duplicate` is declared by both
`TestPlugins\Plugins\Duplicates\AlphaDuplicate.cs` and `BetaDuplicate.cs`, and the
documenter matches files by short name. Neither file may be modified.

### NeverRegistered and Beta.Duplicate

Plugin types with no steps. They must not appear in the list at all, and their files must
come back from a run byte for byte identical.

## Contoso.Crm.Plugins

### Alpha and Charlie

Two ordinary classes in the second vendor's assembly, documented exactly as if they were
in the first. What they are really for is the interleave described above: sorted by type
name they sit either side of `Contoso.Crm.Bravo`, which belongs to `Contoso.Crm.Orphan`.

```csharp
[Plugin]
[Step("Create", "contact", Stages.PostOperation, ExecutionMode.Synchronous)]
```
```csharp
/// Sync Post-Create of contact (order 1): (all columns)
```

```csharp
[Plugin]
[Step("Update", "contact", "jobtitle", Stages.PostOperation, ExecutionMode.Synchronous)]
```
```csharp
/// Sync Post-Update of contact (order 1): jobtitle
```

## Cases that need more than one assembly

### Shared.Twin - one file, two assemblies

`src\Shared\Twin.cs` is linked into both `TestPlugins` and `Contoso.Crm.Plugins`, so
`Shared.Twin` is a type in both, and both register it with steps of their own. This is
what a shared base library looks like once it has been deployed twice, and the documenter
knows nothing about assemblies when it goes looking for a file: both registrations resolve
to this one `.cs`.

Both are written, in assembly order, and the second is what survives. Contoso's is written
first, TestPlugins' second, so the file ends up saying:

```csharp
[Plugin]
[Step("Create", "account", Stages.PostOperation, ExecutionMode.Synchronous, ExecutionOrder = 3)]
```
```csharp
/// Sync Post-Create of account (order 3): (all columns)
```

and the registration that is *not* in the file is Contoso's:

```csharp
[Step("Update", "account", "name", Stages.PreOperation, ExecutionMode.Synchronous, ExecutionOrder = 4)]
```

The write report names `Twin` twice under "Updated", which is the only warning given. Note
also that both writes take a backup and the backup name is only accurate to the second, so
the two collide and the pristine original is the copy that gets lost. Whether that is
acceptable is a decision; that it happens is pinned here.

### Rival - ambiguous across assemblies

`TestPlugins.Rival` and `Contoso.Crm.Rival` are different classes, in different
namespaces, in different assemblies, in a file each. The namespaces would settle it in a
moment, but the documenter matches on the short name alone, so both are reported as
ambiguous and **neither file may be modified**:

```
// Ambiguous, several files declare the class (3)
//   Duplicate (2 files)
//   Rival (2 files)
//   Rival (2 files)
```

Alpha.Duplicate is the same failure inside one assembly; Rival is the same failure where
the answer was available and was not used.

## Contoso.Crm.Orphan - registered, no source

`Contoso.Crm.Bravo` and `Contoso.Crm.Ghost` have steps and no `.cs` under `tests\src`.
They appear in the class list, they appear in the preview with their attributes rendered,
and the write report puts both under:

```
// No matching .cs file (2)
//   Bravo
//   Ghost
```

The preview showing them is deliberate: nothing has failed until you press Write.

## Contoso.Crm.Empty - registered, no steps

`Contoso.Crm.Empty.Idle` and `Contoso.Crm.Empty.Spare` are plugin types with no steps in
an assembly where nothing else has any either. Ticking the assembly must add no group, no
classes and no preview - and must still say so, on the status line, as `· 1 with no steps`.

Unticking and re-ticking it must not send another query: an assembly that turned out to
have nothing is remembered as having been asked.

## Microsoft.Contoso.Extensions - named Microsoft, signed by Contoso

Hidden until the Microsoft switch is on, and once it is on, entirely ordinary:

```csharp
[Plugin]
[Step("Update", "account", "websiteurl", Stages.PostOperation, ExecutionMode.Synchronous, ExecutionOrder = 9)]
```
```csharp
/// Sync Post-Update of account (order 9): websiteurl
```

The genuine article - an assembly signed with one of Microsoft's own keys - cannot be
faked, because the import validates the strong name and nobody outside Microsoft can sign
with those. That branch of `IsMicrosoft` can only be judged against whatever first party
assemblies the environment really has, which is what the count on the switch is for.

## In the codebase, never registered

Two classes in `src\ContosoPlugins` are compiled into `Contoso.Crm.Plugins` and are not
registered as plugin types at all:

| File | What it holds | What must happen |
|---|---|---|
| `StaleDoc.cs` | `[Plugin]`, `[Step]`, `[Image]` and a `Register:` block from a registration that has since been deleted | nothing. The class never reaches the list, so its stale documentation stays exactly as stale as it was found |
| `Untouched.cs` | an ordinary undocumented class | nothing |

Both files have to come back from a run byte for byte identical. `StaleDoc` is the more
interesting of the two: the tool never removes documentation for a registration that has
gone away, and a run over a folder full of other people's classes is the moment that would
matter. It is pinned here as behaviour, not endorsed as correct.

## The write report

With all five assemblies ticked and the source folder set to `tests\src`, all nineteen
classes are accounted for and pressing Write should produce a report of the shape:

```
// Updated (14)
//   ... every class with a file, Twin twice
// Ambiguous, several files declare the class (3)
//   Duplicate (2 files)
//   Rival (2 files)
//   Rival (2 files)
// No matching .cs file (2)
//   Bravo
//   Ghost
```

Fourteen writes over thirteen files, because `Twin` is written once per assembly. Run it a
second time without changing anything and it becomes:

```
// Updated (2)
//   Twin
//   Twin
// Already up to date (12)
```

`Twin` is the one thing that cannot settle: the two registrations overwrite each other on
every run, for ever.

# Deliberately not covered

Worth knowing what the suite does *not* pin, so a gap is not mistaken for a pass:

- **An unmanaged assembly.** `customizationlevel` is a dead end for telling assemblies
  apart and the documenter does not read it, so the value would be a regression guard
  only - and the cost is real: deleting an unmanaged solution leaves its components behind,
  and `pac` has no command that deletes a record, so there would be no clean way to
  unregister it.
- **A real Microsoft signature.** See above; it cannot be faked and is left to the
  environment's own first party assemblies.
- **`isolationmode` "None".** Online forces sandbox for everything that is not Microsoft's,
  so the other value in the Isolation column is not reachable in a Developer environment.
- **`ishidden` assemblies.** The documenter filters them out and the fixture cannot make
  one: `IsHidden` is not in the solution schema for a plugin assembly.
- **Custom API handlers and workflow activities.** Both are plugin types with no
  `sdkmessageprocessingstep`, so the tool drops them and is silently blind to them. That is
  a gap in the tool rather than in the fixture, and pinning it should follow a decision
  about what it ought to do.
- **Assemblies delivered as a plugin package.** The NuGet route registers a
  `pluginpackage`, which nothing in the tool queries.

# Notes from building this

Things that cost time, in case they cost it again:

- `FullName` and `SourceType` are **attributes** on `PluginAssembly`, and `Name` /
  `AssemblyQualifiedName` are attributes on `PluginType`. Get any of them wrong and the
  import fails with a bare `NullReferenceException` out of `GetPluginAssembliesTable`.
- `SourceType` also decides whether SolutionPackager carries the DLL into the zip. Without
  it the zip packs happily and the import fails the same unhelpful way, so `build.ps1`
  checks the zip for every assembly before handing it over.
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
- A plugin type name is only unique within its assembly. `Shared.Twin` exists in two, so
  every query in `verify.ps1` that names a type also names the assembly it belongs to; one
  that did not would pass against the wrong record.
- Assembly metadata is generated from the matrix rather than committed, and the public key
  token is read off the built DLL rather than written down. Five hand maintained copies of
  the same XML is five chances for one of them to be quietly wrong about a token, and a
  wrong token fails the import in the same way everything else does.
