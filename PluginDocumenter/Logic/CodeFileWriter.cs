using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PluginDocumenter.Logic
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

        public static Regex ClassDeclaration(string className)
        {
            return new Regex(
                @"(?m)^[ \t]*(?:(?:public|internal|private|protected|abstract|sealed|static|partial|new)[ \t]+)*class[ \t]+"
                + Regex.Escape(className) + @"\b");
        }

        /// <summary>
        /// Finds the single .cs file declaring <paramref name="className"/>. Returns null
        /// when there is no match; sets <paramref name="ambiguous"/> when there are several.
        /// </summary>
        public static string FindFile(string folder, string className, out List<string> ambiguous)
        {
            ambiguous = null;
            var declaration = ClassDeclaration(className);

            var matches = Directory
                .EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
                .Where(f => !IsGenerated(f))
                .Where(f => declaration.IsMatch(ReadAllText(f)))
                .ToList();

            if (matches.Count == 0)
            {
                return null;
            }

            if (matches.Count > 1)
            {
                ambiguous = matches;
                return null;
            }

            return matches[0];
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
        /// Replaces this tool's attributes above the class declaration with
        /// <paramref name="attributes"/>, leaving any other attributes in place.
        /// </summary>
        public static string Splice(string code, string className, IEnumerable<string> attributes)
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

            // Peel every attribute directly above the class, keeping the ones we do not own.
            var kept = new List<string>();
            while (true)
            {
                var attribute = TrailingAttribute.Match(head);
                if (!attribute.Success)
                {
                    break;
                }

                if (!OwnedAttribute.IsMatch(attribute.Value))
                {
                    kept.Insert(0, attribute.Value);
                }

                head = head.Substring(0, attribute.Index);
            }

            var newline = code.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            var sb = new StringBuilder(head);
            foreach (var attribute in kept)
            {
                sb.Append(attribute);
            }

            foreach (var attribute in attributes)
            {
                // Re-indent continuation lines of multi-line attributes to match the class.
                var text = attribute.Replace("\r\n", "\n").Replace("\n", newline + indent);
                sb.Append(indent).Append(text).Append(newline);
            }

            return sb.Append(tail).ToString();
        }

        /// <summary>
        /// Writes the spliced file, leaving a timestamped .bak copy beside it.
        /// Returns false when the file already had exactly these attributes.
        /// </summary>
        public static bool Update(string filePath, string className, IEnumerable<string> attributes)
        {
            var encoding = DetectEncoding(filePath);
            var original = File.ReadAllText(filePath);
            var updated = Splice(original, className, attributes);

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
