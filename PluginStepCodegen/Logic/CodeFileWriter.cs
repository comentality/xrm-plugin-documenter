using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PluginStepCodegen.Logic
{
    /// <summary>
    /// Locates the .cs file declaring a plugin class and splices registration
    /// attributes in above the class declaration.
    /// </summary>
    public static class CodeFileWriter
    {
        /// <summary>
        /// Matches a balanced attribute group sitting at the very end of the text,
        /// starting at the beginning of its own line. String literals are skipped so
        /// that brackets inside them do not unbalance the match.
        /// </summary>
        private static readonly Regex TrailingAttribute = new Regex(
            @"(?m)^[ \t]*\[(?:[^\[\]""]|""(?:\\.|[^""\\])*""|(?<open>\[)|(?<-open>\]))*(?(open)(?!))\][ \t]*\r?\n?\z",
            RegexOptions.Compiled);

        /// <summary>Attributes this tool owns, and therefore may replace.</summary>
        private static readonly Regex OwnedAttribute = new Regex(
            @"^\s*\[\s*(Plugin|Step|Image)(Attribute)?\s*[\(\]]",
            RegexOptions.Compiled);

        /// <summary>
        /// A doc comment this tool owns: a remarks block at the very end of the text whose
        /// first line is the emitter's marker. Any other remarks block is the user's.
        /// </summary>
        private static readonly Regex TrailingRemarks = new Regex(
            @"(?m)^[ \t]*///[ \t]*<remarks>[ \t]*\r?\n"
            + @"[ \t]*///[ \t]*" + Regex.Escape(RemarksEmitter.Marker) + @"[ \t]*\r?\n"
            + @"(?:[ \t]*///.*\r?\n)*?"
            + @"[ \t]*///[ \t]*</remarks>[ \t]*\r?\n?\z",
            RegexOptions.Compiled);

        public static Regex ClassDeclaration(string className)
        {
            return new Regex(
                @"(?m)^[ \t]*(?:(?:public|internal|private|protected|abstract|sealed|static|partial|new)[ \t]+)*class[ \t]+"
                + Regex.Escape(className) + @"\b");
        }

        /// <summary>
        /// Matches a declaration of exactly this namespace, block or file scoped. A nested
        /// declaration (<c>namespace A { namespace B {</c>) is not recognised, deliberately:
        /// the caller uses a failed match to leave a tie unresolved, never to disqualify the
        /// only file, so the miss is safe in the one direction it can happen.
        /// </summary>
        public static Regex NamespaceDeclaration(string namespaceName)
        {
            return new Regex(
                @"(?m)^[ \t]*namespace[ \t]+" + Regex.Escape(namespaceName) + @"\s*[{;]");
        }

        /// <summary>
        /// Finds the single .cs file declaring <paramref name="className"/>. Returns null
        /// when there is no match; sets <paramref name="ambiguous"/> when there are several.
        /// When several files declare the short name, the registered namespace breaks the
        /// tie if exactly one of them declares it.
        /// </summary>
        public static string FindFile(string folder, string className, string namespaceName, out List<string> ambiguous)
        {
            var files = EnumerateSources(folder)
                .Select(f => new KeyValuePair<string, string>(f, ReadAllText(f)));
            return FindInFiles(files, className, namespaceName, out ambiguous);
        }

        /// <summary>Every .cs file under the folder that somebody wrote, as opposed to a build's.</summary>
        public static IEnumerable<string> EnumerateSources(string folder)
        {
            return Directory
                .EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
                .Where(f => !IsGenerated(f));
        }

        /// <summary>
        /// The matching behind <see cref="FindFile"/>, over texts already read, so the scan
        /// that marks the lists and the write that trusts those marks cannot disagree.
        /// </summary>
        public static string FindInFiles(IEnumerable<KeyValuePair<string, string>> files, string className, string namespaceName, out List<string> ambiguous)
        {
            ambiguous = null;
            var declaration = ClassDeclaration(className);

            var matches = files
                .Where(f => declaration.IsMatch(f.Value))
                .ToList();

            // Only exactly one survivor settles a tie. Zero means the namespace was not
            // found as written - possibly declared nested, possibly the registration is
            // stale - and two or more is a partial class spanning files, and in either
            // case picking a file would mean writing a registration into the wrong class.
            if (matches.Count > 1 && !string.IsNullOrEmpty(namespaceName))
            {
                var ns = NamespaceDeclaration(namespaceName);
                var narrowed = matches.Where(f => ns.IsMatch(f.Value)).ToList();
                if (narrowed.Count == 1)
                {
                    matches = narrowed;
                }
            }

            if (matches.Count == 0)
            {
                return null;
            }

            if (matches.Count > 1)
            {
                ambiguous = matches.Select(f => f.Key).ToList();
                return null;
            }

            return matches[0].Key;
        }

        private static bool IsGenerated(string path)
        {
            if (path.IndexOf(@"\obj\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf(@"\bin\", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            var name = Path.GetFileName(path);
            return name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
                   || name.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadAllText(string path)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException)
            {
                return string.Empty;
            }
            catch (UnauthorizedAccessException)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Replaces this tool's own output above the class declaration, leaving anything
        /// else in place.
        ///
        /// The two outputs are independent: pass null for either to leave whatever is
        /// already in the file untouched, so switching output mode never silently deletes
        /// the other mode's work.
        /// </summary>
        public static string Splice(string code, string className, IEnumerable<string> remarks, IEnumerable<string> attributes)
        {
            var match = ClassDeclaration(className).Match(code);
            if (!match.Success)
            {
                throw new InvalidOperationException("Could not find a declaration for class '" + className + "'.");
            }

            var lineStart = match.Index;
            var declarationLine = code.Substring(lineStart, match.Length);
            var indent = declarationLine.Substring(0, declarationLine.Length - declarationLine.TrimStart(' ', '\t').Length);

            var head = code.Substring(0, lineStart);
            var tail = code.Substring(lineStart);

            // Peel every attribute directly above the class. This happens even when we are
            // not writing attributes, because our remarks block sits above them and cannot
            // be found otherwise; in that case they are all put back untouched.
            var kept = new List<string>();
            while (true)
            {
                var attribute = TrailingAttribute.Match(head);
                if (!attribute.Success)
                {
                    break;
                }

                if (attributes == null || !OwnedAttribute.IsMatch(attribute.Value))
                {
                    kept.Insert(0, attribute.Value);
                }

                head = head.Substring(0, attribute.Index);
            }

            // Our remarks block sits above the attributes, so that it stays contiguous with
            // any hand written summary and the two read as one doc comment.
            if (remarks != null)
            {
                var existing = TrailingRemarks.Match(head);
                if (existing.Success)
                {
                    head = head.Substring(0, existing.Index);
                }
            }

            var newline = code.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            var sb = new StringBuilder(head);
            if (remarks != null)
            {
                foreach (var line in remarks)
                {
                    sb.Append(indent).Append(line).Append(newline);
                }
            }

            foreach (var attribute in kept)
            {
                sb.Append(attribute);
            }

            if (attributes != null)
            {
                foreach (var attribute in attributes)
                {
                    // Re-indent continuation lines of multi-line attributes to match the class.
                    var text = attribute.Replace("\r\n", "\n").Replace("\n", newline + indent);
                    sb.Append(indent).Append(text).Append(newline);
                }
            }

            return sb.Append(tail).ToString();
        }

        /// <summary>
        /// Where a matched file stands relative to what a write would put in it.
        /// </summary>
        public enum WriteState
        {
            /// <summary>Nothing of this tool's in the file yet; a write would add it.</summary>
            Missing,
            /// <summary>The file already says exactly what the registration says.</summary>
            Current,
            /// <summary>This tool's output is present but no longer matches the registration.</summary>
            Stale
        }

        /// <summary>
        /// Classifies without writing: the same splice a write would do, compared against
        /// what is there. Null outputs mean the same here as in <see cref="Splice"/> - not
        /// this mode's concern - so the state is always about the mode that is selected.
        /// </summary>
        public static WriteState StateOf(string code, string className, IEnumerable<string> remarks, IEnumerable<string> attributes)
        {
            string updated;
            try
            {
                updated = Splice(code, className, remarks, attributes);
            }
            catch (InvalidOperationException)
            {
                return WriteState.Missing;
            }

            if (string.Equals(code, updated, StringComparison.Ordinal))
            {
                return WriteState.Current;
            }

            return HasOwnedOutput(code, className, attributes != null) ? WriteState.Stale : WriteState.Missing;
        }

        /// <summary>
        /// Whether the class already carries this tool's output in the given mode: owned
        /// attributes directly above the declaration, or the marked remarks block above those.
        /// </summary>
        private static bool HasOwnedOutput(string code, string className, bool attributeMode)
        {
            var match = ClassDeclaration(className).Match(code);
            if (!match.Success)
            {
                return false;
            }

            var head = code.Substring(0, match.Index);
            while (true)
            {
                var attribute = TrailingAttribute.Match(head);
                if (!attribute.Success)
                {
                    break;
                }

                if (attributeMode && OwnedAttribute.IsMatch(attribute.Value))
                {
                    return true;
                }

                head = head.Substring(0, attribute.Index);
            }

            return !attributeMode && TrailingRemarks.IsMatch(head);
        }

        /// <summary>
        /// Writes the spliced file, leaving a timestamped .bak copy beside it.
        /// Returns false when the file already had exactly these attributes.
        /// </summary>
        public static bool Update(string filePath, string className, IEnumerable<string> remarks, IEnumerable<string> attributes)
        {
            var encoding = DetectEncoding(filePath);
            var original = File.ReadAllText(filePath);
            var updated = Splice(original, className, remarks, attributes);

            if (string.Equals(original, updated, StringComparison.Ordinal))
            {
                return false;
            }

            File.WriteAllText(filePath + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bak", original, encoding);
            File.WriteAllText(filePath, updated, encoding);
            return true;
        }

        private static Encoding DetectEncoding(string path)
        {
            using (var reader = new StreamReader(path, Encoding.UTF8, true))
            {
                reader.Peek();
                return reader.CurrentEncoding;
            }
        }
    }
}
