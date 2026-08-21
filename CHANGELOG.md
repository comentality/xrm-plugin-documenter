# Changelog

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
