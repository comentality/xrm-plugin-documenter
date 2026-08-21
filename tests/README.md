# End to end tests

Six plugin assemblies, two publishers, three solutions of empty plugins and two assemblies
registered by hand into none of them, so a run can be judged against something other than
"looks about right".

The split is not arbitrary. Plugin Step Codegen shows **unmanaged** assemblies by default -
the plugin somebody is in the middle of writing - so the two registered by hand are what
the tool opens on, and they carry every shape of step, image and free text the emitters
have to describe. The four that arrived in managed solutions are what somebody shipped;
they sit behind the **Managed** switch and carry the cases that are about *other people's*
assemblies: a second vendor, a registration with no source, an assembly with no steps.

```powershell
.\register.ps1      # build the assemblies, pack three solutions, import them
.\verify.ps1        # confirm the environment matches registrations.psd1
.\unregister.ps1    # take it all away again
.\xtb.ps1           # build the tool and open it in an XrmToolBox of its own
.\write.ps1         # check the write path headlessly, no environment needed
```

All four use the active organization of the current `pac` auth profile; pass
`-Environment <url>` to target another. `register.ps1` is safe to re-run.

`pac` only reads, and two assemblies here are registered rather than imported, so the
records that have no solution are written and deleted over the Web API instead — with
the access token `pac` has already cached for the same organization. There is still nothing to sign
in to beyond `pac auth`; see [dataverse.ps1](dataverse.ps1) for why the refresh token is
left alone.

