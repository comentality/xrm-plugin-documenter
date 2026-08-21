using System;
using System.Collections.Generic;
using System.Text;

namespace PluginStepCodegen.Logic
{
    /// <summary>
    /// What one press of Write did, in the words the report uses. Every class that was
    /// asked for lands in exactly one of these lists, so the tallies add up to the batch.
    /// </summary>
    public class WriteReport
    {
        public List<string> Written = new List<string>();
        public List<string> Unchanged = new List<string>();
        public List<string> NotFound = new List<string>();
        public List<string> Ambiguous = new List<string>();
        public List<string> Failed = new List<string>();

        public int Skipped
        {
            get { return NotFound.Count + Ambiguous.Count + Failed.Count; }
        }

        /// <summary>The report as the preview pane shows it, headed section by section.</summary>
        public string Format()
        {
            var sb = new StringBuilder();
            Section(sb, "Updated", Written);
            Section(sb, "Already up to date", Unchanged);
            Section(sb, "No matching .cs file", NotFound);
            Section(sb, "Ambiguous, several files declare the class", Ambiguous);
            Section(sb, "Failed", Failed);
            return sb.ToString();
        }

        private static void Section(StringBuilder sb, string heading, List<string> items)
        {
            if (items.Count == 0)
            {
                return;
            }

            sb.AppendLine("// " + heading + " (" + items.Count + ")");
            foreach (var item in items)
            {
                sb.AppendLine("//   " + item);
            }

            sb.AppendLine();
        }
    }

    /// <summary>
    /// What one class is to be given. Both are asked for together and either may be null,
    /// which is how the output mode is said all the way down to the splice: null means "not
    /// this mode's concern", and leaves whatever the file already has alone.
    /// </summary>
    public class ClassOutput
    {
        public IEnumerable<string> Remarks;
        public IEnumerable<string> Attributes;
    }

    /// <summary>
    /// One press of Write: hold every checked class against the folder and splice this
    /// tool's output into the file that declares it.
    ///
    /// It lives here rather than in the button handler because the batch is the unit that
    /// has to be fast and the unit worth testing: the folder is read once for the whole
    /// run, and a headless run of it is the same code the button presses.
    /// </summary>
    public static class SourceWriter
    {
        /// <summary>
        /// <paramref name="output"/> is asked per class rather than passed in, because what
        /// a class is given depends on the output mode and that is the caller's to decide.
        /// </summary>
        public static WriteReport Write(
            string folder,
            IEnumerable<PluginTypeInfo> types,
            bool writeAmbiguous,
            Func<PluginTypeInfo, ClassOutput> output)
        {
            return Write(SourceIndex.Build(folder), types, writeAmbiguous, output);
        }

        /// <summary>
        /// The write over a folder already read - by the scan behind the list, say, which
        /// has just answered the same question for the marks the button was pressed on.
        /// </summary>
        public static WriteReport Write(
            SourceIndex index,
            IEnumerable<PluginTypeInfo> types,
            bool writeAmbiguous,
            Func<PluginTypeInfo, ClassOutput> output)
        {
            var report = new WriteReport();
            var folder = index.Folder;

            foreach (var type in types)
            {
                try
                {
                    List<string> ambiguous;
                    var match = index.Find(type.ClassName, type.Namespace, out ambiguous);
                    var file = match == null ? null : match.Path;

                    if (ambiguous != null)
                    {
                        if (!writeAmbiguous)
                        {
                            report.Ambiguous.Add(type.ClassName + " (" + ambiguous.Count + " files)");
                            continue;
                        }

                        // Asked for by name: every file declaring the class gets the same output.
                        // The splice only ever replaces this tool's own block, so the partial-class
                        // case this exists for ends up documented on both halves.
                        var given = output(type);
                        foreach (var candidate in ambiguous)
                        {
                            var label = type.ClassName + " (" + Relative(folder, candidate) + ")";
                            Tally(report, label, CodeFileWriter.Update(candidate, type.ClassName, given.Remarks, given.Attributes));
                        }

                        continue;
                    }

                    if (file == null)
                    {
                        report.NotFound.Add(type.ClassName);
                        continue;
                    }

                    var write = output(type);
                    Tally(report, type.ClassName, CodeFileWriter.Update(file, type.ClassName, write.Remarks, write.Attributes));
                }
                catch (Exception ex)
                {
                    report.Failed.Add(type.ClassName + ": " + ex.Message);
                }
            }

            return report;
        }

        private static void Tally(WriteReport report, string label, bool changed)
        {
            (changed ? report.Written : report.Unchanged).Add(label);
        }

        private static string Relative(string folder, string path)
        {
            return path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(folder.Length).TrimStart('\\', '/')
                : path;
        }
    }
}
