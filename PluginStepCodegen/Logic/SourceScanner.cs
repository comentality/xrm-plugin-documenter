using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PluginStepCodegen.Logic
{
    /// <summary>How a registered class fared against the source folder.</summary>
    public enum MatchKind
    {
        Found,
        NotFound,
        Ambiguous
    }

    /// <summary>One registered class held against the folder.</summary>
    public class ClassMatch
    {
        public Guid TypeId;
        public MatchKind Kind;
        /// <summary>Full path of the one file that declares the class, when <see cref="MatchKind.Found"/>.</summary>
        public string File;
        /// <summary>That file's text, kept so staleness can be recomputed on a mode switch without re-reading.</summary>
        public string Code;
        /// <summary>Every declaring file, when <see cref="MatchKind.Ambiguous"/>.</summary>
        public List<string> Candidates;
    }

    /// <summary>A plugin class sitting in the folder with no registration behind it.</summary>
    public class LocalClass
    {
        public string ClassName;
        public string File;
    }

    /// <summary>Everything one look at the folder produced.</summary>
    public class FolderScan
    {
        public string Folder;
        public Dictionary<Guid, ClassMatch> Matches = new Dictionary<Guid, ClassMatch>();
        public List<LocalClass> Unregistered = new List<LocalClass>();
    }

    /// <summary>
    /// Reads the source folder once and answers both directions of the question the two
    /// lists ask: which registered classes have a file here, and which plugin classes here
    /// have no registration. Matching goes through <see cref="CodeFileWriter.FindInFiles"/>,
    /// so a mark in the list is a promise about what a write would do.
    /// </summary>
    public static class SourceScanner
    {
        /// <summary>
        /// A class declaration with its modifiers and base list. The base list runs to the
        /// opening brace, which overshoots into constraint clauses on a generic class; for
        /// deciding whether IPlugin or a known base name appears in it, overshooting is safe.
        /// </summary>
        private static readonly Regex Declaration = new Regex(
            @"(?m)^[ \t]*(?<mods>(?:(?:public|internal|private|protected|abstract|sealed|static|partial|new)[ \t]+)*)class[ \t]+(?<name>[A-Za-z_]\w*)(?:\s*<[^>{\n]*>)?\s*(?::\s*(?<bases>[^{]*))?",
            RegexOptions.Compiled);

        public static FolderScan Scan(string folder, IList<PluginTypeInfo> registered, ICollection<string> allRegisteredClassNames)
        {
            var files = CodeFileWriter.EnumerateSources(folder)
                .Select(f => new KeyValuePair<string, string>(f, ReadAllText(f)))
                .Where(f => f.Value.Length > 0)
                .ToList();

            var scan = new FolderScan { Folder = folder };

            foreach (var type in registered)
            {
                List<string> ambiguous;
                var file = CodeFileWriter.FindInFiles(files, type.ClassName, type.Namespace, out ambiguous);

                var match = new ClassMatch { TypeId = type.Id };
                if (ambiguous != null)
                {
                    match.Kind = MatchKind.Ambiguous;
                    match.Candidates = ambiguous;
                }
                else if (file == null)
                {
                    match.Kind = MatchKind.NotFound;
                }
                else
                {
                    match.Kind = MatchKind.Found;
                    match.File = file;
                    match.Code = files.First(f => f.Key == file).Value;
                }

                scan.Matches[type.Id] = match;
            }

            FindUnregistered(files, allRegisteredClassNames, scan.Unregistered);
            return scan;
        }

        /// <summary>
        /// Plugin classes the folder declares that no fetched registration names. Membership
        /// spreads from IPlugin through base lists - a plugin project's classes mostly derive
        /// from a shared base rather than implementing the interface themselves - and abstract
        /// classes are left out at the end: a base nobody can register is not a finding.
        /// </summary>
        private static void FindUnregistered(
            List<KeyValuePair<string, string>> files,
            ICollection<string> registeredClassNames,
            List<LocalClass> results)
        {
            var declared = new List<Declared>();
            foreach (var file in files)
            {
                foreach (Match m in Declaration.Matches(file.Value))
                {
                    declared.Add(new Declared
                    {
                        Name = m.Groups["name"].Value,
                        Bases = m.Groups["bases"].Value,
                        IsAbstract = m.Groups["mods"].Value.Contains("abstract"),
                        File = file.Key
                    });
                }
            }

            // Names spread to a fixpoint: seed with everything whose base list says IPlugin,
            // then admit anything deriving from an admitted name, until a pass adds nothing.
            var plugins = new HashSet<string>(
                declared.Where(d => Regex.IsMatch(d.Bases, @"\bIPlugin\b")).Select(d => d.Name));

            bool grew;
            do
            {
                grew = false;
                foreach (var d in declared)
                {
                    if (plugins.Contains(d.Name))
                    {
                        continue;
                    }

                    if (plugins.Any(p => Regex.IsMatch(d.Bases, @"\b" + Regex.Escape(p) + @"\b")))
                    {
                        plugins.Add(d.Name);
                        grew = true;
                    }
                }
            }
            while (grew);

            results.AddRange(declared
                .Where(d => plugins.Contains(d.Name)
                            && !d.IsAbstract
                            && !registeredClassNames.Contains(d.Name))
                .GroupBy(d => d.Name)
                .Select(g => new LocalClass { ClassName = g.Key, File = g.First().File })
                .OrderBy(c => c.ClassName, StringComparer.OrdinalIgnoreCase));
        }

        private class Declared
        {
            public string Name;
            public string Bases;
            public bool IsAbstract;
            public string File;
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
