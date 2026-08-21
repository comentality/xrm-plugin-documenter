using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
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
        /// Each stepped entity's current columns, fetched alongside its steps and kept for the
        /// session. What lets the comment say "(all columns except ...)" instead of reciting
        /// seventy names. Entities the fetch could not answer for are simply absent, and the
        /// comment falls back to the plain list.
        /// </summary>
        private readonly Dictionary<string, EntityColumnsInfo> _columnsByEntity = new Dictionary<string, EntityColumnsInfo>(StringComparer.OrdinalIgnoreCase);

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
        /// <summary>A size up from the toolbar's own, so the dagger reads as a mark rather than a speck.</summary>
        private readonly Font _daggerFont = new Font("Segoe UI", 11f);
        /// <summary>Loaded before the UI is built, because the experimental menu's check marks read from it.</summary>
        private ExperimentalSettings _experimental = new ExperimentalSettings();
        private Button _btnExperimental;
        private ContextMenuStrip _experimentalMenu;

        private SplitContainer _mainSplit;
        private SplitContainer _leftSplit;
        private SplitContainer _rightSplit;
        private Button _btnLoadAssemblies;
        private Button _btnRefresh;
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
        private Label _lblScanStatus;
        private ListView _lvSource;
        private Button _btnPreviewToggle;
        private RadioButton _rbAttributes;
        private RadioButton _rbComment;
        private Button _btnWrite;
        private Button _btnCreateDefinitions;
        private CheckBox _chkWriteAmbiguous;
        private Label _lblWriteHint;
        private RichTextBox _txtPreview;

        /// <summary>A folder path is typed a character at a time; the scan waits for the last one.</summary>
        private Timer _scanSettled;

        /// <summary>The latest look at the source folder, null until there is one to look at.</summary>
        private FolderScan _scan;

        /// <summary>Bumped whenever a scan starts, so a slow one landing late is thrown away.</summary>
        private int _scanGeneration;

        /// <summary>Set while one list's selection is being echoed into the other.</summary>
        private bool _syncingSelection;

        private static readonly Color GlyphGreen = Color.FromArgb(26, 127, 55);
        private static readonly Color GlyphAmber = Color.FromArgb(154, 103, 0);
        private static readonly Color GlyphRed = Color.Firebrick;

        public PluginStepCodegenControl()
        {
            ExperimentalSettings loaded;
            if (SettingsManager.Instance.TryLoad(typeof(PluginStepCodegenControl), out loaded) && loaded != null)
            {
                _experimental = loaded;
            }

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

            // Sized to their captions rather than round numbers: with Refresh beside it, the row
            // has to fit the pane at its resting width or the switches fall to a second line.
            _btnLoadAssemblies = new Button
            {
                Text = "Load Assemblies",
                Width = 122,
                Height = 26,
                Margin = new Padding(0, 0, 6, 4)
            };
            _btnLoadAssemblies.Click += BtnLoadAssemblies_Click;

            // Rereads the environment without resetting the session: what is ticked, what is
            // excluded, the filter and the folder all survive. For the loop this tool lives in -
            // register from the IDE, come back, refresh, write.
            _btnRefresh = new Button
            {
                Text = "Refresh",
                Width = 64,
                Height = 26,
                Enabled = false,
                Margin = new Padding(0, 0, 6, 4)
            };
            _btnRefresh.Click += BtnRefresh_Click;

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
            var loadRow = Row(_btnLoadAssemblies, _btnRefresh, _chkShowMicrosoft, _chkShowManaged);
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
            _lvAssemblies.Columns.Add("Source");
            // The floor sits just under what the pane's own minimum leaves once a scrollbar has
            // taken its share, so the fallback to sideways scrolling is reserved for a pane
            // narrower than the splitter will allow.
            ShareWidthBetweenColumns(_lvAssemblies, 230, 0.52f, 0.24f, 0.24f);
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
            _lvTypes.Columns.Add("Source");
            ShareWidthBetweenColumns(_lvTypes, 230, 0.62f, 0.16f, 0.22f);
            _lvTypes.ItemChecked += LvTypes_ItemChecked;
            _lvTypes.ItemSelectionChanged += LvTypes_ItemSelectionChanged;

            _leftSplit.Panel2.Controls.Add(_lvTypes);
            _mainSplit.Panel1.Controls.Add(_leftSplit);

            // ===== MIDDLE: source folder + what the scan found =====
            _rightSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel1
            };

            var sourceToolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 2,
                Padding = new Padding(5, 5, 5, 3)
            };
            sourceToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            sourceToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            sourceToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

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
            _txtFolder.TextChanged += (s, e) =>
            {
                UpdateButtonState();
                _scanSettled.Stop();
                _scanSettled.Start();
            };
            _btnBrowse = new Button
            {
                Text = "Browse...",
                Width = 80,
                Height = 24,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 0, 4)
            };
            _btnBrowse.Click += BtnBrowse_Click;

            _lblScanStatus = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Height = 17,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0),
                Text = "Choose the folder your plugin source is in."
            };

            sourceToolbar.Controls.Add(lblFolder, 0, 0);
            sourceToolbar.Controls.Add(_txtFolder, 1, 0);
            sourceToolbar.Controls.Add(_btnBrowse, 2, 0);
            sourceToolbar.Controls.Add(_lblScanStatus, 0, 1);
            sourceToolbar.SetColumnSpan(_lblScanStatus, 3);

            _scanSettled = new Timer { Interval = 500 };
            _scanSettled.Tick += (s, e) =>
            {
                _scanSettled.Stop();
                StartScan();
            };

            // No checkboxes: nothing here is picked for an operation, it is the scan's ledger.
            // Selection echoes into the class list and back so the two read as one thing.
            _lvSource = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                ShowItemToolTips = true,
                Font = _listFont
            };
            _lvSource.Columns.Add("Local source");
            _lvSource.Columns.Add("State");
            ShareWidthBetweenColumns(_lvSource, 210, 0.66f, 0.34f);
            _lvSource.ItemSelectionChanged += LvSource_ItemSelectionChanged;

            _rightSplit.Panel1.Controls.Add(_lvSource);
            _rightSplit.Panel1.Controls.Add(sourceToolbar);

            // ===== RIGHT: the write toolbar, above both the source column and the preview =====
            // Not inside the collapsible pane: Write to Files is the tool's primary action and
            // has to survive the preview being put away. Only the code view collapses.
            _toolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 3,
                Padding = new Padding(5, 5, 5, 5)
            };
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var lblOutput = new Label
            {
                Text = "Write:",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 6, 4)
            };
            // The scan's staleness marks are about the mode that is selected, so a mode switch
            // re-renders them along with everything UpdateButtonState already covered.
            _rbAttributes = new RadioButton { Text = "Xrm Tools attributes", AutoSize = true, Checked = true, Margin = new Padding(0, 0, 16, 0) };
            _rbAttributes.CheckedChanged += (s, e) => RenderScan();
            _rbComment = new RadioButton { Text = "Readable summary comment", AutoSize = true, Margin = new Padding(0) };

            // Puts the code view away and hands its width to the source column, for the sessions
            // that are about auditing the marks rather than reading what would be written. At the
            // toolbar's right edge, against the pane it toggles.
            // No Anchor on either button: inside the flow row an anchor across the flow
            // direction makes the layout wrap, and the row itself is what hugs the right edge.
            _btnPreviewToggle = new Button
            {
                Text = "Preview ▸",
                Width = 78,
                Height = 24,
                Margin = new Padding(6, 0, 0, 4)
            };
            _btnPreviewToggle.Click += (s, e) =>
            {
                var hide = !_rightSplit.Panel2Collapsed;
                _rightSplit.Panel2Collapsed = hide;
                _btnPreviewToggle.Text = hide ? "◂ Preview" : "Preview ▸";
            };

            // The dagger: experimental settings, experiments running as opt-ins until they
            // earn being the default. A menu rather than a dialog, because each one is a
            // single check and the button should cost one click to inspect. The glyph is
            // plain text, not emoji, so it draws in the toolbar's own font and colour.
            _btnExperimental = new Button
            {
                Text = "†",
                Width = 30,
                Height = 24,
                Margin = new Padding(12, 0, 0, 4),
                Font = _daggerFont
            };

            var miAllColumnsExcept = new ToolStripMenuItem("Say near-complete column lists as \"(all columns except ...)\"")
            {
                CheckOnClick = true,
                Checked = _experimental.AllColumnsExcept,
                ToolTipText = "Comment mode only. An image or filter covering nearly every column of its table\r\n"
                              + "is written as the handful it leaves out, measured against the table's columns today."
            };
            miAllColumnsExcept.CheckedChanged += (s, e) =>
            {
                _experimental.AllColumnsExcept = miAllColumnsExcept.Checked;
                SettingsManager.Instance.Save(typeof(PluginStepCodegenControl), _experimental);
                // Staleness and the preview are both questions about the output, so this
                // re-renders exactly the way a mode switch does.
                RenderScan();
            };

            // Check marks draw in the image margin, so hiding it without opening the check
            // margin would leave a toggle that flips with nothing to show for it.
            _experimentalMenu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };
            _experimentalMenu.Items.Add(new ToolStripMenuItem("Experimental") { Enabled = false });
            _experimentalMenu.Items.Add(new ToolStripSeparator());
            _experimentalMenu.Items.Add(miAllColumnsExcept);
            _btnExperimental.Click += (s, e) => _experimentalMenu.Show(_btnExperimental, new Point(0, _btnExperimental.Height));

            var modeRow = Row(_rbAttributes, _rbComment);
            modeRow.Margin = new Padding(0, 0, 0, 4);
            var cornerRow = Row(_btnExperimental, _btnPreviewToggle);
            cornerRow.Dock = DockStyle.None;
            cornerRow.Anchor = AnchorStyles.Right;
            cornerRow.WrapContents = false;
            _toolbar.Controls.Add(lblOutput, 0, 0);
            _toolbar.Controls.Add(modeRow, 1, 0);
            _toolbar.Controls.Add(cornerRow, 2, 0);

            _btnWrite = new Button { Text = "Write to Files", Width = 110, Height = 26, Enabled = false, Margin = new Padding(0, 0, 6, 0) };
            _btnWrite.Click += BtnWrite_Click;
            _btnCreateDefinitions = new Button { Text = "Create Attribute Definitions File", Width = 210, Height = 26, Enabled = false, Margin = new Padding(0) };
            _btnCreateDefinitions.Click += BtnCreateDefinitions_Click;

            // Only shows itself when the scan has found an ambiguity, because "both" is not a
            // choice anybody should be offered while there is nothing it would apply to. Writing
            // to every declaring file is safe for the partial-class case the ambiguity usually is:
            // the splice replaces this tool's own attributes and touches nothing else.
            _chkWriteAmbiguous = new CheckBox
            {
                Text = "Write to both files when ambiguous",
                AutoSize = true,
                Visible = false,
                // The ambiguity's own amber, so the checkbox reads as belonging to the ⚠ rows -
                // darkened a step, because the lists' amber sits on white and this sits on the
                // toolbar's grey, where the same value falls just short of a 4.5:1 contrast.
                ForeColor = Color.FromArgb(138, 92, 0),
                Margin = new Padding(12, 5, 0, 0)
            };
            _chkWriteAmbiguous.CheckedChanged += (s, e) => UpdateButtonState();

            var buttonRow = Row(_btnWrite, _btnCreateDefinitions, _chkWriteAmbiguous);
            _toolbar.Controls.Add(buttonRow, 0, 1);
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
            _toolbar.Controls.Add(_lblWriteHint, 0, 2);
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

            _rightSplit.Panel2.Controls.Add(_txtPreview);
            _mainSplit.Panel2.Controls.Add(_rightSplit);
            _mainSplit.Panel2.Controls.Add(_toolbar);

            EnableDoubleBuffer(_lvAssemblies);
            EnableDoubleBuffer(_lvTypes);
            EnableDoubleBuffer(_lvSource);

            Controls.Add(_mainSplit);
            ResumeLayout(false);
        }

        /// <summary>
        /// A grouped ListView repainted straight to the screen drops rows after a large resize -
        /// which is exactly what collapsing the output pane is. Double buffering is the standard
        /// cure, and the property is non-public for no reason anybody remembers.
        /// </summary>
        private static void EnableDoubleBuffer(ListView list)
        {
            typeof(ListView)
                .GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(list, true, null);
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
                if (_scanSettled != null) _scanSettled.Dispose();
                // A scan in flight checks the generation on arrival; bumping it here is what
                // turns "the control is going away" into "that result is nobody's".
                _scanGeneration++;
            }

            base.Dispose(disposing);

            // After the controls that were drawing with them are gone.
            if (disposing)
            {
                _listFont.Dispose();
                _codeFont.Dispose();
                _daggerFont.Dispose();
                // A ContextMenuStrip belongs to no Controls collection, so nothing else frees it.
                if (_experimentalMenu != null) _experimentalMenu.Dispose();
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
            // 420 is what the load row measures: both buttons, both switches with their counts
            // spelled out, and the margins between them. A share of the window was the rule before
            // there were two switches, and it put the second one on a line of its own on any window
            // narrower than about 1250. Below the floor the row wraps rather than being clipped.
            // The right side's minimum has to cover the other two panes at their own minimums, or
            // the inner splitter is never laid at all.
            _splittersLaid =
                LaySplit(_mainSplit, 250, 526, 420) &
                // Panel1 carries the toolbar as well as the list, so its minimum is that much
                // taller than the class list's below it.
                LaySplit(_leftSplit, 170, 90, (int)(_leftSplit.Height * 0.5)) &
                // The source column earns a fixed share and the preview takes everything else:
                // paths are short, generated code is wide.
                LaySplit(_rightSplit, 220, 302, 340);
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
                    _columnsByEntity.Clear();
                    _checkedAssemblies.Clear();
                    _excludedTypes.Clear();
                    _btnRefresh.Enabled = true;
                    RenderAssemblies();
                    RenderTypes();
                }
            });
        }

        private void BtnRefresh_Click(object sender, EventArgs e)
        {
            ExecuteMethod(RefreshRegistrations);
        }

        /// <summary>
        /// The same fetches as a load, minus the forgetting: ticked assemblies, excluded classes,
        /// the filter and the folder all stand, and only what the environment says is reread. Ids
        /// are stable across a plugin re-registration from the IDE, which is the loop this exists
        /// for; an assembly that genuinely went away drops out of the ticked set silently.
        /// </summary>
        private void RefreshRegistrations()
        {
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Refreshing plugin assemblies...",
                Work = (worker, args) => { args.Result = RegistrationQuery.GetAssemblies(Service); },
                PostWorkCallBack = result =>
                {
                    if (result.Error != null)
                    {
                        ShowErrorDialog(result.Error);
                        return;
                    }

                    _assemblies = (List<AssemblyInfo>)result.Result;
                    var alive = new HashSet<Guid>(_assemblies.Select(a => a.Id));
                    _checkedAssemblies.RemoveWhere(id => !alive.Contains(id));
                    _typesByAssembly.Clear();
                    // Columns move with the registrations - refresh is pressed after changing
                    // things in the IDE, and a new column is exactly such a change.
                    _columnsByEntity.Clear();
                    RenderAssemblies();
                    LoadCheckedTypes();
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
                    Checked = _checkedAssemblies.Contains(assembly.Id),
                    UseItemStyleForSubItems = false
                };
                item.SubItems.Add(assembly.IsolationMode == 2 ? "Sandbox" : "None");
                item.SubItems.Add(string.Empty);
                _lvAssemblies.Items.Add(item);
            }

            AnnotateAssemblies();

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

            // Snapshotted here because the worker must not read a dictionary the UI thread owns.
            var knownEntities = new HashSet<string>(_columnsByEntity.Keys, StringComparer.OrdinalIgnoreCase);

            WorkAsync(new WorkAsyncInfo
            {
                Message = missing.Count == 1
                    ? "Loading registered steps..."
                    : "Loading registered steps from " + missing.Count + " assemblies...",
                Work = (worker, args) =>
                {
                    var types = RegistrationQuery.GetPluginTypes(Service, missing);

                    var entities = types
                        .SelectMany(t => t.Steps)
                        .Select(s => s.PrimaryEntityName)
                        .Where(e => !string.IsNullOrWhiteSpace(e)
                                    && !string.Equals(e, "none", StringComparison.OrdinalIgnoreCase)
                                    && !knownEntities.Contains(e))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    // The columns are garnish on the comment, never worth failing the load over.
                    // Nothing is recorded on a miss, so the next load simply asks again.
                    // Fetched even while the experimental switch is off - one light request buys the
                    // toggle working instantly on whatever is already loaded.
                    Dictionary<string, EntityColumnsInfo> columns;
                    try
                    {
                        columns = RegistrationQuery.GetEntityColumns(Service, entities);
                    }
                    catch
                    {
                        columns = new Dictionary<string, EntityColumnsInfo>();
                    }

                    args.Result = new KeyValuePair<List<PluginTypeInfo>, Dictionary<string, EntityColumnsInfo>>(types, columns);
                },
                PostWorkCallBack = result =>
                {
                    if (result.Error != null)
                    {
                        ShowErrorDialog(result.Error);
                        return;
                    }

                    var fetched = (KeyValuePair<List<PluginTypeInfo>, Dictionary<string, EntityColumnsInfo>>)result.Result;
                    foreach (var entry in fetched.Value)
                    {
                        _columnsByEntity[entry.Key] = entry.Value;
                    }

                    var loaded = fetched.Key.ToLookup(t => t.AssemblyId);

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
                        ToolTipText = type.TypeName,
                        // The source glyph carries its own colour without taking the row with it.
                        UseItemStyleForSubItems = false
                    };
                    item.SubItems.Add(type.Steps.Count.ToString());
                    item.SubItems.Add(string.Empty);
                    _lvTypes.Items.Add(item);
                }
            }

            _lvTypes.EndUpdate();
            _rendering = false;

            UpdateButtonState();
            StartScan();
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
                folder.Length == 0 ? "Pick a source folder first." :
                !hasFolder ? "No folder at that path." :
                !hasChecked ? "Tick the classes to document." :
                WritePlan();

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
            // Without the experimental switch the columns stay out of the call, and the comment
            // reads exactly as it did before the option existed.
            return _rbComment.Checked
                ? RemarksEmitter.Emit(type, _experimental.AllColumnsExcept ? _columnsByEntity : null)
                : null;
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

        private const string GlyphFound = "✓";      // ✓
        private const string GlyphStale = "✎";      // ✎
        private const string GlyphMissing = "✗";    // ✗
        private const string GlyphAmbiguous = "⚠";  // ⚠

        /// <summary>
        /// Holds the folder against the loaded registrations, off the UI thread: the read is
        /// however big somebody's src tree is. Anything that changes either side calls this;
        /// a result that arrives after the next scan started is dropped.
        /// </summary>
        private void StartScan()
        {
            var generation = ++_scanGeneration;
            var folder = _txtFolder.Text.Trim();

            if (folder.Length == 0 || !Directory.Exists(folder) || _typesByAssembly.Count == 0)
            {
                _scan = null;
                RenderScan();
                return;
            }

            var listed = _lvTypes.Items.Cast<ListViewItem>()
                .Select(i => (PluginTypeInfo)i.Tag)
                .ToList();

            // "Not registered" is judged against everything fetched, not just what is ticked,
            // so unticking an assembly does not turn its classes into findings.
            var registeredNames = new HashSet<string>(
                _typesByAssembly.Values.SelectMany(t => t).Select(t => t.ClassName));

            _lblScanStatus.Text = "Scanning...";
            Task.Run(() => SourceScanner.Scan(folder, listed, registeredNames)).ContinueWith(t =>
            {
                if (IsDisposed || !IsHandleCreated)
                {
                    return;
                }

                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (generation != _scanGeneration || IsDisposed)
                        {
                            return;
                        }

                        _scan = t.IsFaulted ? null : t.Result;
                        RenderScan();
                        if (t.IsFaulted)
                        {
                            _lblScanStatus.Text = "Scan failed: " + t.Exception.GetBaseException().Message;
                        }
                    }));
                }
                catch (InvalidOperationException)
                {
                    // The handle went away between the check and the call: nobody to tell.
                }
            });
        }

        /// <summary>
        /// Draws the one scan everywhere it shows: glyphs on the class rows, roll-ups on the
        /// assembly rows, the ledger in the middle column, and the two status lines. Also run
        /// without a new scan when the mode switches, because staleness is a question about
        /// the output that is selected.
        /// </summary>
        private void RenderScan()
        {
            var scan = _scan;

            _lvTypes.BeginUpdate();
            foreach (ListViewItem item in _lvTypes.Items)
            {
                var type = (PluginTypeInfo)item.Tag;
                ClassMatch match;
                string glyph = string.Empty, words = string.Empty, detail = null;
                var color = SystemColors.GrayText;

                if (scan != null && scan.Matches.TryGetValue(type.Id, out match))
                {
                    Describe(type, match, out glyph, out words, out detail, out color);
                }

                var cell = item.SubItems[2];
                cell.Text = glyph.Length == 0 ? string.Empty : glyph + " " + words;
                cell.ForeColor = color;
                item.ToolTipText = detail == null ? type.TypeName : type.TypeName + "\r\n" + detail;
            }

            _lvTypes.EndUpdate();

            AnnotateAssemblies();
            RenderSourceList();

            var ambiguities = scan == null
                ? 0
                : _lvTypes.Items.Cast<ListViewItem>()
                    .Select(i => (PluginTypeInfo)i.Tag)
                    .Count(t => scan.Matches.ContainsKey(t.Id) && scan.Matches[t.Id].Kind == MatchKind.Ambiguous);
            _chkWriteAmbiguous.Visible = ambiguities > 0;

            _lblScanStatus.Text = ScanSummary();
            UpdateButtonState();
        }

        /// <summary>One class's verdict, in every voice that reports it.</summary>
        private void Describe(PluginTypeInfo type, ClassMatch match, out string glyph, out string words, out string detail, out Color color)
        {
            switch (match.Kind)
            {
                case MatchKind.NotFound:
                    glyph = GlyphMissing;
                    words = "no file";
                    detail = "No .cs file under the source folder declares this class.";
                    color = GlyphRed;
                    return;

                case MatchKind.Ambiguous:
                    glyph = GlyphAmbiguous;
                    words = match.Candidates.Count + " files";
                    detail = match.Candidates.Count + " files declare this class:\r\n"
                             + string.Join("\r\n", match.Candidates.Select(f => "  " + Relative(_scan.Folder, f)));
                    color = GlyphAmber;
                    return;

                default:
                    var state = CodeFileWriter.StateOf(match.Code, type.ClassName, Remarks(type), Attributes(type));
                    var file = Relative(_scan.Folder, match.File);
                    if (state == CodeFileWriter.WriteState.Stale)
                    {
                        glyph = GlyphStale;
                        words = "stale";
                        detail = file + "\r\nHas this tool's output, but it no longer matches the registration.";
                        color = GlyphAmber;
                    }
                    else
                    {
                        glyph = GlyphFound;
                        words = state == CodeFileWriter.WriteState.Current ? "current" : "found";
                        detail = file + (state == CodeFileWriter.WriteState.Current
                            ? "\r\nAlready says exactly what the registration says."
                            : "\r\nNothing written yet.");
                        color = GlyphGreen;
                    }

                    return;
            }
        }

        /// <summary>
        /// The per-assembly roll-up: how many of its classes have a file, and the worst thing
        /// the scan found among them, so a bad assembly reads before its group is opened.
        /// </summary>
        private void AnnotateAssemblies()
        {
            var scan = _scan;
            foreach (ListViewItem item in _lvAssemblies.Items)
            {
                var assembly = (AssemblyInfo)item.Tag;
                var cell = item.SubItems[2];

                List<PluginTypeInfo> types;
                if (scan == null || !_typesByAssembly.TryGetValue(assembly.Id, out types))
                {
                    cell.Text = string.Empty;
                    continue;
                }

                if (types.Count == 0)
                {
                    cell.Text = "—";
                    cell.ForeColor = SystemColors.GrayText;
                    continue;
                }

                int found = 0, missing = 0, ambiguous = 0;
                foreach (var type in types)
                {
                    ClassMatch match;
                    if (!scan.Matches.TryGetValue(type.Id, out match))
                    {
                        continue;
                    }

                    if (match.Kind == MatchKind.Found) found++;
                    else if (match.Kind == MatchKind.Ambiguous) ambiguous++;
                    else missing++;
                }

                var glyph = missing > 0 ? GlyphMissing : ambiguous > 0 ? GlyphAmbiguous : GlyphFound;
                cell.Text = found + "/" + types.Count + " " + glyph;
                cell.ForeColor = missing > 0 ? GlyphRed : ambiguous > 0 ? GlyphAmber : GlyphGreen;
            }
        }

        /// <summary>
        /// The middle column's ledger: every verdict as a row, in the class list's order, with
        /// the folder's own surprises - plugin classes nothing registers - at the bottom.
        /// </summary>
        private void RenderSourceList()
        {
            var scan = _scan;

            _lvSource.BeginUpdate();
            _lvSource.Items.Clear();
            _lvSource.Groups.Clear();

            if (scan != null)
            {
                var listed = _lvTypes.Items.Cast<ListViewItem>().Select(i => (PluginTypeInfo)i.Tag).ToList();
                var verdicts = listed
                    .Where(t => scan.Matches.ContainsKey(t.Id))
                    .Select(t => new { Type = t, Match = scan.Matches[t.Id] })
                    .ToList();

                var matched = verdicts.Where(v => v.Match.Kind == MatchKind.Found).ToList();
                var missing = verdicts.Where(v => v.Match.Kind == MatchKind.NotFound).ToList();
                var ambiguous = verdicts.Where(v => v.Match.Kind == MatchKind.Ambiguous).ToList();

                var groupMatched = new ListViewGroup("Matched (" + matched.Count + ")");
                var groupMissing = new ListViewGroup("Not found (" + missing.Count + ")");
                var groupAmbiguous = new ListViewGroup("Ambiguous (" + ambiguous.Count + ")");
                var groupUnregistered = new ListViewGroup("In folder, not registered (" + scan.Unregistered.Count + ")");
                _lvSource.Groups.AddRange(new[] { groupMatched, groupMissing, groupAmbiguous, groupUnregistered });

                foreach (var v in matched)
                {
                    string glyph, words, detail;
                    Color color;
                    Describe(v.Type, v.Match, out glyph, out words, out detail, out color);

                    var item = new ListViewItem(Relative(scan.Folder, v.Match.File), groupMatched)
                    {
                        Tag = v.Type.Id,
                        ToolTipText = v.Type.TypeName + "\r\n" + detail,
                        UseItemStyleForSubItems = false
                    };
                    item.SubItems.Add(words).ForeColor = color;
                    _lvSource.Items.Add(item);
                }

                foreach (var v in missing)
                {
                    var item = new ListViewItem(v.Type.ClassName, groupMissing)
                    {
                        Tag = v.Type.Id,
                        ForeColor = GlyphRed,
                        ToolTipText = v.Type.TypeName + "\r\nNo .cs file under the source folder declares this class.",
                        UseItemStyleForSubItems = false
                    };
                    item.SubItems.Add("no file").ForeColor = GlyphRed;
                    _lvSource.Items.Add(item);
                }

                foreach (var v in ambiguous)
                {
                    var item = new ListViewItem(v.Type.ClassName, groupAmbiguous)
                    {
                        Tag = v.Type.Id,
                        ForeColor = GlyphAmber,
                        ToolTipText = v.Type.TypeName,
                        UseItemStyleForSubItems = false
                    };
                    item.SubItems.Add(v.Match.Candidates.Count + " files").ForeColor = GlyphAmber;
                    _lvSource.Items.Add(item);

                    foreach (var candidate in v.Match.Candidates)
                    {
                        var row = new ListViewItem("    " + Relative(scan.Folder, candidate), groupAmbiguous)
                        {
                            Tag = v.Type.Id,
                            ForeColor = SystemColors.GrayText,
                            ToolTipText = candidate,
                            UseItemStyleForSubItems = false
                        };
                        row.SubItems.Add(string.Empty);
                        _lvSource.Items.Add(row);
                    }
                }

                foreach (var local in scan.Unregistered)
                {
                    var item = new ListViewItem(Relative(scan.Folder, local.File), groupUnregistered)
                    {
                        ForeColor = SystemColors.GrayText,
                        ToolTipText = local.ClassName + "\r\nImplements IPlugin, directly or through a base, "
                                      + "but nothing among the fetched assemblies registers it.",
                        UseItemStyleForSubItems = false
                    };
                    item.SubItems.Add(local.ClassName).ForeColor = SystemColors.GrayText;
                    _lvSource.Items.Add(item);
                }
            }

            _lvSource.EndUpdate();
        }

        /// <summary>The middle column's status line, or the reason there is nothing to say.</summary>
        private string ScanSummary()
        {
            var scan = _scan;
            if (scan == null)
            {
                var folder = _txtFolder.Text.Trim();
                return folder.Length == 0 ? "Choose the folder your plugin source is in." :
                    !Directory.Exists(folder) ? "No folder at that path." :
                    _typesByAssembly.Count == 0 ? "Load the assemblies to match against." :
                    string.Empty;
            }

            int matched = 0, stale = 0, missing = 0, ambiguous = 0;
            foreach (ListViewItem item in _lvTypes.Items)
            {
                var type = (PluginTypeInfo)item.Tag;
                ClassMatch match;
                if (!scan.Matches.TryGetValue(type.Id, out match))
                {
                    continue;
                }

                switch (match.Kind)
                {
                    case MatchKind.Found:
                        matched++;
                        if (CodeFileWriter.StateOf(match.Code, type.ClassName, Remarks(type), Attributes(type))
                            == CodeFileWriter.WriteState.Stale)
                        {
                            stale++;
                        }

                        break;
                    case MatchKind.NotFound:
                        missing++;
                        break;
                    default:
                        ambiguous++;
                        break;
                }
            }

            var parts = new List<string> { matched + " matched" + (stale > 0 ? " (" + stale + " stale)" : string.Empty) };
            if (missing > 0) parts.Add(missing + " not found");
            if (ambiguous > 0) parts.Add(ambiguous + " ambiguous");
            if (scan.Unregistered.Count > 0) parts.Add(scan.Unregistered.Count + " unregistered");
            return string.Join(" · ", parts);
        }

        /// <summary>
        /// What Write would do right now, for the hint line: the button answers with a number
        /// rather than a click finding out.
        /// </summary>
        private string WritePlan()
        {
            var scan = _scan;
            if (scan == null)
            {
                return string.Empty;
            }

            int writable = 0, missing = 0, ambiguous = 0;
            foreach (ListViewItem item in _lvTypes.CheckedItems)
            {
                var type = (PluginTypeInfo)item.Tag;
                ClassMatch match;
                if (!scan.Matches.TryGetValue(type.Id, out match))
                {
                    continue;
                }

                if (match.Kind == MatchKind.Found) writable++;
                else if (match.Kind == MatchKind.NotFound) missing++;
                else ambiguous++;
            }

            var writeAmbiguous = _chkWriteAmbiguous.Visible && _chkWriteAmbiguous.Checked;
            var total = writable + (writeAmbiguous ? ambiguous : 0);
            var skipped = missing + (writeAmbiguous ? 0 : ambiguous);
            if (total == 0 && skipped == 0)
            {
                return string.Empty;
            }

            var text = "Will write " + total + (total == 1 ? " class" : " classes");
            if (skipped > 0)
            {
                var reasons = new List<string>();
                if (missing > 0) reasons.Add(missing + " no file");
                if (!writeAmbiguous && ambiguous > 0) reasons.Add(ambiguous + " ambiguous");
                text += " · " + skipped + " skipped (" + string.Join(", ", reasons) + ")";
            }

            return text;
        }

        private void LvTypes_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            if (!e.IsSelected || _syncingSelection)
            {
                return;
            }

            var type = e.Item.Tag as PluginTypeInfo;
            if (type != null)
            {
                EchoSelection(_lvSource, item => item.Tag is Guid && (Guid)item.Tag == type.Id);
            }
        }

        private void LvSource_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            if (!e.IsSelected || _syncingSelection || !(e.Item.Tag is Guid))
            {
                return;
            }

            var id = (Guid)e.Item.Tag;
            EchoSelection(_lvTypes, item => ((PluginTypeInfo)item.Tag).Id == id);
        }

        /// <summary>
        /// The other list follows along, so a red row and the class it is about are never
        /// found by reading two lists against each other.
        /// </summary>
        private void EchoSelection(ListView list, Func<ListViewItem, bool> matches)
        {
            _syncingSelection = true;
            try
            {
                list.SelectedItems.Clear();
                foreach (ListViewItem item in list.Items)
                {
                    if (matches(item))
                    {
                        item.Selected = true;
                        item.EnsureVisible();
                        break;
                    }
                }
            }
            finally
            {
                _syncingSelection = false;
            }
        }

        private static string Relative(string folder, string path)
        {
            return path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(folder.Length).TrimStart('\\', '/')
                : path;
        }

        private void BtnWrite_Click(object sender, EventArgs e)
        {
            var folder = _txtFolder.Text.Trim();
            var types = CheckedTypes();
            var writeAmbiguous = _chkWriteAmbiguous.Visible && _chkWriteAmbiguous.Checked;

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
                    var file = CodeFileWriter.FindFile(folder, type.ClassName, type.Namespace, out ambiguous);

                    if (ambiguous != null)
                    {
                        if (!writeAmbiguous)
                        {
                            ambiguousTypes.Add(type.ClassName + " (" + ambiguous.Count + " files)");
                            continue;
                        }

                        // Asked for by name: every file declaring the class gets the same output.
                        // The splice only ever replaces this tool's own block, so the partial-class
                        // case this exists for ends up documented on both halves.
                        foreach (var candidate in ambiguous)
                        {
                            var label = type.ClassName + " (" + Relative(folder, candidate) + ")";
                            if (CodeFileWriter.Update(candidate, type.ClassName, Remarks(type), Attributes(type)))
                            {
                                written.Add(label);
                            }
                            else
                            {
                                unchanged.Add(label);
                            }
                        }

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

            // What was stale is now current, and the marks should say so without being asked.
            StartScan();
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
