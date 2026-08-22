using System;
using System.Collections.Generic;
using System.Linq;

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

    /// <summary>A plugin class sitting in the folder with no step behind it.</summary>
    public class LocalClass
    {
        public string ClassName;
        public string File;

        /// <summary>
        /// Whether a registered plugin type of this name exists and simply has no steps. Two
        /// findings a reader must not be given as one: a class nobody registered is work not
        /// started, and a class registered with no steps is work stopped one move from the end.
        /// </summary>
        public bool Stepless;
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
    /// have no registration. Matching goes through <see cref="SourceIndex.Find"/>, the same
    /// call a write uses, so a mark in the list is a promise about what a write would do.
    /// </summary>
    public static class SourceScanner
    {
        public static FolderScan Scan(string folder, IList<PluginTypeInfo> registered,
            ICollection<string> allRegisteredClassNames, ICollection<string> steplessClassNames = null)
        {
            return Scan(SourceIndex.Build(folder), registered, allRegisteredClassNames, steplessClassNames);
        }

        /// <summary>
        /// The scan over a folder already read. Nothing in the tool needs this yet; it is
        /// what keeps the reading and the deciding separable, and it is what the perf
        /// harness times the two halves of.
        /// </summary>
        public static FolderScan Scan(SourceIndex index, IList<PluginTypeInfo> registered,
            ICollection<string> allRegisteredClassNames, ICollection<string> steplessClassNames = null)
        {
            var scan = new FolderScan { Folder = index.Folder };

            foreach (var type in registered)
            {
                List<string> ambiguous;
                var file = index.Find(type.ClassName, type.Namespace, out ambiguous);

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
                    match.File = file.Path;
                    match.Code = file.Text;
                }

                scan.Matches[type.Id] = match;
            }

            scan.Unregistered.AddRange(FindUnregistered(index, allRegisteredClassNames, steplessClassNames));
            return scan;
        }

        /// <summary>
        /// Plugin classes the folder declares that no fetched registration names. Membership
        /// spreads from IPlugin through base lists - a plugin project's classes mostly derive
        /// from a shared base rather than implementing the interface themselves - and abstract
        /// classes are left out at the end: a base nobody can register is not a finding.
        ///
        /// The spread is a walk out from IPlugin rather than repeated passes over every class:
        /// each name admits the classes naming it, and those names go on the queue. A pass per
        /// name over a repository's worth of classes is what this used to cost.
        /// </summary>
        private static IEnumerable<LocalClass> FindUnregistered(SourceIndex index,
            ICollection<string> registeredClassNames, ICollection<string> steplessClassNames)
        {
            // Which classes name a given identifier in their base list.
            var derived = new Dictionary<string, List<DeclaredClass>>(StringComparer.Ordinal);
            foreach (var declared in index.Declarations)
            {
                foreach (var baseName in declared.BaseNames)
                {
                    List<DeclaredClass> children;
                    if (!derived.TryGetValue(baseName, out children))
                    {
                        children = new List<DeclaredClass>();
                        derived.Add(baseName, children);
                    }

                    children.Add(declared);
                }
            }

            // Names, not declarations: a plugin name admits everything declared under it, so
            // one half of a partial class carrying the base list speaks for both halves.
            var plugins = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            queue.Enqueue("IPlugin");

            while (queue.Count > 0)
            {
                List<DeclaredClass> children;
                if (!derived.TryGetValue(queue.Dequeue(), out children))
                {
                    continue;
                }

                foreach (var child in children)
                {
                    if (plugins.Add(child.Name))
                    {
                        queue.Enqueue(child.Name);
                    }
                }
            }

            return index.Declarations
                .Where(d => plugins.Contains(d.Name)
                            && !d.IsAbstract
                            && !registeredClassNames.Contains(d.Name))
                .GroupBy(d => d.Name, StringComparer.Ordinal)
                .Select(g => new LocalClass
                {
                    ClassName = g.Key,
                    File = g.First().File.Path,
                    Stepless = steplessClassNames != null && steplessClassNames.Contains(g.Key)
                })
                .OrderBy(c => c.ClassName, StringComparer.OrdinalIgnoreCase);
        }
    }
}
