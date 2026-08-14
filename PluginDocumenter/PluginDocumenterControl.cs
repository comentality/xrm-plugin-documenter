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

        /// <summary>Types already fetched, keyed by assembly, so re-checking one costs nothing.</summary>
        private readonly Dictionary<Guid, List<PluginTypeInfo>> _typesByAssembly = new Dictionary<Guid, List<PluginTypeInfo>>();

        /// <summary>
        /// The assemblies being documented. Kept apart from the list, which shows only what the
        /// Microsoft switch and the filter box let through, and outlives both.
        /// </summary>
        private readonly HashSet<Guid> _checkedAssemblies = new HashSet<Guid>();

        /// <summary>
        /// Classes the user took out. Held as the exception rather than the rule, because a class
        /// arrives checked and has to survive the list being rebuilt around it.
        /// </summary>
        private readonly HashSet<Guid> _excludedTypes = new HashSet<Guid>();

        /// <summary>Set while code, not the user, is ticking boxes.</summary>
        private bool _rendering;

        private SplitContainer _mainSplit;
        private SplitContainer _leftSplit;
        private Button _btnLoadAssemblies;
        private CheckBox _chkShowMicrosoft;
        private TextBox _txtFilter;
        private CheckBox _chkAllAssemblies;
        private Label _lblStatus;
        private ListView _lvAssemblies;
        private ListView _lvTypes;

        /// <summary>
        /// Checking a box fires one event per box, and selecting the whole list fires one per row.
        /// This waits for the flurry to end so the environment is asked once.
        /// </summary>
        private Timer _checkSettled;

        /// <summary>
        /// The preview redraws itself from whatever is ticked, and colouring it costs a pass over
        /// the whole buffer, so a run of ticks waits for its own end the same way.
        /// </summary>
        private Timer _previewSettled;

        private Panel _toolbar;
        private TextBox _txtFolder;
        private Button _btnBrowse;
        private RadioButton _rbAttributes;
        private RadioButton _rbComment;
        private Button _btnWrite;
        private Button _btnCreateDefinitions;
        private RichTextBox _txtPreview;

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

            var leftToolbar = new Panel { Dock = DockStyle.Top, Height = 90, Padding = new Padding(5) };
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

            // No signature test settles every environment, and an ISV's app is not Microsoft's
            // and not yours either. Typing your own name is the answer that never needs one.
            var lblFilter = new Label { Text = "Filter:", Location = new Point(5, 39), AutoSize = true };
            _txtFilter = new TextBox
            {
                Location = new Point(50, 36),
                Width = 251,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _txtFilter.TextChanged += (s, e) => RenderAssemblies();

            // Tri-state, and driven from code only: the user's click means "everything" or
            // "nothing", never the third thing the box shows when the list is partly ticked.
            _chkAllAssemblies = new CheckBox
            {
                Text = "All",
                Location = new Point(5, 66),
                Width = 46,
                AutoSize = false,
                AutoCheck = false,
                Enabled = false
            };
            _chkAllAssemblies.Click += (s, e) => CheckAllAssemblies(_chkAllAssemblies.CheckState != CheckState.Checked);

            _lblStatus = new Label
            {
                Location = new Point(55, 68),
                Width = 246,
                Height = 16,
                AutoSize = false,
                AutoEllipsis = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = SystemColors.GrayText,
                Text = "Load the assemblies to start."
            };

            leftToolbar.Controls.Add(_btnLoadAssemblies);
            leftToolbar.Controls.Add(_chkShowMicrosoft);
            leftToolbar.Controls.Add(lblFilter);
            leftToolbar.Controls.Add(_txtFilter);
            leftToolbar.Controls.Add(_chkAllAssemblies);
            leftToolbar.Controls.Add(_lblStatus);

            // Checked, not selected: a project that ships one assembly per plugin needs all of them
            // documented in one pass, so the list is a set rather than a pointer at one row.
            _lvAssemblies = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = true,
                CheckBoxes = true,
                HideSelection = false,
                Font = new Font("Segoe UI", 9f)
            };
            _lvAssemblies.Columns.Add("Assembly", 210);
            _lvAssemblies.Columns.Add("Isolation", 80);
            _lvAssemblies.ItemChecked += LvAssemblies_ItemChecked;

            _checkSettled = new Timer { Interval = 120 };
            _checkSettled.Tick += (s, e) =>
            {
                _checkSettled.Stop();
                LoadCheckedTypes();
            };

            _previewSettled = new Timer { Interval = 120 };
            _previewSettled.Tick += (s, e) =>
            {
                _previewSettled.Stop();
                RenderPreview();
            };

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
            _lvTypes.ItemChecked += LvTypes_ItemChecked;

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
            _rbAttributes.CheckedChanged += (s, e) => UpdateButtonState();
            _rbComment = new RadioButton
            {
                Text = "Readable summary comment",
                Location = new Point(250, 38),
                Width = 190
            };

            _btnWrite = new Button { Text = "Write to Files", Location = new Point(5, 64), Width = 110, Height = 26, Enabled = false };
            _btnWrite.Click += BtnWrite_Click;
            _btnCreateDefinitions = new Button { Text = "Create Attribute Definitions File", Location = new Point(120, 64), Width = 210, Height = 26, Enabled = false };
            _btnCreateDefinitions.Click += BtnCreateDefinitions_Click;

            _toolbar.Controls.AddRange(new Control[]
            {
                lblFolder, _txtFolder, _btnBrowse,
                lblOutput, _rbAttributes, _rbComment,
                _btnWrite, _btnCreateDefinitions
            });

            _txtPreview = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ScrollBars = RichTextBoxScrollBars.Both,
                WordWrap = false,
                ReadOnly = true,
                // The attribute definitions file carries documentation URLs, and a RichTextBox
                // left to itself would underline them in its own blue over the colouring.
                DetectUrls = false,
                BackColor = Color.White,
                Font = new Font("Consolas", 9f)
            };

            _mainSplit.Panel2.Controls.Add(_txtPreview);
            _mainSplit.Panel2.Controls.Add(_toolbar);

            Controls.Add(_mainSplit);
            Load += (s, e) =>
            {
                SetInitialSplit();
                RenderPreview();
            };
            ResumeLayout(false);
        }

        /// <summary>
        /// A SplitContainer silently refuses a distance wider than it is, and at construction time
        /// it is a default sized control, so the lists only get their width once the host has
        /// handed this one its real size.
        /// </summary>
        private void SetInitialSplit()
        {
            var widest = _mainSplit.Width - _mainSplit.Panel2MinSize - _mainSplit.SplitterWidth;
            if (widest > 0)
            {
                _mainSplit.SplitterDistance = Math.Min(320, widest);
            }
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
                    _typesByAssembly.Clear();
                    _checkedAssemblies.Clear();
                    _excludedTypes.Clear();
                    RenderAssemblies();
                    RenderTypes();
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

            var filter = _txtFilter.Text.Trim();
            var visible = _assemblies
                .Where(a => _chkShowMicrosoft.Checked || !a.IsMicrosoft)
                .Where(a => filter.Length == 0
                            || (a.Name != null && a.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();

            // Hiding a ticked assembly does not untick it. A filter is typed a letter at a time,
            // and losing a selection to a keystroke would be unforgivable; its classes stay in the
            // list below under their own heading, and the status line counts what is out of sight.
            _rendering = true;
            _lvAssemblies.BeginUpdate();
            _lvAssemblies.Items.Clear();
            foreach (var assembly in visible)
            {
                var item = new ListViewItem(assembly.Name)
                {
                    Tag = assembly,
                    Checked = _checkedAssemblies.Contains(assembly.Id)
                };
                item.SubItems.Add(assembly.IsolationMode == 2 ? "Sandbox" : "None");
                _lvAssemblies.Items.Add(item);
            }

            _lvAssemblies.EndUpdate();
            _rendering = false;

            _chkAllAssemblies.Enabled = visible.Count > 0;
            UpdateStatus();
        }

        private void LvAssemblies_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_rendering)
            {
                return;
            }

            var assembly = (AssemblyInfo)e.Item.Tag;
            if (e.Item.Checked)
            {
                _checkedAssemblies.Add(assembly.Id);
            }
            else
            {
                _checkedAssemblies.Remove(assembly.Id);
            }

            // The counts stay a beat behind until the types arrive; the tally of assemblies
            // does not have to.
            UpdateStatus();

            // Selecting rows and hitting space ticks them one at a time. Wait for the last one.
            _checkSettled.Stop();
            _checkSettled.Start();
        }

        private void CheckAllAssemblies(bool check)
        {
            _rendering = true;
            _lvAssemblies.BeginUpdate();
            foreach (ListViewItem item in _lvAssemblies.Items)
            {
                item.Checked = check;
                var id = ((AssemblyInfo)item.Tag).Id;
                if (check)
                {
                    _checkedAssemblies.Add(id);
                }
                else
                {
                    _checkedAssemblies.Remove(id);
                }
            }

            _lvAssemblies.EndUpdate();
            _rendering = false;

            LoadCheckedTypes();
        }

        /// <summary>
        /// Fetches the types of every checked assembly not fetched already, in one round trip,
        /// then puts the list back together.
        /// </summary>
        private void LoadCheckedTypes()
        {
            var missing = _checkedAssemblies.Where(id => !_typesByAssembly.ContainsKey(id)).ToList();
            if (missing.Count == 0)
            {
                RenderTypes();
                return;
            }

            WorkAsync(new WorkAsyncInfo
            {
                Message = missing.Count == 1
                    ? "Loading registered steps..."
                    : "Loading registered steps from " + missing.Count + " assemblies...",
                Work = (worker, args) => { args.Result = RegistrationQuery.GetPluginTypes(Service, missing); },
                PostWorkCallBack = result =>
                {
                    if (result.Error != null)
                    {
                        ShowErrorDialog(result.Error);
                        return;
                    }

                    var loaded = ((List<PluginTypeInfo>)result.Result).ToLookup(t => t.AssemblyId);

                    // Every assembly asked for is recorded, including the ones that turned out to
                    // have nothing registered, so unticking and reticking one does not ask again.
                    foreach (var id in missing)
                    {
                        _typesByAssembly[id] = loaded[id].ToList();
                    }

                    RenderTypes();
                }
            });
        }

        /// <summary>
        /// Rebuilds the class list from the checked assemblies, one group per assembly, keeping
        /// whatever the user has already unticked.
        /// </summary>
        private void RenderTypes()
        {
            _rendering = true;
            _lvTypes.BeginUpdate();
            _lvTypes.Items.Clear();
            _lvTypes.Groups.Clear();

            foreach (var assembly in _assemblies.Where(a => _checkedAssemblies.Contains(a.Id)))
            {
                List<PluginTypeInfo> types;
                if (!_typesByAssembly.TryGetValue(assembly.Id, out types))
                {
                    continue;
                }

                if (types.Count == 0)
                {
                    // An empty group draws nothing at all, so the assembly would simply be
                    // missing. The status line counts these instead.
                    continue;
                }

                var group = new ListViewGroup(assembly.Name);
                _lvTypes.Groups.Add(group);

                foreach (var type in types)
                {
                    var item = new ListViewItem(type.ClassName, group)
                    {
                        Tag = type,
                        Checked = !_excludedTypes.Contains(type.Id),
                        ToolTipText = type.TypeName
                    };
                    item.SubItems.Add(type.Steps.Count.ToString());
                    _lvTypes.Items.Add(item);
                }
            }

            _lvTypes.EndUpdate();
            _rendering = false;

            UpdateButtonState();
        }

        private void LvTypes_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_rendering)
            {
                return;
            }

            var type = (PluginTypeInfo)e.Item.Tag;
            if (e.Item.Checked)
            {
                _excludedTypes.Remove(type.Id);
            }
            else
            {
                _excludedTypes.Add(type.Id);
            }

            UpdateButtonState();
        }

        private void UpdateButtonState()
        {
            var hasChecked = _lvTypes.CheckedItems.Count > 0;
            var hasFolder = _txtFolder.Text.Trim().Length > 0 && Directory.Exists(_txtFolder.Text.Trim());

            _btnWrite.Enabled = hasChecked && hasFolder;
            // A comment needs no attribute definitions to compile against.
            _btnCreateDefinitions.Enabled = hasFolder && _rbAttributes.Checked;

            UpdateStatus();

            // Whatever is ticked is what the preview shows, so nothing has to be asked for.
            _previewSettled.Stop();
            _previewSettled.Start();
        }

        /// <summary>
        /// Says what is about to be written, because with the classes grouped under dozens of
        /// assemblies the answer is no longer whatever happens to be on screen.
        /// </summary>
        private void UpdateStatus()
        {
            var shown = _lvAssemblies.Items.Count;
            var shownAndChecked = _lvAssemblies.Items.Cast<ListViewItem>().Count(i => i.Checked);
            var chosen = _checkedAssemblies.Count;

            // The box speaks for the rows on screen, so that filtering to your own name and
            // hitting All means all of yours, not all of everybody's.
            _chkAllAssemblies.CheckState =
                shown > 0 && shownAndChecked == shown ? CheckState.Checked :
                shownAndChecked == 0 ? CheckState.Unchecked : CheckState.Indeterminate;

            if (chosen == 0)
            {
                _lblStatus.Text =
                    _assemblies.Count == 0 ? "Load the assemblies to start." :
                    shown == 0 ? "Nothing matches." :
                    "Tick the assemblies to document.";
                return;
            }

            var empty = _checkedAssemblies.Count(id =>
            {
                List<PluginTypeInfo> types;
                return _typesByAssembly.TryGetValue(id, out types) && types.Count == 0;
            });

            var hidden = chosen - shownAndChecked;

            _lblStatus.Text = chosen + " assemblies · "
                + _lvTypes.CheckedItems.Count + " of " + _lvTypes.Items.Count + " classes"
                + (hidden == 0 ? string.Empty : " · " + hidden + " out of view")
                + (empty == 0 ? string.Empty : " · " + empty + " with no steps");
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
            var folder = FolderPicker.Pick(this, "Select the folder containing your plugin source files", _txtFolder.Text);
            if (folder != null)
            {
                _txtFolder.Text = folder;
            }
        }

        /// <summary>
        /// Shows what the checked classes would be given, in the mode that is selected. Run
        /// whenever either of those changes, so the pane is the answer rather than a request.
        /// </summary>
        private void RenderPreview()
        {
            var types = CheckedTypes();
            if (types.Count == 0)
            {
                CsSyntaxHighlighter.Plain(_txtPreview, "Tick the classes on the left to see what they would be given.");
                return;
            }

            var names = _assemblies.ToDictionary(a => a.Id, a => a.Name);
            var assembly = Guid.Empty;

            var sb = new StringBuilder();
            foreach (var type in types)
            {
                // With a class per assembly the type names alone read as one long list of
                // strangers, so each assembly announces itself once.
                if (type.AssemblyId != assembly)
                {
                    assembly = type.AssemblyId;
                    string name;
                    sb.AppendLine("// ===== " + (names.TryGetValue(assembly, out name) ? name : "Unknown assembly"));
                }

                sb.AppendLine("// " + type.TypeName);
                foreach (var line in Remarks(type) ?? Attributes(type))
                {
                    sb.AppendLine(line);
                }

                sb.AppendLine("public partial class " + type.ClassName);
                sb.AppendLine();
            }

            CsSyntaxHighlighter.Apply(_txtPreview, sb.ToString());
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

            // A tally of what happened to which file, not source, so it is left uncoloured.
            CsSyntaxHighlighter.Plain(_txtPreview, report.ToString());
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
                CsSyntaxHighlighter.Apply(_txtPreview, AttributeDefinitions.Source);
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
