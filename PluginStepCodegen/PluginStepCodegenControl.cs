using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Xrm.Sdk;
using PluginStepCodegen.Logic;
using XrmToolBox.Extensibility;
using Label = System.Windows.Forms.Label;

namespace PluginStepCodegen
{
    public partial class PluginStepCodegenControl : PluginControlBase
    {
        private List<AssemblyInfo> _assemblies = new List<AssemblyInfo>();

        /// <summary>Types already fetched, keyed by assembly, so re-checking one costs nothing.</summary>
        private readonly Dictionary<Guid, List<PluginTypeInfo>> _typesByAssembly = new Dictionary<Guid, List<PluginTypeInfo>>();

        /// <summary>
        /// The assemblies being documented. Kept apart from the list, which shows only what the
        /// two switches and the filter box let through, and outlives all three.
        /// </summary>
        private readonly HashSet<Guid> _checkedAssemblies = new HashSet<Guid>();

        /// <summary>
        /// Classes the user took out. Held as the exception rather than the rule, because a class
        /// arrives checked and has to survive the list being rebuilt around it.
        /// </summary>
        private readonly HashSet<Guid> _excludedTypes = new HashSet<Guid>();

        /// <summary>Set while code, not the user, is ticking boxes.</summary>
        private bool _rendering;

        private bool _splittersLaid;

        /// <summary>Kept in fields because a control does not own the font it is handed.</summary>
        private readonly Font _listFont = new Font("Segoe UI", 9f);
        private readonly Font _codeFont = new Font("Consolas", 9f);

        private SplitContainer _mainSplit;
        private SplitContainer _leftSplit;
        private Button _btnLoadAssemblies;
        private CheckBox _chkShowMicrosoft;
        private CheckBox _chkShowManaged;
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

        private TableLayoutPanel _toolbar;
        private TextBox _txtFolder;
        private Button _btnBrowse;
        private RadioButton _rbAttributes;
        private RadioButton _rbComment;
        private Button _btnWrite;
        private Button _btnCreateDefinitions;
        private Label _lblWriteHint;
        private RichTextBox _txtPreview;

        public PluginStepCodegenControl()
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
                FixedPanel = FixedPanel.Panel1
            };

