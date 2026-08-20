# Changelog

## Unreleased

- **A third column holds the source folder against the registrations.** The folder picker
  moved there from above the preview, and as soon as a folder and loaded classes exist the
  tool scans in the background and keeps a ledger: which registered classes have a `.cs`
  file (*current*, *stale* — the tool's output is present but no longer matches the
  registration — or nothing written yet), which have **no file**, which are **ambiguous**,
  and which plugin classes sit **in the folder with no registration** behind them. Every
  class row carries the verdict as a glyph (✓ ✎ ✗ ⚠), every assembly row a roll-up
  (`4/5 ⚠`), and selecting a row in either list highlights it in the other. What used to be
  discovered by pressing *Write to Files* is now on screen before it.
- **Write to both files when ambiguous.** A checkbox that only appears when the scan has
  found an ambiguity. Off, an ambiguous class is skipped as before; on, every file
  declaring the class gets the same output — the splice replaces only the tool's own
  block, so the partial-class case the ambiguity usually is ends up documented on both
  halves.
- **The preview is togglable.** A *Preview ▸* button in the write toolbar collapses the
  code view and hands its width to the source column, for the sessions that are about
  auditing the marks rather than reading what would be written; *◂ Preview* brings it
  back. The write controls sit above both panes and stay put either way.
- **Refresh.** Rereads the assemblies and their steps without resetting the session:
  what is ticked, what is excluded, the filter and the folder all survive. For the loop
  the tool lives in — register from the IDE, come back, refresh, write.
- The hint above the buttons now says what a write would do (`Will write 5 classes ·
  2 skipped (1 no file, 1 ambiguous)`) instead of going quiet once the folder was valid.

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
