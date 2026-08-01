using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PluginDocumenter.Logic
{
    /// <summary>
    /// Renders a plugin type's registration as an XML doc comment meant to be read,
    /// not deployed.
    ///
    /// Because nothing has to compile back into an attribute, this can carry facts the
    /// Xrm Tools attribute model cannot express: a disabled step, and the user a step
    /// impersonates. Everything else is deliberately left out - step names, descriptions
    /// and configuration are noise when you are skimming.
    /// </summary>
    public static class RemarksEmitter
    {
        /// <summary>Longest line, including the leading /// and any indent.</summary>
        private const int MaxWidth = 100;

        /// <summary>First line inside the block. <see cref="CodeFileWriter"/> uses it to
        /// recognise a block this tool owns and may replace.</summary>
        public const string Marker = "Register:";

        public static IEnumerable<string> Emit(PluginTypeInfo type)
        {
            var lines = new List<string> { "/// <remarks>", "/// " + Marker };

            foreach (var step in type.Steps)
            {
                lines.AddRange(Compose(string.Empty, StepHeader(step), List(step.FilteringAttributes)));
                foreach (var image in step.Images)
                {
                    // No attributes on an image means every column, which Microsoft
                    // explicitly calls out as bad practice. Worth saying out loud.
                    lines.AddRange(Compose("    ", ImageName(image.ImageType),
                        string.IsNullOrWhiteSpace(image.Attributes) ? "(all columns)" : List(image.Attributes)));
                }
            }

            lines.Add("/// </remarks>");
            return lines;
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
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return string.Join(", ", value
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim())
                .Where(a => a.Length > 0));
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
