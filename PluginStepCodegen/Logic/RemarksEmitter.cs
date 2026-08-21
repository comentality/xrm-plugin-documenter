using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PluginStepCodegen.Logic
{
    /// <summary>
    /// Renders a plugin type's registration as an XML doc comment meant to be read,
    /// not deployed.
    ///
    /// Because nothing has to compile back into an attribute, this can carry facts the
    /// Xrm Tools attribute model cannot express: a disabled step, and the user a step
    /// impersonates. Everything else is deliberately left out - step names, descriptions
    /// and configuration are noise when you are skimming.
    ///
    /// Given the entities' current columns, a list that covers nearly all of them is said
    /// as its exceptions - "(all columns except: ...)" - because with seventy names the
    /// question a reader has is which five are not there. Without the columns every list
    /// is rendered as written.
    /// </summary>
    public static class RemarksEmitter
    {
        /// <summary>Longest line, including the leading /// and any indent.</summary>
        private const int MaxWidth = 100;

        /// <summary>First line inside the block. <see cref="CodeFileWriter"/> uses it to
        /// recognise a block this tool owns and may replace.</summary>
        public const string Marker = "Register:";

        /// <summary>Stands in for the column list when there is none. Bracketed, so it reads as
        /// a remark about the list rather than as a name in it.</summary>
        private const string AllColumns = "(all columns)";

        public static IEnumerable<string> Emit(PluginTypeInfo type, IDictionary<string, EntityColumnsInfo> columnsByEntity = null)
        {
            var lines = new List<string> { "/// <remarks>", "/// " + Marker };

            foreach (var step in type.Steps)
            {
                var entity = ColumnsOf(columnsByEntity, step.PrimaryEntityName);
                lines.AddRange(Compose(string.Empty, StepHeader(step), StepColumns(step, entity)));
                foreach (var image in step.Images)
                {
                    // No attributes on an image means every column, which Microsoft
                    // explicitly calls out as bad practice. Worth saying out loud.
                    lines.AddRange(Compose("    ", ImageName(image.ImageType),
                        string.IsNullOrWhiteSpace(image.Attributes)
                            ? AllColumns
                            : Describe(image.Attributes, entity == null ? null : entity.ImageColumns)));
                }
            }

            lines.Add("/// </remarks>");
            return lines;
        }

        private static EntityColumnsInfo ColumnsOf(IDictionary<string, EntityColumnsInfo> columnsByEntity, string entityName)
        {
            if (columnsByEntity == null || string.IsNullOrWhiteSpace(entityName))
            {
                return null;
            }

            EntityColumnsInfo info;
            return columnsByEntity.TryGetValue(entityName, out info) ? info : null;
        }

        /// <summary>
        /// "Sync Post-Update of account (order 2, disabled, As SYSTEM)". Everything after
        /// the order is omitted unless it differs from the default.
        /// </summary>
        private static string StepHeader(PluginStepInfo step)
        {
            var entity = step.PrimaryEntityName;
            // Global messages register against no entity; spkl writes "none".
            var hasEntity = !string.IsNullOrWhiteSpace(entity)
                            && !string.Equals(entity, "none", StringComparison.OrdinalIgnoreCase);

            var facts = new List<string> { "order " + step.Rank.ToString(CultureInfo.InvariantCulture) };
            if (step.IsDisabled)
            {
                facts.Add("disabled");
            }

            if (!string.IsNullOrWhiteSpace(step.ImpersonatingUser))
            {
                // The only free text in the whole block, so the only place that can break
                // the doc comment's XML (CS1570). Everything else is a logical name.
                facts.Add("As " + step.ImpersonatingUser.Trim()
                    .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;"));
            }

            return Mode(step.Mode) + " " + Stage(step.Stage) + "-" + step.MessageName
                   + (hasEntity ? " of " + entity : string.Empty)
                   + " (" + string.Join(", ", facts) + ")";
        }

        /// <summary>
        /// A step with no filtering attributes fires on every column, so say that rather than
        /// leaving the reader to infer it from a missing list. Only where columns are part of
        /// the message though: Dataverse filters <c>Update</c> and <c>Create</c>, and on a
        /// <c>Delete</c> or a global message there is nothing to have filtered.
        /// </summary>
        private static string StepColumns(PluginStepInfo step, EntityColumnsInfo entity)
        {
            var list = List(step.FilteringAttributes);
            if (list.Length > 0)
            {
                return Describe(step.FilteringAttributes, entity == null ? null : entity.FilterColumns);
            }

            return string.Equals(step.MessageName, "Update", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(step.MessageName, "Create", StringComparison.OrdinalIgnoreCase)
                ? AllColumns
                : string.Empty;
        }

        /// <summary>
        /// The list as written, unless the entity's own columns show it is nearly all of them -
        /// then the handful left out is the readable fact, and seventy names collapse to
        /// "(all columns except: ...)". A list that turns out to be every column is still not
        /// called <see cref="AllColumns"/>: a registration with no list follows the table as it
        /// grows, a spelled-out one is pinned to today, and the difference is worth a word.
        /// </summary>
        private static string Describe(string csv, List<string> universe)
        {
            var items = Items(csv);
            var list = string.Join(", ", items);
            if (items.Count == 0 || universe == null || universe.Count == 0)
            {
                return list;
            }

            // A column the entity no longer has is the most interesting thing a list can
            // carry; folding it into an "except" would hide it, so such a list stays verbatim.
            var known = new HashSet<string>(universe, StringComparer.OrdinalIgnoreCase);
            if (!items.All(known.Contains))
            {
                return list;
            }

            var present = new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);
            var missing = universe.Where(c => !present.Contains(c)).ToList();
            if (missing.Count == 0)
            {
                return "(all " + universe.Count + " columns, written out)";
            }

            // "Except" has to mean a handful, or the phrase lies about the coverage; past a
            // quarter of what is included, the plain list says it straighter.
            return missing.Count * 4 <= present.Count
                ? "(all columns except: " + string.Join(", ", missing) + ")"
                : list;
        }

        /// <summary>
        /// One line when it fits, otherwise the head keeps its colon and the list wraps
        /// onto continuation lines indented a further four spaces.
        /// </summary>
        private static IEnumerable<string> Compose(string indent, string head, string list)
        {
            if (string.IsNullOrEmpty(list))
            {
                return new[] { "/// " + indent + head };
            }

            var single = "/// " + indent + head + ": " + list;
            if (single.Length <= MaxWidth)
            {
                return new[] { single };
            }

            var lines = new List<string> { "/// " + indent + head + ":" };
            lines.AddRange(Wrap(list, "/// " + indent + "    "));
            return lines;
        }

        private static IEnumerable<string> Wrap(string list, string prefix)
        {
            var items = list.Split(new[] { ", " }, StringSplitOptions.None);
            var line = prefix;

            for (var i = 0; i < items.Length; i++)
            {
                // The separator travels with the item it follows, so a wrapped line still
                // ends in a comma and reads as continuing.
                var item = i < items.Length - 1 ? items[i] + "," : items[i];

                if (line.Length > prefix.Length && line.Length + 1 + item.Length > MaxWidth)
                {
                    yield return line;
                    line = prefix;
                }

                line += line.Length > prefix.Length ? " " + item : item;
            }

            yield return line;
        }

        /// <summary>Stored comma separated with no spaces, which does not wrap or read well.</summary>
        private static string List(string value)
        {
            return string.Join(", ", Items(value));
        }

        private static List<string> Items(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new List<string>();
            }

            return value
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim())
                .Where(a => a.Length > 0)
                .ToList();
        }

        private static string Stage(int stage)
        {
            switch (stage)
            {
                case 10: return "PreValidation";
                case 20: return "Pre";
                case 30: return "Main";
                case 40: return "Post";
                // Every other value is either deprecated or reserved for internal use,
                // so name it by number rather than inventing a label.
                default: return "Stage" + stage.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static string Mode(int mode)
        {
            return mode == 1 ? "Async" : "Sync";
        }

        private static string ImageName(int imageType)
        {
            switch (imageType)
            {
                case 0: return "PreImage";
                case 1: return "PostImage";
                default: return "PreImage and PostImage";
            }
        }
    }
}
