# Attribute definitions file

The attributes this tool writes are [Xrm Tools](https://github.com/rezanid/xrmtools)
attributes. For them to compile, the types have to exist in your project. There are two
ways to get them, and you must pick exactly one.

## Option 1 — the NuGet package

```xml
<PackageReference Include="XrmTools.Meta.Attributes" Version="1.2.0" />
```

The upstream package. Use it if you are already using Xrm Tools, or want its source
generators.

Any version works. The package has had two shapes — up to 1.1.3 it added its attributes to
your compilation as source files and declared them `public`, from 1.1.4 a source generator
emits them and declares them `internal` unless you set
`<XrmToolsMetaAttributesUsePublicAccessibility>`. Neither shape changes what this tool
writes, and both are compiled against on every test run.

## Option 2 — the generated file

**Create Attribute Definitions File** writes `XrmToolsMetaAttributes.cs` into your source
folder: a minimal, dependency-free subset of `XrmTools.Meta.Attributes` holding

- `PluginAttribute`, `StepAttribute`, `ImageAttribute`
- `PluginAssemblyAttribute`
- the enums they need — `Stages`, `ExecutionMode`, `ImageTypes`, and the rest

Namespace, type names, constructor signatures, property names and enum values are
identical to the published package, so the file is a drop-in for it: delete it at any time,
add the `PackageReference`, and the same source still compiles.

The file is written UTF-8 with a BOM, and you are asked before an existing one is
overwritten. It is meant to be committed — it is source, not a build artefact.

Both routes are verified, and by more than eye. `tests\compat.ps1` builds a corpus that
hits every decision the emitter makes, compiles it against the generated file and against
the real package pulled from nuget.org at four versions, and then compares what the two
builds produced: the constructor the compiler bound each attribute to, and every property
of the attribute objects the runtime constructs. Those have to be identical. Defaults are
the reason it goes as far as constructing them — the emitter leaves `ExecutionOrder` out
when the rank is 1 *because* `StepAttribute` defaults it to 1, and only building one
proves that is still true on both sides.

## Do not use both

The package generates the same types into your compilation. Having both gives you `CS0101`
duplicate type errors on every one of them. The tool says so when it writes the file, but
it cannot check your project for you.

## The deliberate defect

`StepAttribute.State` and `StepAttribute.SupportedDeployment` are declared upstream as
**nullable enums**, which C# rejects as attribute named arguments (`CS0655`, reproduced
against v1.0.57). Neither can ever be emitted by anything, this tool included.

The generated file mirrors that rather than quietly fixing it. A definitions file that
diverged from the package would compile source the package would reject, which is a worse
problem than the one it would solve — and the two properties are not ones this tool would
emit anyway. See [Limits](limits.md).
