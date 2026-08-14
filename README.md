# Plugin Documenter

An [XrmToolBox](https://www.xrmtoolbox.com/) tool that documents your Dataverse plugin
step registrations **in your C# source**.

It reads the steps and images registered in the connected environment and writes them
back into your plugin classes as [Xrm Tools](https://github.com/rezanid/xrmtools)
compatible `[Plugin]`, `[Step]` and `[Image]` attributes.

Your registration stops living only in an environment you have to go look at, and starts
living in the code review, the diff and the git history.

## Why

Registration lives in the environment, source lives in git, and nothing keeps them
honest. The only existing tool that closes the gap is `spkl instrument`, a CLI buried in
the largely dormant SparkleXrm framework. This does the same job from inside XrmToolBox,
against the modern attribute model.

## What it does

1. **Load Assemblies** lists the custom plugin assemblies in the connected environment.
2. Ticking one — or **All** of them, for a project that ships an assembly per plugin —
   loads every plugin type that has at least one registered step, grouped by assembly.
3. **Write** chooses the output: *Xrm Tools attributes* or a *readable summary comment*.
4. The preview pane shows exactly what would be written, and follows the ticks and the
   mode as you change them.
5. **Write to Files** finds the `.cs` file declaring each class and splices the output
   in above the class declaration.
6. **Create Attribute Definitions File** drops a dependency-free
   `XrmToolsMetaAttributes.cs` into your project so the emitted attributes compile
   without the NuGet package or the Visual Studio extension.

Every file that changes gets a timestamped `.bak` copy beside it.

### Finding your assemblies among Microsoft's

An environment carries dozens of first party assemblies and a handful of yours, so
Microsoft's are held back by default, with the count on the switch that brings them back.
They are told apart by their **strong name signature**, not their name: plugin assemblies
must be signed, `31bf3856ad364e35` is a key nobody outside Microsoft can sign with, and it
covers Power Pages, Field Service and the rest of the optional apps whatever they call
themselves and whichever of Microsoft's several publishers shipped them.

An ISV's app is neither Microsoft's nor yours, and no test will ever say so. That is what
the **Filter** box is for: type your own name and the list is yours. Filtering only hides
rows, it never unticks one, so you can narrow the list, tick **All**, and clear it again.

## Output

Two shapes, chosen with the **Write** toggle. They are independent: each replaces only
its own block, so switching modes never deletes the other one's work, and a class can
carry both.

### Xrm Tools attributes

```csharp
/// <summary>Handles account writes.</summary>
[Obsolete("your own attributes are left alone")]
[Plugin(Description = "Keeps account data consistent.")]
[Step("Create", "account", Stages.PreOperation, ExecutionMode.Synchronous)]
[Image(ImageTypes.PostImage, "name")]
[Step("Update", "account", "name,address1_line1", Stages.PostOperation, ExecutionMode.Asynchronous,
    Name = "Recalculate rollups",
    ExecutionOrder = 25,
    Description = "Runs after the write completes.",
    AsyncAutoDelete = true)]
[Image(ImageTypes.PreImage, "name", Name = "Before", EntityAlias = "Before")]
[Step("Associate", Stages.PreValidation, ExecutionMode.Synchronous)]
public partial class AccountManager : IPlugin
```

Style follows the `XrmTools.Meta.Attributes` README: the widest positional constructor
the step's data supports, remaining facts as named properties, wrapping one argument per
line only when the line gets long.

**Attribute order is load bearing.** `[Image]` binds to the nearest preceding `[Step]`,
so steps are written in execution order with their own images following them.

### Readable summary comment

The same registration as prose, for the reader rather than the compiler:

```csharp
/// <summary>Handles course history.</summary>
/// <remarks>
/// Register:
/// Sync Pre-Delete of ilac_class (order 1, disabled, As SYSTEM)
///     PreImage: (all columns)
/// Sync Post-Create of mshied_coursehistory (order 3): ilac_suggestedesllevel
///     PreImage:
///         mshied_academicperioddetailsid, ilac_class, mshied_courseid, ilac_currentlevel,
///         ilac_enddate, ilac_exitlevel, ilac_isstudentleaving, ilac_sessiontype
/// Sync Post-Update of mshied_coursehistory (order 1):
///     ilac_enddate, mshied_enrollmentstatus, ilac_startdate, ilac_suggestedesllevel
/// </remarks>
public partial class CourseHistoryHandler : IPlugin
```

Deliberately not a second serialisation. Step names, descriptions and configuration are
left out as noise; the unlabelled list after the colon is the step's filtering attributes,
which [Dataverse now honours on `Create` as well as
`Update`](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/register-plug-in).

Nothing that has columns is ever left blank. An unfiltered `Create` or `Update` step, and an
image with no columns, both read `(all columns)` — bracketed, so it reads as a remark about
the list rather than as a name in it. For an image that is Microsoft's
[explicit bad practice](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/register-plug-in)
rather than a neutral default. Messages that filter nothing — `Delete`, a global message —
keep their bare header, because there is no column list to have omitted.

Because nothing has to compile, the comment can carry two facts no attribute can express:
a **disabled** step, and the user a step impersonates (PRT's *Run in User's Context*),
shown as `As <name>`. Both appear only when they differ from the default.

The tool owns a `<remarks>` block whose first line is `Register:` and replaces it in
place on later runs. Any other `<remarks>` you have written is left alone.

## Attribute definitions file

`Create Attribute Definitions File` writes a minimal subset of
`XrmTools.Meta.Attributes` — the three attributes, `PluginAssemblyAttribute`, and the
enums they need. Namespace, type names, constructor signatures, property names and enum
values are identical to the published package, so you can delete the file at any time and
replace it with:

```xml
<PackageReference Include="XrmTools.Meta.Attributes" Version="1.0.57" />
```

Both are verified: the same generated source compiles against the generated file and
against the real package.

Do **not** use both at once. The package generates the same types into your compilation
and you will get `CS0101` duplicate type errors.

## Known limits

These come from the Xrm Tools attribute model, not from this tool.

- **Isolation mode is assembly-level.** `[assembly: PluginAssembly(IsolationMode = ...)]`
  has no per-step equivalent, unlike spkl.
- **Step state is not documented in attribute mode.** A registered plugin is a registered
  plugin, so steps are written whether or not they are active. The summary comment does
  mark disabled steps, because a comment has no such constraint.
- **`StepAttribute.State` and `StepAttribute.SupportedDeployment` can never be emitted.**
  They are declared as nullable enums upstream, which C# rejects as attribute named
  arguments (`CS0655`, reproduced against v1.0.57). The generated definitions file
  mirrors the defect deliberately rather than diverging from the package.
- **Impersonation is only in the summary comment.** `StepAttribute` carries it as
  `ImpersonatingUserFullname`, a plain string that resolves by name, so a step could point
  at a different user in a different environment. The comment states it as a fact instead
  of trying to redeploy it.
- **Classes are matched by name.** A class declared in more than one file under the
  selected folder is reported as ambiguous and skipped rather than guessed at.

## Building

```powershell
.\build.ps1     # build Debug and copy the DLL into your local XrmToolBox Plugins folder
.\deploy.ps1    # copy the existing Debug DLL without rebuilding
.\publish.ps1   # build Release, pack, and push to NuGet.org
```

## Testing

`tests/` holds an end to end suite: a dozen empty plugin classes registered every way this
tool has to describe, driven entirely by `pac`.

```powershell
cd tests
.\register.ps1      # build the assembly, pack two solutions, import them
.\verify.ps1        # confirm the environment matches the test matrix
.\unregister.ps1    # take it all away again
.\xtb.ps1           # build the tool and open it in an XrmToolBox of its own
```

`xtb.ps1` puts a private XrmToolBox in `tests\.xtb` holding nothing but this tool, connects
it to the same environment and opens it, so testing a change is one command and cannot
disturb the XrmToolBox you work in.

Then compare what the tool writes with the expected output in
[tests/README.md](tests/README.md), which spells out, class by class, exactly what both
output modes should produce.

## License

MIT