            // ===== LEFT: assemblies (top) + plugin types (bottom) =====
            _leftSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal
            };

            // Laid out rather than positioned: the pane is a splitter away from any width, and at
            // fixed coordinates the filter and the status line are either clipped or adrift.
            var leftToolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 3,
                Padding = new Padding(5, 5, 5, 3)
            };
            leftToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            leftToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            _btnLoadAssemblies = new Button
            {
                Text = "Load Assemblies",
                Width = 150,
                Height = 26,
                Margin = new Padding(0, 0, 8, 4)
            };
            _btnLoadAssemblies.Click += BtnLoadAssemblies_Click;
            _chkShowMicrosoft = new CheckBox
            {
                Text = "Microsoft's",
                AutoSize = true,
                Margin = new Padding(0, 5, 0, 4)
            };
            _chkShowMicrosoft.CheckedChanged += (s, e) => RenderAssemblies();

            // Everything else that arrived in a solution: an ISV's app, or your own in a build that
            // is no longer the one on disk. Documenting one is a real thing to want - the only
            // environment you can reach is not always the one you develop in - but it is the
            // exception, so it is off by default and one click away.
            _chkShowManaged = new CheckBox
            {
                Text = "Managed",
                AutoSize = true,
                Margin = new Padding(12, 5, 0, 4)
            };
            _chkShowManaged.CheckedChanged += (s, e) => RenderAssemblies();

            // No signature test settles every environment, and an ISV's app is not Microsoft's
            // and not yours either. Typing your own name is the answer that never needs one.
            var lblFilter = new Label
            {
                Text = "Filter:",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 6, 4)
            };
            _txtFilter = new TextBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 0, 0, 4)
            };
            _txtFilter.TextChanged += (s, e) => RenderAssemblies();

            // Tri-state, and driven from code only: the user's click means "everything" or
            // "nothing", never the third thing the box shows when the list is partly ticked.
            _chkAllAssemblies = new CheckBox
            {
                Text = "All",
                AutoSize = true,
                AutoCheck = false,
                Enabled = false,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 6, 0)
            };
            _chkAllAssemblies.Click += (s, e) => CheckAllAssemblies(_chkAllAssemblies.CheckState != CheckState.Checked);

            _lblStatus = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0),
                Text = "Load the assemblies to start."
            };

            // Both on one wrapping row of their own: in a cell of the grid the button would set the
            // width of the whole first column and the filter box would start where it ends.
            var loadRow = Row(_btnLoadAssemblies, _chkShowMicrosoft, _chkShowManaged);
            leftToolbar.Controls.Add(loadRow, 0, 0);
            leftToolbar.SetColumnSpan(loadRow, 2);
            leftToolbar.Controls.Add(lblFilter, 0, 1);
            leftToolbar.Controls.Add(_txtFilter, 1, 1);
            leftToolbar.Controls.Add(_chkAllAssemblies, 0, 2);
            leftToolbar.Controls.Add(_lblStatus, 1, 2);

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
                Font = _listFont
            };
            _lvAssemblies.Columns.Add("Assembly");
            _lvAssemblies.Columns.Add("Isolation");
            // The floor sits just under what the pane's own minimum leaves once a scrollbar has
            // taken its share, so the fallback to sideways scrolling is reserved for a pane
            // narrower than the splitter will allow.
            ShareWidthBetweenColumns(_lvAssemblies, 230, 0.74f, 0.26f);
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
                // The rows carry the namespace the class name was trimmed from, which a ListView
                // shows only when asked.
                ShowItemToolTips = true,
                Font = _listFont
            };
            _lvTypes.Columns.Add("Plugin Class");
            _lvTypes.Columns.Add("Steps", -1, HorizontalAlignment.Right);
            ShareWidthBetweenColumns(_lvTypes, 230, 0.8f, 0.2f);
            _lvTypes.ItemChecked += LvTypes_ItemChecked;

            _leftSplit.Panel2.Controls.Add(_lvTypes);
            _mainSplit.Panel1.Controls.Add(_leftSplit);

            // ===== RIGHT: toolbar + preview =====
            _toolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 4,
                Padding = new Padding(5, 5, 5, 5)
            };
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var lblFolder = new Label
            {
                Text = "Source folder:",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 6, 4)
            };
            // A path is as long as it is, so the field takes whatever the pane can spare rather
            // than a fixed 400px that clipped the Browse button off the edge when docked narrow.
            _txtFolder = new TextBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 0, 6, 4)
            };
            _txtFolder.TextChanged += (s, e) => UpdateButtonState();
            _btnBrowse = new Button
            {
                Text = "Browse...",
                Width = 80,
                Height = 24,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 0, 4)
            };
            _btnBrowse.Click += BtnBrowse_Click;

            _toolbar.Controls.Add(lblFolder, 0, 0);
            _toolbar.Controls.Add(_txtFolder, 1, 0);
            _toolbar.Controls.Add(_btnBrowse, 2, 0);

            var lblOutput = new Label
            {
                Text = "Write:",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 6, 4)
            };
            _rbAttributes = new RadioButton { Text = "Xrm Tools attributes", AutoSize = true, Checked = true, Margin = new Padding(0, 0, 16, 0) };
            _rbAttributes.CheckedChanged += (s, e) => UpdateButtonState();
            _rbComment = new RadioButton { Text = "Readable summary comment", AutoSize = true, Margin = new Padding(0) };

            var modeRow = Row(_rbAttributes, _rbComment);
            modeRow.Margin = new Padding(0, 0, 0, 4);
            _toolbar.Controls.Add(lblOutput, 0, 1);
            _toolbar.Controls.Add(modeRow, 1, 1);
            _toolbar.SetColumnSpan(modeRow, 2);

            _btnWrite = new Button { Text = "Write to Files", Width = 110, Height = 26, Enabled = false, Margin = new Padding(0, 0, 6, 0) };
            _btnWrite.Click += BtnWrite_Click;
            _btnCreateDefinitions = new Button { Text = "Create Attribute Definitions File", Width = 210, Height = 26, Enabled = false, Margin = new Padding(0) };
            _btnCreateDefinitions.Click += BtnCreateDefinitions_Click;

            var buttonRow = Row(_btnWrite, _btnCreateDefinitions);
            _toolbar.Controls.Add(buttonRow, 0, 2);
            _toolbar.SetColumnSpan(buttonRow, 3);

            // A disabled button gives no reason, and the only status line the tool had is in the
            // other pane counting assemblies. This one answers for the two buttons above it.
            _lblWriteHint = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Height = 17,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 3, 0, 0)
            };
            _toolbar.Controls.Add(_lblWriteHint, 0, 3);
            _toolbar.SetColumnSpan(_lblWriteHint, 3);

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
                Font = _codeFont
            };

            _mainSplit.Panel2.Controls.Add(_txtPreview);
            _mainSplit.Panel2.Controls.Add(_toolbar);

            Controls.Add(_mainSplit);
            ResumeLayout(false);
        }

        /// <summary>
        /// A row of controls that wraps rather than clips: "Create Attribute Definitions File" is
        /// wide enough that the pair does not fit on one line once the tool is docked narrow, and a
        /// button that falls to a second line beats one cut in half.
        /// </summary>
        private static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };
            row.Controls.AddRange(controls);
            return row;
        }

        /// <summary>
        /// XrmToolBox builds a fresh control each time the tool is opened and disposes it when the
        /// tab is closed, but nothing here belongs to a container, so nothing here goes with it:
        /// the two timers would keep ticking against a dead control, and the fonts are handles the
        /// controls they were handed to never owned.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_checkSettled != null) _checkSettled.Dispose();
                if (_previewSettled != null) _previewSettled.Dispose();
            }

            base.Dispose(disposing);

            // After the controls that were drawing with them are gone.
            if (disposing)
            {
                _listFont.Dispose();
                _codeFont.Dispose();
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            LaySplitters();
            RenderPreview();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            // Only until it takes: after that the splitters belong to whoever drags them.
            if (!_splittersLaid && _mainSplit != null) LaySplitters();
        }

        /// <summary>
        /// Panel sizes are validated against the container's *current* size, and during
        /// InitializeComponent that is still the 150x100 default a SplitContainer starts at: a
        /// distance set there is silently shrunk to fit and a min size set there throws outright.
        /// Both have to wait until the tool has been handed its real size, which is Load unless
        /// XrmToolBox builds the tab while it is still hidden - hence the retry from OnSizeChanged.
        /// </summary>
        private void LaySplitters()
        {
            // 400 is what the load row measures: the button, both switches with their counts
            // spelled out, and the margins between them. A share of the window was the rule before
            // there were two switches, and it put the second one on a line of its own on any window
            // narrower than about 1250. The pane has nothing to do with the extra width anyway -
            // assembly and class names are short, and the preview is what wants the rest. Below the
            // floor the switches wrap rather than being clipped.
            _splittersLaid =
                LaySplit(_mainSplit, 270, 340, 400) &
                // Panel1 carries the toolbar as well as the list, so its minimum is that much
                // taller than the class list's below it.
                LaySplit(_leftSplit, 170, 90, (int)(_leftSplit.Height * 0.5));
        }

        private static bool LaySplit(SplitContainer split, int min1, int min2, int distance)
        {
            var span = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
            if (span < min1 + min2 + split.SplitterWidth) return false;

            split.Panel1MinSize = min1;
            split.Panel2MinSize = min2;
            split.SplitterDistance = Math.Min(Math.Max(distance, min1), span - min2 - split.SplitterWidth);
            return true;
        }

        /// <summary>
        /// List columns are plain pixel widths, so a list narrower than their sum scrolls sideways
        /// and a wider one leaves dead space past the last column. Give each column a share of the
        /// list instead and redistribute it whenever the list is resized. Below
        /// <paramref name="minTotal"/> the shares would ellipsise every cell down to nothing, so
        /// the columns stop shrinking there and the list scrolls sideways as before.
        /// </summary>
        private static void ShareWidthBetweenColumns(ListView list, int minTotal, params float[] shares)
        {
            EventHandler apply = (s, e) =>
            {
                var available = Math.Max(list.ClientSize.Width - 4, minTotal);
                if (available <= 0) return;

                list.BeginUpdate();
                var used = 0;
                for (var i = 0; i < list.Columns.Count; i++)
                {
                    // The last column absorbs the rounding so the widths always add up exactly.
                    var width = i == list.Columns.Count - 1 ? available - used : (int)(available * shares[i]);
                    list.Columns[i].Width = width;
                    used += width;
                }
                list.EndUpdate();
            };

            list.ClientSizeChanged += apply;
            apply(list, EventArgs.Empty);
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
        /// Fills the list from what was loaded. What is left by default is the unmanaged
        /// registrations - a plugin somebody is in the middle of writing, which is what this tool is
        /// for. An environment carries dozens of Microsoft's assemblies and however many an ISV
        /// installed, and neither has source in anybody's folder, so both are held back with the
        /// count on the switch that brings them back rather than the list quietly being short.
        ///
        /// The two switches govern disjoint sets, which is why the tests are asked in this order
        /// rather than chained: every one of Microsoft's is managed too, and if both applied to the
        /// same row then ticking "Microsoft's" while Managed was off would show nothing at all and
        /// the switch would look broken.
        /// </summary>
        private void RenderAssemblies()
        {
            var microsoft = _assemblies.Count(a => a.IsMicrosoft);
            _chkShowMicrosoft.Text = microsoft == 0
                ? "Microsoft's"
                : "Microsoft's (" + microsoft + ")";

            var managed = _assemblies.Count(a => a.IsManaged && !a.IsMicrosoft);
            _chkShowManaged.Text = managed == 0
                ? "Managed"
                : "Managed (" + managed + ")";

            var filter = _txtFilter.Text.Trim();
            var visible = _assemblies
                .Where(a => a.IsMicrosoft
                    ? _chkShowMicrosoft.Checked
                    : _chkShowManaged.Checked || !a.IsManaged)
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
            var folder = _txtFolder.Text.Trim();
            var hasFolder = folder.Length > 0 && Directory.Exists(folder);

            _btnWrite.Enabled = hasChecked && hasFolder;
            // A comment needs no attribute definitions to compile against.
            _btnCreateDefinitions.Enabled = hasFolder && _rbAttributes.Checked;

            // A path is typed or pasted a character at a time and one wrong letter reads the same
            // as a right one, so the field says whether it landed rather than leaving the buttons
            // to go quiet for a reason nothing gives.
            _txtFolder.ForeColor = folder.Length > 0 && !hasFolder ? Color.Firebrick : SystemColors.WindowText;
            _lblWriteHint.Text =
                folder.Length == 0 ? "Choose the folder your plugin source is in." :
                !hasFolder ? "No folder at that path." :
                !hasChecked ? "Tick the classes to document." :
                string.Empty;

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
