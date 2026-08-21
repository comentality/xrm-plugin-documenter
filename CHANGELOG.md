# Changelog

## Unreleased

- **The summary comment is grouped by table.** A generic plugin — one class registered
  against a dozen tables to stamp the same column on all of them — used to read as one
  interleaved list, every table's steps scattered through it by stage. The comment now
  takes a table at a time, tables alphabetically, keeping the order each table's own steps
  run in; a step on a global message goes last.

  ```csharp
  /// Sync Post-Create of account (order 1): (all columns)
  /// Sync Post-Update of account (order 1): (all columns)
  /// Sync Pre-Update of annotation (order 1): (all columns)
  /// Sync Post-Create of annotation (order 1): (all columns)
  ```

  **The attributes are unchanged** and stay in execution order: that is the order Xrm Tools
  reads them back in. The one thing that did change there is a tiebreak — steps tying on
  stage, rank *and* message name are now ordered by table rather than by whatever the query
  returned, so the same registration writes the same file twice running.

- **Credit where it is due.** The docs, the store listing, the generated definitions file
  and the dialog that writes it now say plainly whose attribute model this is:
  [Xrm Tools](https://github.com/rezanid/xrmtools), the Visual Studio extension that reads
  these same attributes back to deploy and register an assembly — and that is worth
  installing whether or not you use this. Compatibility with it is this tool's premise; it
  had earned more than a passing link.
- `XrmToolsMetaAttributes.cs` gains `Stages.DepecratedPostOperation = 50` — upstream's
  spelling, upstream's `[Obsolete]` — which it had been missing. Nothing the tool writes
  changes: a step at the retired stage 50 is still emitted as `(Stages)50`, because naming
  that member is a compile error against the real package too.
- The emitted attributes are now checked against the real `XrmTools.Meta.Attributes`
  package rather than only against our copy of it. `tests\compat.ps1` compiles a corpus
  covering every emitter decision against both, at four package versions, and compares the
  constructed attributes property by property.

## 1.1.0

- **"All columns except" phrasing** (experimental, off by default): an image or filter
  covering nearly all of an entity's columns reads `(all columns except: creditlimit,
  ilac_legacyid)` instead of reciting seventy names. A list that covers every column reads
  `(all 75 columns, written out)`; a stale column name is left verbatim so it stays visible.
- **Experimental settings** behind a new † button in the write toolbar, remembered across
  sessions.

  ![The dagger button beside the preview toggle](assets/changelog-guru-dagger.png)

- **Source folder status column**: the folder picker moved into a third column that scans
  in the background and marks every class — current ✓, stale ✎, no file ✗, ambiguous ⚠ —
  with per-assembly roll-ups and cross-highlighting between the lists.
- **Write to both files when ambiguous** (optional): every file declaring the class gets
  the same block, so a partial class is documented on both halves.
- **Togglable preview**: collapse the code pane to give its width to the source column.
- **Refresh**: rereads assemblies and steps without losing ticks, filter, or folder.
- **Namespace-aware file matching**: a short-name tie between files is settled by the
  registered namespace, so same-named classes in two projects no longer come back
  ambiguous.
- The hint above the buttons now says what a write would do
  (`Will write 5 classes · 2 skipped (1 no file, 1 ambiguous)`).

## 1.0.0

First release.

- Reads the plugin steps and images registered in the connected environment and writes
  them into your C# source, as [Xrm Tools](https://github.com/rezanid/xrmtools)
  `[Plugin]`, `[Step]` and `[Image]` attributes or as a readable summary comment.
- Assembly list defaults to the unmanaged assemblies; **Microsoft's** and **Managed** are
  switches carrying the count of what they hold back.
- Writes above the class declaration, replacing only its own block; every changed file
  gets a timestamped `.bak` beside it.
- **Create Attribute Definitions File** emits a dependency-free
  `XrmToolsMetaAttributes.cs`, so the attributes compile without the
  `XrmTools.Meta.Attributes` package.
- Read-only: nothing is ever written to the environment.

Requires XrmToolBox 1.2025.7 or later.
