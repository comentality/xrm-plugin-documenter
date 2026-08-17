# What gets written

Two shapes, chosen with the **Write** toggle. They are independent: each replaces only its
own block, so switching modes never deletes the other one's work, and a class can carry
both.

Steps are ordered the way they run — stage, then execution order, then message name — so
the block reads as an execution plan rather than as whatever order the query returned.

## Xrm Tools attributes

![The attribute output in the preview pane](https://raw.githubusercontent.com/comentality/xrm-plugin-documenter/main/assets/ui-attributes.png)

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

These are [Xrm Tools](https://github.com/rezanid/xrmtools) attributes and they compile —
see [Attribute definitions file](attribute-definitions.md) for what has to be in the
project for that.

**Attribute order is load bearing.** `[Image]` binds to the nearest preceding `[Step]`, so
steps are written in execution order with their own images following them. Do not sort
them.

### The style

The widest positional constructor the step's data supports, and everything else as named
properties:

| Attribute | Positional | Named, when not the default |
|---|---|---|
| `[Plugin]` | — | `Description` |
| `[Step]` | message, entity, filtering attributes, stage, mode | `Name`, `ExecutionOrder`, `Description`, `Configuration`, `AsyncAutoDelete` |
| `[Image]` | image type, columns | `Name`, `EntityAlias`, `MessagePropertyName` |

A step on a **global message** — one registered against no entity — uses the constructor
overload with no entity at all, rather than writing `"none"` the way spkl does.
Filtering attributes are only positional when there is an entity to filter on, because the
constructor has no overload that takes one without the other.

### What is left out, and why

Anything at its default would be noise on the way back:

- **`ExecutionOrder`** when the rank is 1.
- **`Name`** when it matches the name Dataverse generates for an unnamed step,
  `Type: Message of entity`. A name somebody typed is kept.
- **`Name` and `EntityAlias` on an image** when they match the image type's own name
  (`PreImage`, `PostImage`, `Both`).
- **`MessagePropertyName`** when it is `Target`.
- Empty descriptions and configuration.

Free text is written as a C# literal, with `\`, `"`, tab, carriage return and newline
escaped, so a description containing quotes still compiles.

### Line wrapping

One line while it stays readable. Past 120 characters, and only when there are named
arguments to move, each named argument goes onto its own line indented four spaces —
positional arguments stay on the first line where they read as a signature. A step with no
named arguments is never wrapped, however long its filter list.

### Stages

`Stages.PreValidation` (10), `Stages.PreOperation` (20), `Stages.MainOperation` (30),
`Stages.PostOperation` (40). Stage 50 is retired and has no enum member in either the real
package or the generated definitions, so it is emitted as `(Stages)50`, which compiles
against both.

## Readable summary comment

![The summary comment output in the preview pane](https://raw.githubusercontent.com/comentality/xrm-plugin-documenter/main/assets/ui-comment.png)

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

Each step is one line: mode, stage, message, entity, then the facts in brackets, then the
columns after the colon. Images follow their step, indented, pre before post.

Deliberately not a second serialisation. Step names, descriptions and configuration are
left out as noise; the unlabelled list after the colon is the step's filtering attributes,
which [Dataverse now honours on `Create` as well as
`Update`](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/register-plug-in).

### Two facts no attribute can carry

Because nothing here has to compile back into an attribute, the comment can say two things
the attribute model cannot express:

- a **disabled** step, as `disabled` in the brackets
- the user a step impersonates — PRT's *Run in User's Context* — as `As <name>`

Both appear only when they differ from the default. The impersonated user's name is the
only free text in the whole block, so it is the only place that could break the doc
comment's XML; `&`, `<` and `>` are escaped for that reason.

### `(all columns)`

Nothing that has columns is ever left blank. An unfiltered `Create` or `Update` step, and
an image with no columns, both read `(all columns)` — bracketed, so it reads as a remark
about the list rather than as a name in it. For an image that is Microsoft's
[explicit bad practice](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/register-plug-in)
rather than a neutral default, and worth seeing in a diff.

Messages that filter nothing — `Delete`, `Associate`, a custom action — keep their bare
header, because there is no column list to have omitted.

### Wrapping

Lines are held to 100 characters including the `///` and the indent. A column list that
does not fit moves onto continuation lines indented a further four spaces, with each
line still ending in a comma so it reads as continuing.

### The block the tool owns

A `<remarks>` block whose first line is `Register:`. That marker is how the tool
recognises its own work on a later run and replaces it in place. **Any other `<remarks>`
you have written is left alone**, as is your `<summary>` and every attribute that is not
`[Plugin]`, `[Step]` or `[Image]`.