`xtb.ps1` leaves you looking at the tool, connected, with the source folder on the
clipboard. Compare what it writes with [Expected output](#expected-output) below.
`dotnet build tests\src\TestPlugins` afterwards is itself a check: the project compiles
against the same `XrmToolsMetaAttributes.cs` the tool emits, so attributes that do not
compile break the build.

## The source folder

The folder the tool is pointed at is `tests\src`, and it holds the source of
**several** assemblies at once - which is the normal shape of a plugin repository and the
only way to reach the things that can only go wrong when a class name is not unique across
one:

```
tests\src\                    <- point the tool here
    TestPlugins\              registered by hand, in no solution
    WorkInProgressPlugins\    registered by hand, in no solution
    ContosoPlugins\           Contoso's assembly, shipped in a managed solution
    MsContosoExtensions\      the assembly named Microsoft, also shipped
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

`xtb.ps1` itself is fifteen lines. Everything in it that is XrmToolBox rather than Plugin Step
Codegen lives in the [XtbSandbox](https://github.com/comentality/xrmtoolbox-sandbox)
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
| `keys/Contoso.snk` | The strong name key four of the six assemblies are signed with, whoever published them. |
| `solution/` | The solution manifest template and the two static files every solution carries. |
| `matrix.ps1` | Ids, assembly full names and generated step names, shared so `build.ps1` and `verify.ps1` cannot disagree. |
| `build.ps1` | Turns the matrix into three solution zips. `register.ps1` calls it; run it alone to inspect the zips. |
| `unmanaged.ps1` | The other half of `register.ps1` and `unregister.ps1`: the two assemblies that are in no solution, written and deleted record by record. |
| `dataverse.ps1` | An access token and four verbs. The only thing here that talks to the environment without going through `pac`. |
| `write.ps1` | The write path, headlessly: rebuilds from `registrations.psd1` the objects `RegistrationQuery` would have read, runs the real find, emit and write code over sandbox copies of `src\` under `tests\.write`, and checks the reports and files against [Expected output](#expected-output) - including that the registered namespace settles every short name collision, that a second run settles, and that what was written compiles. No environment needed. |

`TestPlugins.snk` and `keys/Contoso.snk` are committed on purpose. The public key token is
part of every `AssemblyQualifiedName` in the fixture, and telling one vendor from another
is exactly what the tool uses a token for, so the keys have to be the same
everywhere. They sign nothing anyone should trust.

## The assemblies

| Assembly | Registered | Publisher | Key | Source | Why it is here |
|---|---|---|---|---|---|
| `TestPlugins` | by hand | none | its own | `src\TestPlugins` | Every shape of step, image and free text the emitters have to describe - on the route the tool is really used through. |
| `WorkInProgress.Plugins` | by hand | none | TestPlugins | `src\WorkInProgressPlugins` | The second thing the same developer has open: a plugin still being written, so the default view has two assemblies to group under. |
| `Contoso.Crm.Plugins` | solution | Contoso | Contoso | `src\ContosoPlugins` | A second vendor in the same source folder: different publisher, different signature, colliding class names. |
| `Contoso.Crm.Orphan` | solution | Contoso | Contoso | none | Registered, with steps, and no `.cs` anywhere the tool will look. |
| `Contoso.Crm.Empty` | solution | Contoso | Contoso | none | Plugin types and not one step against any of them. |
| `Microsoft.Contoso.Extensions` | solution | Comentality | Contoso | `src\MsContosoExtensions` | Named Microsoft, signed by Contoso, shipped by Comentality. The case `IsMicrosoft` gets wrong on purpose. |

Two publishers and two keys, and nothing lines up:
`Microsoft.Contoso.Extensions` is called Microsoft, carries the same signature as its
plainly-not-Microsoft neighbours, and was shipped by a publisher of neither name. The
tool reads the name and the signature and cannot see the publisher at all, which is
the point of there being one to ignore.

Neither assembly registered by hand has a publisher, because nothing published them. They
share nothing but a folder: `WorkInProgress.Plugins` is signed with TestPlugins' key
because it is the same developer's second project rather than a third vendor.

## Why three solutions

All three are **managed**, and that is load bearing: deleting an unmanaged solution leaves
every component behind in the Default solution, so `unregister.ps1` would unregister
nothing.

A solution has exactly one publisher, so the two publishers need one each. The third exists
because the solution format has no element for a step's state: `StateCode` is not in the
schema and the importer ignores one if you invent it. Every step lands *disabled* unless
the import is run with `--activate-plugins`, which then enables all of them. So the steps
the matrix marks `Disabled` **and** that belong to an assembly in a solution go into a
companion imported without that flag. An unmanaged step needs none of this: its state is
one field on the record.

| Solution | Publisher | Contents | Imported |
|---|---|---|---|
| `PluginStepCodegenE2E` | Comentality | `Microsoft.Contoso.Extensions`, 1 plugin type, 1 step | `--activate-plugins` |
| `PluginStepCodegenE2EContoso` | Contoso | the three Contoso assemblies, 8 plugin types, 5 steps | `--activate-plugins` |
| `PluginStepCodegenE2EDisabled` | Contoso | 1 step | plain, so it stays off |

The companion goes last, because its step runs against a plugin type another solution
installs. `unregister.ps1` deletes it first for the same reason.

The first solution exists to keep a second publisher in play, and holds the one assembly
whose publisher is a deliberate lie. If it ever ends up empty `build.ps1` throws rather
than packing a solution with nothing in it.

## Why two assemblies are in none of them

Everything in a solution describes a plugin that has been *shipped*. That is not where the
tool is used. It is used on a plugin somebody is in the middle of writing: built,
registered straight into a development environment, and pointed at to get the comments
out. Nothing about that involves a solution, and it is what the assembly list shows before
any switch is touched — so it is where the assembly with every interesting case lives.

Both are registered the way the plugin registration tool registers one — the assembly,
its plugin types, its steps and their images written into the organization as unmanaged
records belonging to no solution but the Default one. What that reaches and the solutions
do not:

- **`ismanaged` is false** on every record, along with the whole unmanaged customization
  layer. The tool does not read any of it and is not meant to start; `verify.ps1`
  asserts it so the two routes are known to produce two genuinely different kinds of row.
- **`PluginTypeId` is derived, not written.** A step is created with its plugin type on
  `EventHandler`, which is polymorphic — a step can run a service endpoint instead — and
  the platform fills in `PluginTypeId` from it. `PluginTypeId` is what the tool's
  step query joins on. Were that ever to stop happening, the tool would find no steps at
  all in the one environment shape that matters most, and only this fixture would say so.
- **A disabled step, disabled on the step.** The managed route can only reach one through
  a whole companion solution imported without `--activate-plugins`. Here it is one field
  on one record, which is how a developer actually switches a step off.
- **The generated step name is the registration tool's.** The tool suppresses a step
  name it recognises as the default one, and the string it compares against here was
  produced the same way a user's is.
- **Impersonation is written as an id.** A solution carries `ImpersonatingUserIdName` and
  lets the importer resolve the name; a record written by hand binds
  `impersonatinguserid` to a `systemuser`, which `unmanaged.ps1` asks `WhoAmI` for. Both
  reach the same column and the tool cannot tell which route wrote it — which is
  worth having pinned, because those are two quite different pieces of plumbing.
- **Free text arrives exactly as it was sent.** The Web API keeps CRLF; the solution
  importer does not, because XML normalises line endings inside an element. Same text, two
  routes, two literals in the emitted attribute. `TestPlugins.EscapedText` and
  `Contoso.Crm.Charlie` are the pair, and the difference is visible in what the tool
  writes rather than only in the environment.

There is no zip to delete afterwards, so `unregister.ps1` deletes the records instead:
images, steps, plugin types, then the assembly, because none of them will go while
something still points at it.

# Expected output

Thirty three steps across twenty seven registered plugin types in six assemblies.

## The assembly list

With nothing typed in the filter box and both switches off, the list shows **two** of the
fixture's six, in name order - the two nobody shipped:

```
TestPlugins              Sandbox
WorkInProgress.Plugins   Sandbox
```

That is the whole point of the default: it is what a developer sees in the environment
they develop in, and both rows have source in the folder they are about to point the tool
at. Nothing in the list says which is which - there is no column for it - so the switch
counts beside the button are where the rest is accounted for:

```
[Load Assemblies]  □ Microsoft's (65)  □ Managed (3)
```

- **Managed (3)** is `Contoso.Crm.Plugins`, `Contoso.Crm.Orphan` and `Contoso.Crm.Empty`.
- **Microsoft's (65)** is the fixture's `Microsoft.Contoso.Extensions` plus however many
  first party assemblies the environment really carries - sixty four in a fresh Developer
  environment, which is the number the switch exists for.

`Microsoft.Contoso.Extensions` is managed *and* named Microsoft, and is counted only once,
on the Microsoft switch. That is the fixture's proof that the two tests are asked in the
right order: tick **Microsoft's** with **Managed** still off and it appears anyway. Had
both applied to the same row it could not, and the switch would look broken. It is signed
with the Contoso key, so nothing about its signature says Microsoft; it is hidden on the
strength of its name alone.

Ticking both rows in the default list:

```
2 assemblies · 16 of 16 classes
```

Turning **Managed** on and ticking all five:

```
5 assemblies · 22 of 22 classes · 1 with no steps
```

`Contoso.Crm.Empty` is the one with no steps. It contributes no group at all to the class
list - an empty group draws nothing - so the status line is the only place it is
accounted for.

Things worth doing to the list once it is loaded:

- Type `Contoso` in the filter with both switches off and nothing ticked. **No rows at
  all**, and `Nothing matches.` on the status line - which is right rather than broken:
  every Contoso assembly was shipped. Turn **Managed** on and the same filter shows three,
  with the "All" box ticking those three and going indeterminate rather than checked. Turn
  **Microsoft's** on as well and it shows four. The switches and the filter compose rather
  than override each other.
- Tick `TestPlugins`, then filter it out of view. It stays ticked, its classes stay in the
  list under their own heading, and the status line says `· 1 out of view`.
- Tick all five with **Managed** on, then turn it back off. The three managed rows leave
  the list and stay ticked - their classes are still there under their own headings, and
  the status line says `· 3 out of view`. A switch hides exactly as the filter box does,
  and neither ever unticks anything.

## The class list and the preview

Classes are grouped under the assembly they were registered from, assemblies in name
order and classes within one in type name order. The default list already has two groups
to get right:

```
// ===== TestPlugins
// ===== WorkInProgress.Plugins
```

The harder case is behind the **Managed** switch, and is worth checking rather than
assuming: the environment hands all of the types back in one list sorted by type name, in
which `Contoso.Crm.Alpha`, `Contoso.Crm.Bravo` and `Contoso.Crm.Charlie` belong to *two
different assemblies* in alternation. Both the list and the preview have to regroup them,
so with all five ticked each assembly's `// =====` heading appears exactly once:

```
// ===== Contoso.Crm.Orphan
// ===== Contoso.Crm.Plugins
// ===== TestPlugins
// ===== WorkInProgress.Plugins
```

`Shared.Twin` sorts ahead of everything named `TestPlugins.*`, so it is the first class
under the TestPlugins heading and the last under Contoso's.

## TestPlugins

Everything below is registered by hand, in no solution, and none of it reads any
differently for that — which is the claim worth making, because the tool is not
supposed to be able to tell. The one place the route shows through is `EscapedText`, and
it shows through in a line ending.

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
arrives with `\r\n` intact, because the Web API stores exactly the text it was given.
`Contoso.Crm.Charlie` carries the same shape of description through a solution and comes
back with `\n`, so the pair is what pins the difference.

The summary comment says none of this — names, descriptions and configuration are
deliberately left out of it - which is also what keeps the doc comment well formed.

```csharp
[Plugin(Description = "Quote \" backslash \\ ampersand & angle <tag> all in one description.")]
[Step("Update", "account", Stages.PostOperation, ExecutionMode.Synchronous,
    Name = "Quote \" backslash \\ ampersand & angle <tag>",
    Description = "First line, with a tab\there.\r\nSecond line.",
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

### NearlyAllColumns

The three shapes of a column list the experimental `(all columns except ...)` rendering
(behind the `†` button, off by default) has to tell apart — and the reason the fixture
can say "nearly every column" at all: `register.ps1` reads the table's updatable columns
off the live environment and writes the near-complete and complete lists from them
(`FilterAllExcept` and `FilterAll` in `registrations.psd1`), so the annotation step
really is filtered on everything but two of *this* environment's columns, however many
that turns out to be. `verify.ps1` recomputes the same lists from the same metadata, and
`write.ps1` expands them against declared stand-in universes, so live and headless agree
on everything but the counts.

With the setting on, the comment collapses to the exceptions; the third step's list
holds a column contact does not have — the shape left behind when a custom column is
deleted after registration — and stays verbatim, because the stale name is the finding.
`N` below is the live count of task's updatable columns (10 in the headless stand-in):

```csharp
/// Sync Post-Update of annotation (order 1): (all columns except notetext, subject)
///     PreImage: (all columns except documentbody)
/// Sync Post-Update of task (order 2): (all N columns, written out)
/// Sync Post-Update of contact (order 3): cmtl_legacyscore, firstname, lastname
```

With it off — the default — the first two steps and the image are recited exactly as
registered, wrapping like WideRegistration's. Attribute mode always writes the literal
lists, whatever the setting; they have to compile, and they do.

The image is measured against every real column of annotation where the filter is
measured against only the updatable ones — diffing either against the other universe
would invent exceptions that were never offered, which is why `documentbody` can be an
image's lone exception while `createdby` never shows up as one of the filter's.

Because the lists are pinned to the day they were registered, a table that gains a
column afterwards drifts out from under them — the near-complete filter grows an extra
"except", and verify.ps1 fails until `register.ps1` is re-run. That is the behaviour the
"(all N columns, written out)" phrasing exists to flag, wearing its own fixture.

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

`Duplicate` is declared by both `TestPlugins\Plugins\Duplicates\AlphaDuplicate.cs` and
`BetaDuplicate.cs`, so the short name alone matches two files — and the registered
namespace `TestPlugins.Alpha` settles it. `AlphaDuplicate.cs` is written; `BetaDuplicate.cs`
must come back byte for byte identical.

```csharp
[Plugin]
[Step("Create", "annotation", Stages.PostOperation, ExecutionMode.Synchronous)]
```
```csharp
/// Sync Post-Create of annotation (order 1): (all columns)
```

### NeverRegistered and Beta.Duplicate

Plugin types with no steps. They must not appear in the list at all, and their files must
come back from a run byte for byte identical.

## Contoso.Crm.Plugins

Behind the **Managed** switch, along with the rest of what somebody shipped.

### Alpha

An ordinary class in the second vendor's assembly, documented exactly as if it were in the
first. What it is really for is the interleave described above: sorted by type name, Alpha
and Charlie sit either side of `Contoso.Crm.Bravo`, which belongs to `Contoso.Crm.Orphan`.

```csharp
[Plugin]
[Step("Create", "contact", Stages.PostOperation, ExecutionMode.Synchronous)]
```
```csharp
/// Sync Post-Create of contact (order 1): (all columns)
```

### Charlie

Everything the managed route can say that the unmanaged one cannot, on one step. It is
**disabled**, which no solution can express - so it is imported in the companion without
`--activate-plugins`, which is the only reason there is a third solution. And its
description went in carrying CRLF and comes back with LF, because XML normalises line
endings inside an element:

```csharp
[Plugin]
[Step("Update", "contact", "jobtitle", Stages.PostOperation, ExecutionMode.Synchronous,
    Description = "Held back until Contoso ships it.\nSecond line.")]
```
```csharp
/// Sync Post-Update of contact (order 1, disabled): jobtitle
```

Compare the `\n` here with the `\r\n` in `TestPlugins.EscapedText`: same shape of text,
two registration routes, and the difference survives all the way into the file the tool
writes. And compare `(order 1, disabled)` with `HalfFinished`'s: a step switched off by a
solution import and a step switched off on the record have to read identically.

## Cases that need more than one assembly

Both of these straddle the **Managed** switch, which is the interesting part: one half of
each is in the default list and the other half arrived in a solution.

### Shared.Twin - one file, two assemblies

`src\Shared\Twin.cs` is linked into both `TestPlugins` and `Contoso.Crm.Plugins`, so
`Shared.Twin` is a type in both, and both register it with steps of their own. This is
what a shared base library looks like once it has been deployed twice, and the tool
knows nothing about assemblies when it goes looking for a file: both registrations resolve
to this one `.cs`.

With **Managed** off only TestPlugins' registration is in view, so the file is written
once and a second run leaves it alone. Turn the switch on and the fight below starts.

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

### Rival - settled by namespace across assemblies

`TestPlugins.Rival` and `Contoso.Crm.Rival` are different classes, in different
namespaces, in different assemblies, in a file each. The short name matches both files,
and the registered namespace settles which is which: each registration is written into
its own file, never the other's.

`TestPlugins\Plugins\Rival.cs`:

```csharp
[Plugin]
[Step("Delete", "task", Stages.PreOperation, ExecutionMode.Synchronous)]
```
```csharp
/// Sync Pre-Delete of task (order 1)
```

`ContosoPlugins\Rival.cs`, behind the **Managed** switch:

```csharp
[Plugin]
[Step("Delete", "contact", Stages.PreOperation, ExecutionMode.Synchronous)]
```
```csharp
/// Sync Pre-Delete of contact (order 1)
```

With **Managed** off only `TestPlugins.Rival` is in view, and the tie resolves exactly the
same way: the namespace is read from the registration and the files, not from which
assemblies happen to be ticked, so hiding half the registrations changes what is written
but never where.

Alpha.Duplicate is the same tie inside one assembly; Rival is the same tie across two.

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

`Ghost` is also where impersonation is pinned on the managed route — the solution carries
a user's full name and the importer resolves it, where `TestPlugins.DisabledAndImpersonated`
gets there by id over the Web API. Both come out saying the same thing, and `As <name>` is
whoever ran `register.ps1`:

```csharp
[Plugin]
[Step("Update", "task", Stages.PostOperation, ExecutionMode.Synchronous, ExecutionOrder = 2)]
```
```csharp
/// Sync Post-Update of task (order 2, As Kosta Koniev): (all columns)
```

In attribute mode the impersonation is gone, because no attribute can carry it — the same
suppression `DisabledAndImpersonated` pins, here on a class with no file to write to.

## Contoso.Crm.Empty - registered, no steps

`Contoso.Crm.Empty.Idle` and `Contoso.Crm.Empty.Spare` are plugin types with no steps in
an assembly where nothing else has any either. Ticking the assembly must add no group, no
classes and no preview - and must still say so, on the status line, as `· 1 with no steps`.

Unticking and re-ticking it must not send another query: an assembly that turned out to
have nothing is remembered as having been asked.

## Microsoft.Contoso.Extensions - named Microsoft, signed by Contoso, shipped by Comentality

Hidden until the Microsoft switch is on - **whatever the Managed switch is set to**, which
is the disjointness the two switches depend on - and once it is on, entirely ordinary:

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

## WorkInProgress.Plugins - the second assembly registered by hand

In the default list beside `TestPlugins`, and there to make sure the default list is a
list rather than a row: two groups in the class list, two `// =====` headings in the
preview, and an "All" box that means both. Its classes read like a feature somebody is
halfway through, which is the situation the whole tool is for.

### NewFeature

Everything at its default, on the route the tool is really used through.

```csharp
[Plugin]
[Step("Create", "account", Stages.PostOperation, ExecutionMode.Synchronous)]
```
```csharp
/// Sync Post-Create of account (order 1): (all columns)
```

### HalfFinished

Filtered and switched off, which is the resting state of half the steps in any
development environment. `disabled` here comes off `statecode` on the step itself rather
than off which solution imported it, and has to read identically to
`DisabledAndImpersonated`'s.

```csharp
[Plugin(Description = "Not finished, and switched off until it is.")]
[Step("Update", "contact", "firstname", Stages.PreOperation, ExecutionMode.Synchronous)]
```
```csharp
/// Sync Pre-Update of contact (order 1, disabled): firstname
```

### Scratch

A step whose name a person typed, sitting beside two in the same assembly that kept the
name the registration tool offered - so `Name` is emitted here and suppressed there,
against a default string produced the same way a user's is. Its pre image is on a
`Delete`, which is the only place a pre image is much use.

```csharp
[Plugin]
[Step("Delete", "task", Stages.PreOperation, ExecutionMode.Synchronous,
    Name = "Tidy up after a deleted task",
    Description = "Temporary. Remove before this goes anywhere near production.")]
[Image(ImageTypes.PreImage, "subject,regardingobjectid")]
```
```csharp
/// Sync Pre-Delete of task (order 1)
///     PreImage: subject, regardingobjectid
```

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

Two reports are worth having, because the tool has two states worth being in.

**The default list**, both switches off, both assemblies ticked, source folder `tests\src`
- sixteen classes, every one resolving to a file — `Duplicate` and `Rival` match two files
each on the short name, and the registered namespace picks the right one:

```
// Updated (16)
//   ... every class with a file, Twin once
```

Run it again and everything says `Already up to date (16)`. This is the run that settles,
which is what a developer documenting their own assembly should get.

**With Managed and Microsoft's both on** and all six assemblies ticked, all twenty three
classes are accounted for and pressing Write should produce a report of the shape:

```
// Updated (21)
//   ... every class with a file, Twin twice
// No matching .cs file (2)
//   Bravo
//   Ghost
```

Twenty one writes over twenty files, because `Twin` is written once per assembly. Run it
a second time without changing anything and it becomes:

```
// Updated (2)
//   Twin
//   Twin
// Already up to date (19)
```

`Twin` is the one thing that cannot settle: the two registrations overwrite each other on
every run, for ever - but only while both are in view, which is the difference between
this report and the one above.

# Deliberately not covered

Worth knowing what the suite does *not* pin, so a gap is not mistaken for a pass:

- **A real Microsoft signature.** See above; it cannot be faked and is left to the
  environment's own first party assemblies.
- **`isolationmode` "None".** Online forces sandbox for everything that is not Microsoft's,
  so the other value in the Isolation column is not reachable in a Developer environment.
- **`ishidden` assemblies.** The tool filters them out and the fixture cannot make
  one: `IsHidden` is not in the solution schema for a plugin assembly.
- **Custom API handlers and workflow activities.** Both are plugin types with no
  `sdkmessageprocessingstep`, so the tool drops them and is silently blind to them. That is
  a gap in the tool rather than in the fixture, and pinning it should follow a decision
  about what it ought to do.
- **Assemblies delivered as a plugin package.** The NuGet route registers a
  `pluginpackage`, which nothing in the tool queries.
- **An unmanaged assembly that is also named Microsoft.** `IsMicrosoft` is asked before
  `IsManaged`, so such a row would be held back by the Microsoft switch and not appear in
  the default list. The managed half of that ordering is pinned by
  `Microsoft.Contoso.Extensions`; the unmanaged half is not, and adding a seventh assembly
  to say the same thing twice did not seem worth it.
- **A managed assembly of your own that you still want to document.** The switch exists
  for it and the Contoso assemblies exercise the path, but "the environment you can reach
  is not the one you develop in" is a situation, not a record shape - there is nothing in
  the environment that distinguishes it from an ISV's app.

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
  token is read off the built DLL rather than written down. Four hand maintained copies of
  the same XML is four chances for one of them to be quietly wrong about a token, and a
  wrong token fails the import in the same way everything else does.
- XML normalises line endings inside an element, so a description that goes into a
  solution as CRLF comes back as LF. The Web API keeps what it was given.
  `Contoso.Crm.Charlie` and `TestPlugins.EscapedText` are the two halves of that, and it
  is visible in the emitted attribute rather than only in the environment.
- A step in a solution can run against a plugin type a *different* solution installed: the
  companion is packed with no assemblies at all, only root components of type 92, and the
  type its step names arrives with `PluginStepCodegenE2EContoso`. It shares that solution's
  publisher, which is not known to be required and was not worth finding out.

And, from registering assemblies without a solution:

- `pac` reads and imports; it deletes solutions and nothing smaller. That is the whole
  reason `dataverse.ps1` exists, and it is a short script rather than a dependency because
  the Power Platform CLI's own MSAL cache already holds an access token for the
  organization it last talked to. Read that and there is no second sign in to arrange.
  Do **not** redeem the refresh token instead: Entra rotates it, and `pac` would be left
  holding one that no longer works.
- A `PATCH` to a record that is not there creates it, which is what makes registering by
  hand as re-runnable as importing a solution twice.
- `sdkmessageprocessingstep` has no `plugintypeid` column in the Web API at all - the
  writable one is `eventhandler`, bound as `eventhandler_plugintype@odata.bind`, and
  `PluginTypeId` is derived from it. FetchXML and the SDK both still see `plugintypeid`,
  which is why `verify.ps1` and the tool can join on something the create call
  cannot set.
- The API validates the image against the message, which is worth knowing before writing
  a fixture that reads sensibly and then will not register: a pre image on `Create` is
  refused outright ("Message Create does not support this image type"), and a post image
  on `Create` has to name `Id` as its message property rather than `Target`.
- `statecode` is its own write. It is made on every registration rather than only for the
  steps meant to be off, so a step somebody disabled by hand comes back enabled.
- Impersonation is a lookup, so the Web API wants an id where the solution schema wants a
  full name. `WhoAmI` answers with the caller's id and needs no extra permission, which is
  the whole of `unmanaged.ps1`'s handling of it. Clearing one again would be a `DELETE`
  against the reference rather than a null in the body, which is why nothing here does.
