# Changelog

## Unreleased

- **A registered class with no steps is no longer filed as unregistered.** A plugin type
  somebody registered and has not written a step for yet is not listed — there is nothing
  to document — so its `.cs` file fell through to **In folder, not registered**, whose
  tooltip said in as many words that nothing registers it. That is the opposite of what
  happened to that class, and it sent you off to register something that was already
  registered. Those files now have a group of their own, and the middle column's status
  line counts them apart from the classes nothing registers at all.

  ![The two groups, side by side](assets/changelog-no-steps.png)

- **The assembly rows say what their Source cell means.** Three of its states are blanks
  that look alike — not ticked yet, no source folder chosen, nothing registered in the
  assembly — and the roll-up's glyph says how bad the news is without saying which news it
  is. Every state now answers when the row is hovered, and an assembly with no step
  registered against anything in it reads `—` as soon as it is ticked, rather than staying
  blank until a folder is chosen.

- **The tool behaves itself on a slow link.** XrmToolBox draws a small panel in the middle of
  a tool while it waits on the environment — not a sheet over the whole tab — so every button
  stayed live for however long the fetch took. On a fast connection that window is
  milliseconds wide. On a slow one it is where most of a session is spent, and eight things
  went wrong in it:

  - **Closing the tab mid-fetch could take the tab down with it.** The answer still arrived,
    and was handed to a control that no longer existed. Every fetch now checks before it
    draws, the way the folder scan always has.
  - **A late answer could overwrite a newer one — for good.** Press **Refresh** while a fetch
    is still out and two answers to two different questions are in the air; whichever landed
    last was believed, and because the assembly was then in the cache it was never asked
    about again. Fetches now carry the question they belong to and are dropped if it has
    moved on.
  - **Ticking assemblies one at a time re-asked for the ones already on their way.** Five
    ticks put the first assembly on the wire five times. There is now one question out at a
    time, and whatever is ticked meanwhile goes out together when it lands — one round trip
    for the batch instead of one per tick.
  - **Load Assemblies and Write to Files are dead while their own work runs.** Pressing Load
    again cost a second full query whose answer cleared everything ticked since the first;
    pressing Write twice put two writers over the same files, where the backup name is only
    accurate to the second, so the two `.bak` copies collided and the pristine original was
    the copy that got lost.
  - **Write waits for the environment.** It was offered while an assembly was still loading,
    and would write a half-loaded list with a report that read like a complete one.
  - **The status lines stop claiming what has not arrived.** `2 assemblies · 4 of 4 classes`
    is a complete-sounding sentence about a list with an assembly missing from it, and every
    class of that assembly was filed, in words, under **In folder, not registered** — then
    quietly un-filed when the network caught up. The count now says what it is waiting on,
    the assembly's own row shows `…`, and nothing is called unregistered until every
    registration is in.

    ![The status line and the assembly rows while a fetch is still out](assets/changelog-still-loading.png)

    ![Write to Files, greyed, saying what it is waiting for](assets/changelog-write-waiting.png)

  - **A fetch can be abandoned.** The progress panel now offers Cancel. A query already on
    the wire cannot be recalled, but the three round trips behind it can be called off, which
    is most of the wait on the link where it matters. Nothing is recorded, so unticking and
    ticking again asks afresh.
  - **A fetch that fails says so where you can still read it.** The dialog is dismissed and
    then there is only the list, and an environment that could not be reached looked exactly
    like an environment with nothing in it. The status line keeps the reason.

  `tests\slow.ps1` is what found all of it: ten scenarios driving the real control against
  a Dataverse that takes seconds to answer, with a screenshot per gesture.

- **The source folder can be slow too, and no longer freezes the window when it is.** A plugin
  repository is as likely to be on a UNC share, a mapped drive or a sync client that has
  stopped syncing as it is to be on a disk, and against one of those simply asking whether a
  folder exists blocks until the network gives up. That question was being asked from the
  button-state pass, which runs **on every keystroke in the folder box** — so typing the path
  to a share that was down froze the tool once per character. It is now asked once, on a
  worker, after the same half-second pause the scan already waits out, and everything else
  reads the answer it left. The box says `Looking for that folder...` while the question is
  out, rather than calling the path wrong before anybody has looked.

  **Create Attribute Definitions File** was the last thing still writing on the UI thread —
  both looking for the file and writing it. It runs on a worker like every other write.

- **The source folder is read once, not once per class.** A project of 250 files with 33
  registered classes took four seconds to scan and six to write. Reading all 250 files takes
  twelve milliseconds, so none of that was ever the disk: every registered class was
  re-scanning every file's text with a regex of its own, working out which local classes are
  plugins built a fresh `Regex` for every name-against-class pair and did it again on every
  pass, and one press of **Write to Files** walked and re-read the whole folder once per
  class. The folder is now read and parsed once and answered out of a dictionary.

  | On 250 files, 33 classes | Was | Now |
  |---|---|---|
  | Scan | 4051 ms | 17 ms |
  | Write | 6306 ms | 48 ms |

  A 4000 file repository — larger than most plugin repositories get — now scans in about
  600 ms, of which 430 ms is reading the files.

- **Write no longer freezes the window.** It ran on the UI thread, so a slow write was a
  tool that had stopped repainting. It runs under the same progress overlay as loading the
  assemblies does, and says how many classes it is writing.

- **Marks from another folder are cleared when you change folders**, rather than sitting
  there looking authoritative until the new scan lands. A rescan of the *same* folder — the
  one that follows a write — keeps its marks up, because blanking the column on the way back
  from a successful write reads as a fault.

- `tests\perf.ps1` times the scan and the write over generated repositories of 250 to 4000
  files, against what reading the folder once costs on the same machine. It is what found
  all of the above.

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

- **Nothing that goes wrong should reach XrmToolBox's crash dialog.** Everything the tool
  does on a worker already reported itself — a failed query, a locked file, a folder that
  cannot be read — but the same work on the UI thread had no net under it at all, so one
  unlucky registration could take the tab down while drawing a list. Composing the output
  for a class is now guarded everywhere it happens: in the preview the class says so in its
  own place and the rest of the list still renders, in the source column it reads as stale,
  and on **Write to Files** it lands in the report's Failed section by name, beside the
  files that could not be written. A settings file XrmToolBox cannot read or write no
  longer costs the tab either — that read happens before any of the UI exists, so a throw
  there was a tool that would not open and gave no reason. And a folder nested past what
  Windows will open — which a package cache manages without trying — is stepped over the
  way an unreadable one already was, rather than failing the whole scan.

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
