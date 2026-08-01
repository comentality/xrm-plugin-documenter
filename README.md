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
2. Selecting one loads every plugin type that has at least one registered step.
3. **Preview Attributes** shows exactly what would be written.
4. **Write to Files** finds the `.cs` file declaring each class and splices the
   attributes in above the class declaration.
5. **Create Attribute Definitions File** drops a dependency-free
   `XrmToolsMetaAttributes.cs` into your project so the emitted attributes compile
   without the NuGet package or the Visual Studio extension.

Every file that changes gets a timestamped `.bak` copy beside it.

## Output

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
- **Step state is not documented.** A registered plugin is a registered plugin, so steps
  are written whether or not they are active.
- **`StepAttribute.State` and `StepAttribute.SupportedDeployment` can never be emitted.**
  They are declared as nullable enums upstream, which C# rejects as attribute named
  arguments (`CS0655`, reproduced against v1.0.57). The generated definitions file
  mirrors the defect deliberately rather than diverging from the package.
- **Classes are matched by name.** A class declared in more than one file under the
  selected folder is reported as ambiguous and skipped rather than guessed at.

## Building

```powershell
.\build.ps1     # build Debug and copy the DLL into your local XrmToolBox Plugins folder
.\deploy.ps1    # copy the existing Debug DLL without rebuilding
.\publish.ps1   # build Release, pack, and push to NuGet.org
```

## License

MIT
