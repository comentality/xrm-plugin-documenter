using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PluginStepCodegen.Logic;

namespace PluginStepCodegen.PerfHarness
{
    /// <summary>
    /// Generates a plugin repository of a given size and times what the tool does to it, so
    /// "the scan takes five seconds" is a number somebody can reproduce rather than a report.
    ///
    ///   perfharness.exe &lt;tree root&gt; &lt;source files&gt; &lt;registered classes&gt;
    ///
    /// It times the real entry points - <see cref="SourceScanner.Scan"/>, the per-class
    /// <see cref="CodeFileWriter.StateOf"/> the list render runs, and <see cref="SourceWriter.Write"/> -
    /// so an optimisation that only helps a copy of the code shows here as no gain at all.
    ///
    /// Every phase prints one tab separated line and perf.ps1 judges them. The judging lives
    /// there because the interesting question is a ratio: reading the tree once is the floor
    /// nothing can beat, and a phase that costs many times the floor is one that walks the
    /// folder once per class rather than once per press.
    ///
    /// Run it through perf.ps1 rather than directly.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("usage: perfharness.exe <tree root> <source files> <registered classes>");
                return 2;
            }

            var root = Path.GetFullPath(args[0]);
            var sources = int.Parse(args[1], CultureInfo.InvariantCulture);
            var classes = int.Parse(args[2], CultureInfo.InvariantCulture);

            try
            {
                var tree = Tree.Generate(root, sources, classes);
                Measure(tree);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static void Measure(Tree tree)
        {
            Line("tree", tree.GenerateMs,
                tree.SourceFiles + " source files, " + tree.BuildFiles + " under bin and obj, "
                + (tree.Bytes / 1024) + " KB, " + tree.Types.Count + " registered classes");

            // The floor: every source file opened and read once, which is what any honest
            // answer about the folder costs. Everything below is quoted against this.
            var read = 0L;
            var count = 0;
            var floor = Best(3, () =>
            {
                read = 0;
                count = 0;
                foreach (var file in CodeFileWriter.EnumerateSources(tree.Root))
                {
                    read += File.ReadAllText(file).Length;
                    count++;
                }
            });
            Line("floor", floor, count + " files, " + (read / 1024) + " KB read");

            var enumerate = Best(3, () => CodeFileWriter.EnumerateSources(tree.Root).Count());
            Line("enumerate", enumerate, "walking the tree, nothing read");

            // Reading and parsing the folder, which is what a scan and a write both start
            // with and where the difference between the two halves of a scan shows.
            SourceIndex index = null;
            var indexMs = Best(3, () => index = SourceIndex.Build(tree.Root));
            Line("index", indexMs, index.Files.Count + " files parsed, " + index.Declarations.Count() + " classes declared");

            FolderScan scan = null;
            var scanMs = Best(3, () => scan = SourceScanner.Scan(tree.Root, tree.Types, tree.RegisteredNames));
            Line("scan", scanMs,
                scan.Matches.Values.Count(m => m.Kind == MatchKind.Found) + " matched, "
                + scan.Matches.Values.Count(m => m.Kind == MatchKind.NotFound) + " not found, "
                + scan.Unregistered.Count + " unregistered");

            // What the list render costs on the UI thread, once per scan and again on every
            // mode switch: the same splice a write would do, per class, for the glyph.
            var renderMs = Best(3, () =>
            {
                foreach (var type in tree.Types)
                {
                    ClassMatch match;
                    if (scan.Matches.TryGetValue(type.Id, out match) && match.Kind == MatchKind.Found)
                    {
                        CodeFileWriter.StateOf(match.Code, type.ClassName, null, AttributeEmitter.Emit(type));
                        CodeFileWriter.StateOf(match.Code, type.ClassName, RemarksEmitter.Emit(type, null), null);
                    }
                }
            });
            Line("render", renderMs, "StateOf per class, both output modes");

            // The deciding half on its own, over a folder already read: what the batch costs
            // once the reading is paid for. A lookup that walks the folder again shows up
            // here as the whole read per class.
            var lookupMs = Best(3, () =>
            {
                foreach (var type in tree.Types)
                {
                    List<string> ambiguous;
                    index.Find(type.ClassName, type.Namespace, out ambiguous);
                }
            });
            Line("lookup", lookupMs, "which file declares each class, folder already read");

            // Mutating from here on, so each of these runs exactly once and in this order:
            // the first write changes every file, the second finds them all up to date.
            WriteReport first = null;
            var writeMs = Once(() => first = tree.Write());
            Line("write", writeMs, first.Written.Count + " written, " + first.Skipped + " skipped");

            WriteReport again = null;
            var rewriteMs = Once(() => again = tree.Write());
            Line("rewrite", rewriteMs, again.Unchanged.Count + " already up to date");

            // The scan the tool runs itself the moment a write finishes, over a tree whose
            // files have all just changed underneath the OS cache.
            var afterMs = Once(() => SourceScanner.Scan(tree.Root, tree.Types, tree.RegisteredNames));
            Line("rescan", afterMs, "the scan a finished write starts");
        }

        /// <summary>
        /// The best of several runs, not the mean: a run that lost the CPU to something else
        /// on the machine says nothing about the code, and only the floor is being cheated by
        /// taking the fastest - which is exactly the comparison every other phase wants.
        /// </summary>
        private static double Best(int runs, Action action)
        {
            var best = double.MaxValue;
            for (var i = 0; i < runs; i++)
            {
                var sw = Stopwatch.StartNew();
                action();
                sw.Stop();
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
            }

            return best;
        }

        private static double Once(Action action)
        {
            var sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }

        private static void Line(string phase, double ms, string note)
        {
            Console.WriteLine(string.Join("\t", phase, ms.ToString("F1", CultureInfo.InvariantCulture), note));
        }
    }

    /// <summary>
    /// A generated plugin repository: several projects, a shared base class, plugins spread
    /// through folders, ordinary code around them and a build's worth of output to walk past.
    /// The shape matters more than the contents - a folder of one-line files would make every
    /// phase look free.
    /// </summary>
    internal class Tree
    {
        public string Root;
        public double GenerateMs;
        public int SourceFiles;
        public int BuildFiles;
        public long Bytes;
        public List<PluginTypeInfo> Types = new List<PluginTypeInfo>();
        public HashSet<string> RegisteredNames = new HashSet<string>();

        private static readonly string[] Areas =
        {
            "Accounts", "Contacts", "Leads", "Opportunities", "Orders", "Quotes",
            "Cases", "Activities", "Integration", "Security", "Pricing", "Shared"
        };

        private static readonly string[] Projects =
        {
            "Contoso.Plugins", "Contoso.Integration.Plugins", "Contoso.Core", "Contoso.Model"
        };

        public static Tree Generate(string root, int sources, int classes)
        {
            var tree = new Tree { Root = root };
            var sw = Stopwatch.StartNew();

            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }

            Directory.CreateDirectory(root);

            // The base every generated plugin derives from. Nothing registers it, and it is
            // abstract, so it is also the check that an abstract base is not reported as an
            // unregistered plugin.
            tree.AddSource(@"Contoso.Plugins\Shared\PluginBase.cs", Base("Contoso.Plugins.Shared"));

            // A tenth of the plugin classes are deliberately not registered, which is the
            // normal state of a repository mid-change and the input the "not registered"
            // list is computed from.
            var plugins = classes + Math.Max(1, classes / 10);
            for (var i = 0; i < plugins; i++)
            {
                var area = Areas[i % Areas.Length];
                var name = area + "Handler" + i.ToString("D3", CultureInfo.InvariantCulture);
                var ns = "Contoso.Plugins." + area;
                tree.AddSource("Contoso.Plugins\\" + area + "\\" + name + ".cs", Plugin(ns, name, i));

                if (i < classes)
                {
                    tree.Register(ns + "." + name);
                }
            }

            // Everything else in the repository: the code the plugins call, which the scan
            // has to read to find out it declares nothing it is looking for.
            var filler = Math.Max(0, sources - (plugins + 1));
            for (var i = 0; i < filler; i++)
            {
                var project = Projects[i % Projects.Length];
                var area = Areas[(i / Projects.Length) % Areas.Length];
                var name = area + "Service" + i.ToString("D4", CultureInfo.InvariantCulture);
                tree.AddSource(project + "\\" + area + "\\" + name + ".cs", Filler(project + "." + area, name, i));
            }

            // A build's output, which the tool skips by path. It is generated because walking
            // past it is part of what a scan of a working copy costs.
            var build = Math.Max(1, sources / 4);
            for (var i = 0; i < build; i++)
            {
                var project = Projects[i % Projects.Length];
                var folder = i % 2 == 0 ? "obj\\Debug\\net48" : "bin\\Debug\\net48";
                var name = "Generated" + i.ToString("D4", CultureInfo.InvariantCulture);
                tree.AddBuildOutput(project + "\\" + folder + "\\" + name + ".g.cs",
                    Filler(project + ".Generated", name, i));
            }

            sw.Stop();
            tree.GenerateMs = sw.Elapsed.TotalMilliseconds;
            return tree;
        }

        /// <summary>One press of Write over the whole tree, in attribute mode.</summary>
        public WriteReport Write()
        {
            return SourceWriter.Write(Root, Types, false,
                t => new ClassOutput { Attributes = AttributeEmitter.Emit(t) });
        }

        private void Register(string typeName)
        {
            var type = new PluginTypeInfo
            {
                Id = Guid.NewGuid(),
                AssemblyId = Guid.Empty,
                TypeName = typeName,
                FriendlyName = typeName
            };

            // Two steps and an image: enough that the emitted block is several lines and the
            // splice has something to compare, which is what the render phase measures.
            type.Steps.Add(Step("Create", "account", 40, 0, null));
            type.Steps.Add(Step("Update", "account", 20, 1, "name,telephone1,revenue"));
            type.Steps[0].Images.Add(new PluginImageInfo
            {
                ImageType = 1,
                EntityAlias = "PostImage",
                Name = "PostImage",
                Attributes = "accountid,name,ownerid"
            });

            Types.Add(type);
            RegisteredNames.Add(type.ClassName);
        }

        private static PluginStepInfo Step(string message, string entity, int stage, int mode, string filter)
        {
            return new PluginStepInfo
            {
                Id = Guid.NewGuid(),
                MessageName = message,
                PrimaryEntityName = entity,
                Stage = stage,
                Mode = mode,
                Rank = 1,
                FilteringAttributes = filter,
                Name = "Contoso: " + message + " of " + entity
            };
        }

        private void AddSource(string relative, string content)
        {
            SourceFiles++;
            Bytes += content.Length;
            Save(relative, content);
        }

        private void AddBuildOutput(string relative, string content)
        {
            BuildFiles++;
            Save(relative, content);
        }

        private void Save(string relative, string content)
        {
            var path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
        }

        private static string Base(string ns)
        {
            return "using System;\r\nusing Microsoft.Xrm.Sdk;\r\n\r\n"
                   + "namespace " + ns + "\r\n{\r\n"
                   + "    public abstract class PluginBase : IPlugin\r\n    {\r\n"
                   + "        public void Execute(IServiceProvider serviceProvider)\r\n        {\r\n"
                   + "            Run(serviceProvider.GetService(typeof(IPluginExecutionContext)) as IPluginExecutionContext);\r\n"
                   + "        }\r\n\r\n"
                   + "        protected abstract void Run(IPluginExecutionContext context);\r\n"
                   + "    }\r\n}\r\n";
        }

        /// <summary>
        /// A plugin as somebody writes them: a summary the tool must not disturb, a base class
        /// rather than the interface, and enough body that the file is a realistic few KB.
        /// </summary>
        private static string Plugin(string ns, string name, int seed)
        {
            var sb = new StringBuilder();
            sb.Append("using System;\r\nusing System.Linq;\r\nusing Microsoft.Xrm.Sdk;\r\n");
            sb.Append("using Contoso.Plugins.Shared;\r\n\r\n");
            sb.Append("namespace ").Append(ns).Append("\r\n{\r\n");
            sb.Append("    /// <summary>Keeps ").Append(name).Append(" in step with the record.</summary>\r\n");
            sb.Append("    public class ").Append(name).Append(" : PluginBase\r\n    {\r\n");
            sb.Append("        protected override void Run(IPluginExecutionContext context)\r\n        {\r\n");
            sb.Append("            var target = context.InputParameters[\"Target\"] as Entity;\r\n");
            sb.Append("            if (target == null)\r\n            {\r\n                return;\r\n            }\r\n\r\n");
            Body(sb, seed, 3, "            ");
            sb.Append("        }\r\n\r\n");
            Methods(sb, seed, 4);
            sb.Append("    }\r\n}\r\n");
            return sb.ToString();
        }

        /// <summary>
        /// Ordinary code: several classes in the file, no plugin among them, and a name the
        /// registrations never mention. Most of a repository looks like this.
        /// </summary>
        private static string Filler(string ns, string name, int seed)
        {
            var sb = new StringBuilder();
            sb.Append("using System;\r\nusing System.Collections.Generic;\r\nusing System.Linq;\r\n\r\n");
            sb.Append("namespace ").Append(ns).Append("\r\n{\r\n");

            for (var c = 0; c < 3; c++)
            {
                sb.Append("    public class ").Append(name).Append(c == 0 ? string.Empty : c.ToString(CultureInfo.InvariantCulture));
                sb.Append(c == 0 ? string.Empty : " : IDisposable").Append("\r\n    {\r\n");
                Methods(sb, seed + c, 5);
                if (c != 0)
                {
                    sb.Append("        public void Dispose()\r\n        {\r\n        }\r\n");
                }

                sb.Append("    }\r\n\r\n");
            }

            sb.Append("}\r\n");
            return sb.ToString();
        }

        private static void Methods(StringBuilder sb, int seed, int count)
        {
            for (var m = 0; m < count; m++)
            {
                sb.Append("        private int Step").Append(m).Append("(int value)\r\n        {\r\n");
                Body(sb, seed + m, 4, "            ");
                sb.Append("            return value;\r\n        }\r\n\r\n");
            }
        }

        private static void Body(StringBuilder sb, int seed, int lines, string indent)
        {
            for (var i = 0; i < lines; i++)
            {
                sb.Append(indent).Append("var candidate").Append(i).Append(" = (")
                  .Append(seed + i).Append(" * 31 + ").Append(i)
                  .Append(") % 97; // a line of the kind a real file is mostly made of\r\n");
            }
        }
    }
}
