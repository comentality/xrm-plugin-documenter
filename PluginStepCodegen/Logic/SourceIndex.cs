using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PluginStepCodegen.Logic
{
    /// <summary>A class declaration found in a file, with what it says about itself.</summary>
    public class DeclaredClass
    {
        public string Name;
        public bool IsAbstract;

        /// <summary>
        /// The identifiers in the base list, split out once. Asking whether a class derives
        /// from a name is a set lookup here rather than a regex over the text, which matters
        /// because the question is asked for every known plugin name against every class.
        /// </summary>
        public HashSet<string> BaseNames = new HashSet<string>(StringComparer.Ordinal);

        public SourceFile File;
    }

    /// <summary>One .cs file, read once and parsed once.</summary>
    public class SourceFile
    {
        public string Path;
        public string Text;
        public List<DeclaredClass> Classes = new List<DeclaredClass>();

        /// <summary>Every namespace the file declares, for settling which file a class is in.</summary>
        public HashSet<string> Namespaces = new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// The source folder, read once: every file's text, what each declares, and a name to
    /// file map to look classes up in.
    ///
    /// It exists because the folder is the expensive thing and every question about it is
    /// cheap once it has been read. Asking file by file - open the folder, read it all,
    /// regex it for this one class, and again for the next class - is what made a scan of a
    /// few hundred files take seconds: the cost was never the disk, it was reading the same
    /// text once per registered class. One index answers the whole batch.
    /// </summary>
    public class SourceIndex
    {
        /// <summary>
        /// A class declaration with its modifiers and base list, read from the start of its
        /// own line. The base list runs to the opening brace, which overshoots into constraint
        /// clauses on a generic class; for deciding whether IPlugin or a known base name
        /// appears in it, overshooting is safe.
        ///
        /// It is anchored rather than searched, because it is only ever run at a line that
        /// already turned out to hold the keyword - see <see cref="DeclaringLines"/>.
        /// </summary>
        private static readonly Regex Declaration = new Regex(
            @"^[ \t]*(?<mods>(?:(?:public|internal|private|protected|abstract|sealed|static|partial|new)[ \t]+)*)class[ \t]+(?<name>@?[_\w]\w*)(?:\s*<[^>{\n]*>)?\s*(?::\s*(?<bases>[^{]*))?",
            RegexOptions.Compiled);

        /// <summary>Block or file scoped; a nested declaration is deliberately not recognised.</summary>
        private static readonly Regex NamespaceDeclaration = new Regex(
            @"^[ \t]*namespace[ \t]+(?<name>[\w.]+)\s*[{;]",
            RegexOptions.Compiled);

        private static readonly Regex Identifier = new Regex(@"\w+", RegexOptions.Compiled);

        private readonly Dictionary<string, List<SourceFile>> _byClassName =
            new Dictionary<string, List<SourceFile>>(StringComparer.Ordinal);

        public string Folder { get; private set; }

        public List<SourceFile> Files { get; private set; }

        /// <summary>Every class declared anywhere under the folder, in file order.</summary>
        public IEnumerable<DeclaredClass> Declarations
        {
            get { return Files.SelectMany(f => f.Classes); }
        }

        /// <summary>Reads and parses the whole folder. The one expensive call.</summary>
        public static SourceIndex Build(string folder)
        {
            var index = new SourceIndex
            {
                Folder = folder,
                Files = new List<SourceFile>()
            };

            foreach (var path in CodeFileWriter.EnumerateSources(folder))
            {
                var text = ReadAllText(path);
                if (text.Length == 0)
                {
                    continue;
                }

                index.Files.Add(Parse(path, text));
            }

            foreach (var file in index.Files)
            {
                foreach (var declared in file.Classes)
                {
                    List<SourceFile> declaring;
                    if (!index._byClassName.TryGetValue(declared.Name, out declaring))
                    {
                        declaring = new List<SourceFile>();
                        index._byClassName.Add(declared.Name, declaring);
                    }

                    // A file declaring the same class twice - a nested class of the same name,
                    // say - is still one candidate file, not two.
                    if (declaring.Count == 0 || declaring[declaring.Count - 1] != file)
                    {
                        declaring.Add(file);
                    }
                }
            }

            return index;
        }

        private static SourceFile Parse(string path, string text)
        {
            var file = new SourceFile { Path = path, Text = text };

            foreach (var m in DeclaringLines(text, "namespace", NamespaceDeclaration, BraceOrSemicolon))
            {
                file.Namespaces.Add(m.Groups["name"].Value);
            }

            foreach (var m in DeclaringLines(text, "class", Declaration, Brace))
            {
                var declared = new DeclaredClass
                {
                    Name = m.Groups["name"].Value,
                    IsAbstract = m.Groups["mods"].Value.Contains("abstract"),
                    File = file
                };

                // Word by word, which is the same test as looking for \bName\b in the text
                // and is done once here instead of once per name asked about.
                foreach (Match token in Identifier.Matches(m.Groups["bases"].Value))
                {
                    declared.BaseNames.Add(token.Value);
                }

                file.Classes.Add(declared);
            }

            return file;
        }

        /// <summary>
        /// Every line that declares something, found by looking for the keyword and then
        /// reading the line it sits on.
        ///
        /// The obvious spelling - one line-anchored regex run over the whole file - is what
        /// most of a scan used to be spent in: a pattern beginning with an optional run of
        /// modifiers has nothing the engine can search for, so it is tried at every character
        /// of every file. Finding the keyword first is a string search, which is the thing
        /// the platform is fastest at, and the regex then runs a few times per file on a
        /// region that starts where a declaration would have to start and ends at the brace.
        /// The pattern is unchanged, so what counts as a declaration is unchanged with it.
        /// </summary>
        private static IEnumerable<Match> DeclaringLines(string text, string keyword, Regex shape, char[] terminators)
        {
            var lastLine = -1;
            var at = 0;

            while (at < text.Length)
            {
                at = text.IndexOf(keyword, at, StringComparison.Ordinal);
                if (at < 0)
                {
                    yield break;
                }

                var after = at + keyword.Length;
                var keywordAt = at;
                at = after;

                // A keyword only counts as one when it is a word of its own, and only the
                // first one on a line can be at the start of it.
                if (!IsWord(text, keywordAt - 1) && !IsWord(text, after))
                {
                    var line = keywordAt == 0 ? 0 : text.LastIndexOf('\n', keywordAt - 1) + 1;
                    if (line != lastLine)
                    {
                        // Bounded at whatever ends the declaration - a class always opens a
                        // body, a namespace may be file scoped - so the anchored pattern has
                        // a short region to fail in when the line turns out to be something
                        // else that mentions the word.
                        var end = text.IndexOfAny(terminators, after);
                        var region = end < 0 ? text.Substring(line) : text.Substring(line, end - line + 1);

                        var match = shape.Match(region);
                        if (match.Success)
                        {
                            lastLine = line;
                            yield return match;
                        }
                    }
                }
            }
        }

        private static readonly char[] Brace = { '{' };
        private static readonly char[] BraceOrSemicolon = { '{', ';' };

        private static bool IsWord(string text, int index)
        {
            if (index < 0 || index >= text.Length)
            {
                return false;
            }

            var c = text[index];
            return c == '_' || char.IsLetterOrDigit(c);
        }

        /// <summary>
        /// The single file declaring <paramref name="className"/>. Returns null when there is
        /// no match; sets <paramref name="ambiguous"/> when there are several. When several
        /// files declare the short name, the registered namespace breaks the tie if exactly
        /// one of them declares it.
        /// </summary>
        public SourceFile Find(string className, string namespaceName, out List<string> ambiguous)
        {
            ambiguous = null;

            List<SourceFile> matches;
            if (!_byClassName.TryGetValue(className ?? string.Empty, out matches) || matches.Count == 0)
            {
                return null;
            }

            if (matches.Count == 1)
            {
                return matches[0];
            }

            // Only exactly one survivor settles a tie. Zero means the namespace was not
            // found as written - possibly declared nested, possibly the registration is
            // stale - and two or more is a partial class spanning files, and in either
            // case picking a file would mean writing a registration into the wrong class.
            if (!string.IsNullOrEmpty(namespaceName))
            {
                var narrowed = matches.Where(f => f.Namespaces.Contains(namespaceName)).ToList();
                if (narrowed.Count == 1)
                {
                    return narrowed[0];
                }
            }

            ambiguous = matches.Select(f => f.Path).ToList();
            return null;
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
    }
}
