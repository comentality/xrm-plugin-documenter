using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Xrm.Sdk;
using PluginDocumenter.Logic;
using XrmToolBox.Extensibility;
using Label = System.Windows.Forms.Label;

namespace PluginDocumenter
{
    public partial class PluginDocumenterControl : PluginControlBase
    {
        private List<AssemblyInfo> _assemblies = new List<AssemblyInfo>();
        private List<PluginTypeInfo> _types = new List<PluginTypeInfo>();
        private AssemblyInfo _selectedAssembly;

        private SplitContainer _mainSplit;
        private SplitContainer _leftSplit;
        private Button _btnLoadAssemblies;
        private CheckBox _chkShowMicrosoft;
        private ListView _lvAssemblies;
        private ListView _lvTypes;

        private Panel _toolbar;
        private TextBox _txtFolder;
        private Button _btnBrowse;
        private RadioButton _rbAttributes;
        private RadioButton _rbComment;
        private Button _btnPreview;
        private Button _btnWrite;
        private Button _btnCreateDefinitions;
        private TextBox _txtPreview;

        public PluginDocumenterControl()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            _mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 320,
                FixedPanel = FixedPanel.Panel1
            };

            // ===== LEFT: assemblies (top) + plugin types (bottom) =====
            _leftSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal
            };

            var leftToolbar = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(5) };
            _btnLoadAssemblies = new Button { Text = "Load Assemblies", Location = new Point(5, 5), Width = 150, Height = 26 };
            _btnLoadAssemblies.Click += BtnLoadAssemblies_Click;
            _chkShowMicrosoft = new CheckBox
            {
                Text = "Microsoft's",
                Location = new Point(161, 9),
                Width = 140,
                AutoSize = false
            };
            _chkShowMicrosoft.CheckedChanged += (s, e) => RenderAssemblies();
            leftToolbar.Controls.Add(_btnLoadAssemblies);
            leftToolbar.Controls.Add(_chkShowMicrosoft);

            _lvAssemblies = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                Font = new Font("Segoe UI", 9f)
            };
            _lvAssemblies.Columns.Add("Assembly", 210);
            _lvAssemblies.Columns.Add("Isolation", 80);
            _lvAssemblies.SelectedIndexChanged += LvAssemblies_SelectedIndexChanged;

            _leftSplit.Panel1.Controls.Add(_lvAssemblies);
            _leftSplit.Panel1.Controls.Add(leftToolbar);

            _lvTypes = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = true,
                CheckBoxes = true,
                HideSelection = false,
                Font = new Font("Segoe UI", 9f)
            };
            _lvTypes.Columns.Add("Plugin Class", 210);
            _lvTypes.Columns.Add("Steps", 50);
            _lvTypes.ItemChecked += (s, e) => UpdateButtonState();

            _leftSplit.Panel2.Controls.Add(_lvTypes);
            _mainSplit.Panel1.Controls.Add(_leftSplit);

            // ===== RIGHT: toolbar + preview =====
            _toolbar = new Panel { Dock = DockStyle.Top, Height = 98, Padding = new Padding(5) };

            var lblFolder = new Label { Text = "Source folder:", Location = new Point(5, 10), AutoSize = true };
            _txtFolder = new TextBox { Location = new Point(95, 7), Width = 400 };
            _txtFolder.TextChanged += (s, e) => UpdateButtonState();
            _btnBrowse = new Button { Text = "Browse...", Location = new Point(500, 5), Width = 80, Height = 24 };
            _btnBrowse.Click += BtnBrowse_Click;

            var lblOutput = new Label { Text = "Write:", Location = new Point(5, 40), AutoSize = true };
            _rbAttributes = new RadioButton
            {
                Text = "Xrm Tools attributes",
                Location = new Point(95, 38),
                Width = 150,
                Checked = true
            };
            _rbAttributes.CheckedChanged += (s, e) => OutputModeChanged();
            _rbComment = new RadioButton
            {
                Text = "Readable summary comment",
                Location = new Point(250, 38),
                Width = 190
            };

            _btnPreview = new Button { Text = "Preview Attributes", Location = new Point(5, 64), Width = 130, Height = 26, Enabled = false };
            _btnPreview.Click += BtnPreview_Click;
            _btnWrite = new Button { Text = "Write to Files", Location = new Point(140, 64), Width = 110, Height = 26, Enabled = false };
            _btnWrite.Click += BtnWrite_Click;
            _btnCreateDefinitions = new Button { Text = "Create Attribute Definitions File", Location = new Point(255, 64), Width = 210, Height = 26, Enabled = false };
            _btnCreateDefinitions.Click += BtnCreateDefinitions_Click;

            _toolbar.Controls.AddRange(new Control[]
            {
                lblFolder, _txtFolder, _btnBrowse,
                lblOutput, _rbAttributes, _rbComment,
                _btnPreview, _btnWrite, _btnCreateDefinitions
            });

            _txtPreview = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                ReadOnly = true,
                Font = new Font("Consolas", 9f)
            };

            _mainSplit.Panel2.Controls.Add(_txtPreview);
            _mainSplit.Panel2.Controls.Add(_toolbar);

            Controls.Add(_mainSplit);
            ResumeLayout(false);
        }

        private void BtnLoadAssemblies_Click(object sender, EventArgs e)
        {
            ExecuteMethod(LoadAssemblies);
        }

        private void LoadAssemblies()
        {
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading plugin assemblies...",
                Work = (worker, args) => { args.Result = RegistrationQuery.GetAssemblies(Service); },
                PostWorkCallBack = result =>
                {
                    if (result.Error != null)
                    {
                        ShowErrorDialog(result.Error);
                        return;
                    }

                    _assemblies = (List<AssemblyInfo>)result.Result;
                    _selectedAssembly = null;
                    RenderAssemblies();
                    ClearTypes();
                }
            });
        }

        /// <summary>
        /// Fills the list from what was loaded. An environment carries dozens of Microsoft's own
        /// assemblies and one or two of yours, so they are held back by default, with the count on
        /// the switch that brings them back rather than the list quietly being short.
        /// </summary>
        private void RenderAssemblies()
        {
            var microsoft = _assemblies.Count(a => a.IsMicrosoft);
            _chkShowMicrosoft.Text = microsoft == 0
                ? "Microsoft's"
                : "Microsoft's (" + microsoft + ")";

            _lvAssemblies.BeginUpdate();
            _lvAssemblies.Items.Clear();
            foreach (var assembly in _assemblies.Where(a => _chkShowMicrosoft.Checked || !a.IsMicrosoft))
            {
                var item = new ListViewItem(assembly.Name) { Tag = assembly };
                item.SubItems.Add(assembly.IsolationMode == 2 ? "Sandbox" : "None");
                _lvAssemblies.Items.Add(item);
            }

            // Clearing the list drops the selection with it. Put it back, or say plainly that the
            // assembly being documented is the one the switch just hid.
            var reselected = _lvAssemblies.Items.Cast<ListViewItem>()
                .FirstOrDefault(i => _selectedAssembly != null && ((AssemblyInfo)i.Tag).Id == _selectedAssembly.Id);

            _lvAssemblies.EndUpdate();

            if (reselected != null)
            {
                reselected.Selected = true;
            }
            else if (_selectedAssembly != null)
            {
                _selectedAssembly = null;
                ClearTypes();
            }
        }

        private void LvAssemblies_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_lvAssemblies.SelectedItems.Count == 0)
            {
                return;
            }

            var selected = (AssemblyInfo)_lvAssemblies.SelectedItems[0].Tag;
            if (_selectedAssembly != null && selected.Id == _selectedAssembly.Id)
            {
                // A re-render put the same selection back. Its steps are already on screen.
                return;
            }

            _selectedAssembly = selected;
            var assemblyId = _selectedAssembly.Id;

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading registered steps...",
                Work = (worker, args) => { args.Result = RegistrationQuery.GetPluginTypes(Service, assemblyId); },
                PostWorkCallBack = result =>
                {
                    if (result.Error != null)
                    {
                        ShowErrorDialog(result.Error);
                        return;
                    }

                    _types = (List<PluginTypeInfo>)result.Result;
                    _lvTypes.BeginUpdate();
                    _lvTypes.Items.Clear();
                    foreach (var type in _types)
                    {
                        var item = new ListViewItem(type.ClassName) { Tag = type, Checked = true };
                        item.SubItems.Add(type.Steps.Count.ToString());
                        item.ToolTipText = type.TypeName;
                        _lvTypes.Items.Add(item);
                    }

                    _lvTypes.EndUpdate();
                    UpdateButtonState();
                }
            });
        }

        private void ClearTypes()
        {
            _types = new List<PluginTypeInfo>();
            _lvTypes.Items.Clear();
            _txtPreview.Clear();
            UpdateButtonState();
        }

        private void UpdateButtonState()
        {
            var hasChecked = _lvTypes.CheckedItems.Count > 0;
            var hasFolder = _txtFolder.Text.Trim().Length > 0 && Directory.Exists(_txtFolder.Text.Trim());

            _btnPreview.Enabled = hasChecked;
            _btnWrite.Enabled = hasChecked && hasFolder;
            // A comment needs no attribute definitions to compile against.
            _btnCreateDefinitions.Enabled = hasFolder && _rbAttributes.Checked;
        }

        private void OutputModeChanged()
        {
            _btnPreview.Text = _rbAttributes.Checked ? "Preview Attributes" : "Preview Comment";
            UpdateButtonState();
        }

        /// <summary>
        /// The two outputs are independent in the file: whichever mode is off returns null,
        /// which tells <see cref="CodeFileWriter"/> to leave what is already there alone.
        /// </summary>
        private IEnumerable<string> Remarks(PluginTypeInfo type)
        {
            return _rbComment.Checked ? RemarksEmitter.Emit(type) : null;
        }

        private IEnumerable<string> Attributes(PluginTypeInfo type)
        {
            return _rbAttributes.Checked ? AttributeEmitter.Emit(type) : null;
        }

        private List<PluginTypeInfo> CheckedTypes()
        {
            return _lvTypes.CheckedItems.Cast<ListViewItem>().Select(i => (PluginTypeInfo)i.Tag).ToList();
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog { Description = "Select the folder containing your plugin source files" })
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    _txtFolder.Text = dialog.SelectedPath;
                }
            }
        }

        private void BtnPreview_Click(object sender, EventArgs e)
        {
            var sb = new StringBuilder();
            foreach (var type in CheckedTypes())
            {
                sb.AppendLine("// " + type.TypeName);
                foreach (var line in Remarks(type) ?? Attributes(type))
                {
                    sb.AppendLine(line);
                }

                sb.AppendLine("public partial class " + type.ClassName);
                sb.AppendLine();
            }

            _txtPreview.Text = sb.ToString();
        }

        private void BtnWrite_Click(object sender, EventArgs e)
        {
            var folder = _txtFolder.Text.Trim();
            var types = CheckedTypes();

            var written = new List<string>();
            var unchanged = new List<string>();
            var notFound = new List<string>();
            var ambiguousTypes = new List<string>();
            var failed = new List<string>();

            foreach (var type in types)
            {
                try
                {
                    List<string> ambiguous;
                    var file = CodeFileWriter.FindFile(folder, type.ClassName, out ambiguous);

                    if (ambiguous != null)
                    {
                        ambiguousTypes.Add(type.ClassName + " (" + ambiguous.Count + " files)");
                        continue;
                    }

                    if (file == null)
                    {
                        notFound.Add(type.ClassName);
                        continue;
                    }

                    if (CodeFileWriter.Update(file, type.ClassName, Remarks(type), Attributes(type)))
                    {
                        written.Add(type.ClassName);
                    }
                    else
                    {
                        unchanged.Add(type.ClassName);
                    }
                }
                catch (Exception ex)
                {
                    failed.Add(type.ClassName + ": " + ex.Message);
                }
            }

            var report = new StringBuilder();
            Report(report, "Updated", written);
            Report(report, "Already up to date", unchanged);
            Report(report, "No matching .cs file", notFound);
            Report(report, "Ambiguous, several files declare the class", ambiguousTypes);
            Report(report, "Failed", failed);

            _txtPreview.Text = report.ToString();
            MessageBox.Show(
                written.Count + " file(s) updated, " + unchanged.Count + " unchanged, "
                + (notFound.Count + ambiguousTypes.Count + failed.Count) + " skipped."
                + Environment.NewLine + Environment.NewLine
                + "A timestamped .bak copy was left beside every file that changed.",
                "Write complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void Report(StringBuilder sb, string heading, List<string> items)
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

        private void BtnCreateDefinitions_Click(object sender, EventArgs e)
        {
            var folder = _txtFolder.Text.Trim();
            var target = Path.Combine(folder, AttributeDefinitions.FileName);

            if (File.Exists(target))
            {
                var overwrite = MessageBox.Show(
                    AttributeDefinitions.FileName + " already exists in that folder." + Environment.NewLine
                    + "Overwrite it?",
                    "File exists", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (overwrite != DialogResult.Yes)
                {
                    return;
                }
            }

            try
            {
                File.WriteAllText(target, AttributeDefinitions.Source, new UTF8Encoding(true));
                _txtPreview.Text = AttributeDefinitions.Source;
                MessageBox.Show(
                    "Wrote " + target + Environment.NewLine + Environment.NewLine
                    + "Do not also reference the XrmTools.Meta.Attributes NuGet package in this project. "
                    + "It generates the same types and you will get duplicate type errors.",
                    "Attribute definitions created", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ShowErrorDialog(ex);
            }
        }
    }
}
