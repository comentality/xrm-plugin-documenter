# Changelog

## Unreleased

- **The registered namespace now settles a short-name tie.** When several `.cs` files
  declare a class of the same short name, the file whose `namespace` declaration matches
  the namespace the type was registered under is the one written, provided exactly one
  file does. Two projects in one tree with a class name in common no longer come back as
  *Ambiguous*. A tie the namespace cannot settle — a partial class spanning files, the
  same namespace declared twice, a namespace declared in nested form — is still reported
  as ambiguous and no file is touched.

## 1.0.0

First release.

Reads the plugin steps and images registered in the connected Dataverse environment and
writes them into your C# source, as [Xrm Tools](https://github.com/rezanid/xrmtools)
`[Plugin]`, `[Step]` and `[Image]` attributes or as a readable summary comment.

- **Assembly list** defaults to the unmanaged assemblies — the plugin somebody is
  writing — with **Microsoft's** and **Managed** as switches carrying the count of what
  they hold back. Microsoft's own are told apart by strong name signature rather than by
  name, so a first party app shipped under its own name is still recognised and an
  assembly of yours called `Microsoft.*` is not.
- **Two output modes**, independent of each other: attributes that compile, and a
  `<remarks>` block that carries the two facts no attribute can express — a disabled step
  and the user a step impersonates.
- **Writes into existing files** above the class declaration, replacing only its own
  output and leaving your summaries, your other attributes and the class body alone. Every
  changed file gets a timestamped `.bak` beside it.
- **Create Attribute Definitions File** writes a dependency-free
  `XrmToolsMetaAttributes.cs`, so the emitted attributes compile without the
  `XrmTools.Meta.Attributes` package or the Visual Studio extension.
- Nothing is ever written to the environment; a read-only role is enough.

Requires XrmToolBox 1.2025.7 or later.
