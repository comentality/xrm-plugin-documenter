# Writing to files

**Write to Files** takes what is in the preview and puts it in your source. This page is
what it does to a file, and what it will refuse to do.

## Finding the file for a class

The tool searches the **source folder** and everything under it for a `.cs` file that
declares the class, matching on the **short class name** — `AccountManager`, not
`Contoso.Plugins.Accounts.AccountManager`.

Skipped while searching:

- anything under a `\bin\` or `\obj\` folder
- `*.g.cs` and `*.designer.cs`

Exactly one match is required.

- **No match** — the class is reported under *No matching .cs file* and nothing is
  written. This is normal for an assembly whose source is not in the folder you chose.
- **Several matches** — reported as *Ambiguous, several files declare the class*, and
  **no file is modified**. The namespace would often settle it, but a partial class
  legitimately spans files and guessing wrong means writing a registration into the wrong
  class. It is left for you to decide.

The commonest cause of an ambiguity is two projects in the same tree that both define a
class of that name. Narrow the source folder to one project and run again.

## What is replaced, and what is not

The output is spliced in immediately above the class declaration, at the declaration's own
indentation, in the file's existing line ending and encoding.

The tool replaces **only what it owns**:

| In the file | What happens |
|---|---|
| `[Plugin]`, `[Step]`, `[Image]` above the class | replaced, when writing attribute mode |
| a `<remarks>` block whose first line is `Register:` | replaced, when writing comment mode |
| your `<summary>`, any other `<remarks>` | untouched |
| `[Obsolete]`, `[CrmPluginRegistration]`, any other attribute | untouched, and kept in place |
| the class body | untouched |

Writing one mode never disturbs the other, so a class can carry both, and switching the
toggle does not silently delete the work of the mode you switched away from.

The comment block sits **above** the attributes, so that it stays contiguous with a hand
written `<summary>` and the two read as one doc comment.

## Backups

Every file that changes gets a copy beside it named `<file>.yyyyMMddHHmmss.bak` — the
original content, in the original encoding, before the write.

A file whose content would not change is not rewritten and gets no backup; it is reported
as *Already up to date*. Running twice in a row therefore produces one set of backups, not
two.

The backup name is accurate to the second. Two writes to the same file inside the same
second collide, and the first backup is the one lost — which only happens if the same
class is registered in two assemblies you have both ticked.

Nothing cleans these up. They are `.bak` files in your source tree; add `*.bak` to
`.gitignore` or sweep them when you are happy with the result.

## The report

The preview is replaced by a tally of what happened, and a dialog gives the same counts:

```
// Updated (6)
//   AccountPreValidation
//   ...

// Already up to date (1)
//   ErpOrderSync

// No matching .cs file (2)
//   Bravo
//   Ghost

// Ambiguous, several files declare the class (1)
//   Rival (2 files)
```

*Failed* appears for anything that threw — a file locked by another process, a read-only
file, a permission problem — with the message beside the class name.

A class that appears twice under *Updated* is a class registered in two ticked assemblies
and resolving to one file. Both registrations were written, in assembly order, and the
second is the one now in the file. Nothing is lost silently: it is named twice, once per
write.

## What is never written

The environment. The tool issues no create, update or delete against Dataverse — it reads
the registration and writes source. Fixing a registration is still the Plugin Registration
Tool's job.
