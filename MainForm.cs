using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace WorkbenchHost
{
    internal sealed class MainForm : Form
    {
        private sealed class NodeTarget
        {
            internal string Path;
            internal bool IsDirectory;
        }

        private sealed class EditorDocument
        {
            internal string Path;
            internal string DisplayName;
            internal string SavedText;
            internal RichTextBox Editor;
            internal Panel Surface;
            internal Label Breadcrumb;
            internal EditorGutter Gutter;
            internal TabPage Tab;
            internal bool SuppressChanges;
            internal bool IsUntitled;
        }

        private sealed class ApplicationWindow
        {
            internal Process Process;
            internal IntPtr Handle;
        }

        private readonly string root;
        private readonly List<WorkbenchProfile> profiles;
        private WorkbenchProfile profile;
        private readonly WorkspaceState sessionState;
        private string workspaceDirectory;
        private int untitledCounter;
        private readonly Color windowColor = VSCodeColors.Window;
        private readonly Color sidebarColor = VSCodeColors.Sidebar;
        private readonly Color editorColor = VSCodeColors.Editor;
        private readonly Color textColor = VSCodeColors.Text;
        private readonly Color mutedColor = VSCodeColors.TextMuted;
        private readonly Font uiFont = new Font("Segoe UI", 9F);
        private readonly Font codeFont;

        private MenuStrip menu;
        private Panel titleBar;
        private TextBox titleText;
        private TitleBarButton minButton;
        private TitleBarButton maxButton;
        private TitleBarButton closeButton;
        private Panel resizeRightGrip;
        private Panel resizeBottomGrip;
        private Panel resizeCornerGrip;
        private bool resizing;
        private int resizeEdges;
        private Point resizeStartCursor;
        private Rectangle resizeStartBounds;
        private Rectangle resizePreviewBounds;
        private bool resizePreviewVisible;
        private ToolStripLabel opacityLabel;
        private ToolStripMenuItem runButton;
        private ToolStripMenuItem grayscaleButton;
        private TrackBar opacitySlider;
        private StatusStrip status;
        private ToolStripStatusLabel statusText;
        private ToolStripStatusLabel positionStatus;
        private ToolStripStatusLabel encodingStatus;
        private SplitContainer contentSplit;
        private Label explorerTitle;
        private TreeView tree;
        private TabControl tabs;
        private RichTextBox output;
        private System.Windows.Forms.Timer watchTimer;
        private int activityHoverIndex = -1;
        private int closeHoverIndex = -1;
        private bool updatingTabLayout;
        private bool tabOverflowUpdatePending;
        private readonly ToolTip activityTip = new ToolTip();

        private readonly Dictionary<string, EditorDocument> openDocuments = new Dictionary<string, EditorDocument>(StringComparer.OrdinalIgnoreCase);
        private EditorDocument lastCodeDocument;
        private EditorDocument applicationHostDocument;
        private CodePanel applicationHost;
        private Process applicationProcess;
        private IntPtr applicationHandle = IntPtr.Zero;
        private long originalApplicationStyle;
        private long originalApplicationExStyle;
        private long originalApplicationOwner;
        private bool applicationEmbedded;
        private bool applicationOverlay;
        private bool applicationViewVisible;
        private bool grayscaleApplied;
        private bool f10WasDown;
        private bool focusProtection;
        private int focusAwayTicks;
        private int hostingRecoveryAttempts;
        private DateTime lastHostingRecoveryUtc = DateTime.MinValue;
        private bool suppressTabSwitch;
        private bool restoringSession;

        internal MainForm(string rootDirectory, IList<WorkbenchProfile> workbenchProfiles)
        {
            root = rootDirectory;
            profiles = workbenchProfiles == null ? new List<WorkbenchProfile>() : new List<WorkbenchProfile>(workbenchProfiles);
            profile = profiles.Count == 0 ? null : profiles[0];
            workspaceDirectory = profile == null ? root : profile.ResolveWorkspaceDirectory();
            sessionState = WorkspaceState.Load();
            if (!String.IsNullOrWhiteSpace(sessionState.WorkspaceDirectory) && Directory.Exists(sessionState.WorkspaceDirectory))
                workspaceDirectory = Path.GetFullPath(sessionState.WorkspaceDirectory);
            Font candidate = new Font("Cascadia Mono", 10F);
            if (candidate.Name.IndexOf("Cascadia", StringComparison.OrdinalIgnoreCase) < 0)
            {
                candidate.Dispose();
                candidate = new Font("Consolas", 10F);
            }
            codeFont = candidate;
            focusProtection = profile == null || profile.FocusProtection;

            InitializeWindow();
            BuildMenu();
            BuildTitleBar();
            BuildStatus();
            BuildWorkspace();
            BuildEvents();
            Controls.SetChildIndex(titleBar, Controls.Count - 1);
            RestoreSession();
            WriteOutput(profile == null ? "Host initialized in editor-only mode." : "Host initialized with profile: " + profile.Id);
            WriteOutput("Available adapters: " + AdapterNames());
            WriteOutput("Real workspace: " + workspaceDirectory);
            WriteOutput("Activation input: Command Center");
        }

        private void InitializeWindow()
        {
            Text = profiles.Count == 1 ? profile.WindowTitle : "workspace - Code";
            try
            {
                Icon executableIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (executableIcon != null) Icon = (Icon)executableIcon.Clone();
                if (executableIcon != null) executableIcon.Dispose();
            }
            catch { }
            ShowIcon = true;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = windowColor;
            ForeColor = textColor;
            Font = uiFont;
            DoubleBuffered = true;
            MinimumSize = new Size(960, 640);
            Size = new Size(1440, 900);
            StartPosition = FormStartPosition.CenterScreen;
            if (sessionState.WindowWidth >= MinimumSize.Width && sessionState.WindowHeight >= MinimumSize.Height)
            {
                Rectangle savedBounds = new Rectangle(sessionState.WindowX, sessionState.WindowY, sessionState.WindowWidth, sessionState.WindowHeight);
                if (IsVisibleOnAnyScreen(savedBounds))
                {
                    StartPosition = FormStartPosition.Manual;
                    Bounds = savedBounds;
                }
            }
            if (sessionState.Maximized)
            {
                MaximizedBounds = Screen.FromPoint(new Point(Bounds.Left + Bounds.Width / 2, Bounds.Top + Bounds.Height / 2)).WorkingArea;
                WindowState = FormWindowState.Maximized;
            }
            else Bounds = ClampToWorkingArea(Bounds);
            KeyPreview = true;
        }

        private static Rectangle ClampToWorkingArea(Rectangle bounds)
        {
            Screen screen = Screen.FromPoint(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2));
            Rectangle work = screen.WorkingArea;
            int width = Math.Min(bounds.Width, work.Width);
            int height = Math.Min(bounds.Height, work.Height);
            int x = Math.Max(work.Left, Math.Min(bounds.Left, work.Right - width));
            int y = Math.Max(work.Top, Math.Min(bounds.Top, work.Bottom - height));
            return new Rectangle(x, y, width, height);
        }

        private static bool IsVisibleOnAnyScreen(Rectangle bounds)
        {
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle intersection = Rectangle.Intersect(bounds, screen.WorkingArea);
                if (intersection.Width >= 120 && intersection.Height >= 80) return true;
            }
            return false;
        }

        private void BuildMenu()
        {
            menu = new MenuStrip();
            menu.Dock = DockStyle.Fill;
            menu.AutoSize = false;
            menu.Padding = new Padding(4, 6, 4, 6);
            menu.BackColor = VSCodeColors.TitleBar;
            menu.ForeColor = textColor;
            menu.Renderer = new VSCodeToolStripRenderer(VSCodeColors.TitleBar);

            ToolStripMenuItem file = new ToolStripMenuItem("File");
            ToolStripMenuItem edit = new ToolStripMenuItem("Edit");
            ToolStripMenuItem selection = new ToolStripMenuItem("Selection");
            ToolStripMenuItem view = new ToolStripMenuItem("View");
            ToolStripMenuItem go = new ToolStripMenuItem("Go");
            ToolStripMenuItem run = new ToolStripMenuItem("Run");
            ToolStripMenuItem terminal = new ToolStripMenuItem("Terminal");
            ToolStripMenuItem help = new ToolStripMenuItem("Help");
            menu.Items.AddRange(new ToolStripItem[] { file, edit, selection, view, go, run, terminal, help });

            file.DropDownItems.Add(MenuItem("New File", delegate { NewFile(); }, Keys.Control | Keys.N));
            file.DropDownItems.Add(MenuItem("Open File...", delegate { OpenFileDialogForWorkspace(); }, Keys.Control | Keys.O));
            file.DropDownItems.Add(MenuItem("Open Folder...", delegate { OpenFolder(); }, Keys.Control | Keys.K));
            file.DropDownItems.Add(MenuItem("Import Application...", delegate { ImportApplicationProfile(); }, Keys.Control | Keys.Shift | Keys.I));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(MenuItem("Save", delegate { SaveCurrentDocument(); }, Keys.Control | Keys.S));
            file.DropDownItems.Add(MenuItem("Save As...", delegate { SaveCurrentDocumentAs(); }, Keys.Control | Keys.Shift | Keys.S));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(MenuItem("Close Editor", delegate { CloseCurrentTab(); }, Keys.Control | Keys.W));
            file.DropDownItems.Add(MenuItem("Exit", delegate { Close(); }, Keys.None));

            edit.DropDownItems.Add(MenuItem("Undo", delegate { EditCurrent("undo"); }, Keys.Control | Keys.Z));
            edit.DropDownItems.Add(MenuItem("Redo", delegate { EditCurrent("redo"); }, Keys.Control | Keys.Y));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(MenuItem("Cut", delegate { EditCurrent("cut"); }, Keys.Control | Keys.X));
            edit.DropDownItems.Add(MenuItem("Copy", delegate { EditCurrent("copy"); }, Keys.Control | Keys.C));
            edit.DropDownItems.Add(MenuItem("Paste", delegate { EditCurrent("paste"); }, Keys.Control | Keys.V));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(MenuItem("Find...", delegate { FindInCurrent(); }, Keys.Control | Keys.F));
            edit.DropDownItems.Add(MenuItem("Select All", delegate { EditCurrent("selectall"); }, Keys.Control | Keys.A));

            selection.DropDownItems.Add(MenuItem("Select All", delegate { EditCurrent("selectall"); }, Keys.Control | Keys.A));
            go.DropDownItems.Add(MenuItem("Previous Editor", delegate { if (tabs.TabCount > 0) tabs.SelectedIndex = Math.Max(0, tabs.SelectedIndex - 1); }, Keys.None));
            go.DropDownItems.Add(MenuItem("Next Editor", delegate { if (tabs.TabCount > 0) tabs.SelectedIndex = Math.Min(tabs.TabCount - 1, tabs.SelectedIndex + 1); }, Keys.None));
            go.DropDownItems.Add(new ToolStripSeparator());
            go.DropDownItems.Add(MenuItem("Go to Line...", delegate { GoToLine(); }, Keys.Control | Keys.G));

            ToolStripMenuItem toggleOutput = new ToolStripMenuItem("Toggle Output");
            toggleOutput.Click += delegate { contentSplit.Panel2Collapsed = !contentSplit.Panel2Collapsed; };
            ToolStripMenuItem focusItem = new ToolStripMenuItem("Switch to Code on Focus Loss");
            focusItem.CheckOnClick = true;
            focusItem.Checked = focusProtection;
            focusItem.CheckedChanged += delegate
            {
                focusProtection = focusItem.Checked;
                statusText.Text = focusProtection ? "Focus protection enabled" : "Ready";
            };
            view.DropDownItems.Add(toggleOutput);
            view.DropDownItems.Add(focusItem);

            ToolStripMenuItem applicationDisplay = new ToolStripMenuItem("Application Display");
            grayscaleButton = new ToolStripMenuItem("Grayscale");
            grayscaleButton.CheckOnClick = true;
            grayscaleButton.Enabled = profile != null && profile.EnableGrayscale;
            grayscaleButton.ToolTipText = "Toggle low-overhead system grayscale";
            grayscaleButton.CheckedChanged += delegate
            {
                ApplyGrayscaleState();
                if (applicationViewVisible) statusText.Text = grayscaleButton.Checked ? "Application attached - grayscale enabled" : "Application attached - color enabled";
            };

            int initialOpacity = profile == null ? 100 : profile.DefaultOpacity;
            opacityLabel = new ToolStripLabel("Opacity: " + initialOpacity + "%");
            opacityLabel.Enabled = false;
            opacitySlider = new TrackBar();
            opacitySlider.Minimum = 0;
            opacitySlider.Maximum = 100;
            opacitySlider.Value = initialOpacity;
            opacitySlider.TickStyle = TickStyle.None;
            opacitySlider.AutoSize = false;
            opacitySlider.Size = new Size(180, 26);
            opacitySlider.BackColor = VSCodeColors.Dropdown;
            opacitySlider.ValueChanged += delegate
            {
                opacityLabel.Text = "Opacity: " + opacitySlider.Value + "%";
                if (applicationEmbedded && NativeMethods.IsWindow(applicationHandle)) NativeMethods.SetOpacity(applicationHandle, opacitySlider.Value, originalApplicationExStyle);
                if (applicationViewVisible) encodingStatus.Text = "Runtime  " + ApplicationModeName() + "  " + opacitySlider.Value + "%";
            };
            ToolStripControlHost opacityHost = new ToolStripControlHost(opacitySlider);
            opacityHost.AutoSize = false;
            opacityHost.Size = new Size(190, 30);
            applicationDisplay.DropDownItems.Add(grayscaleButton);
            applicationDisplay.DropDownItems.Add(new ToolStripSeparator());
            applicationDisplay.DropDownItems.Add(opacityLabel);
            applicationDisplay.DropDownItems.Add(opacityHost);
            view.DropDownItems.Add(applicationDisplay);

            runButton = new ToolStripMenuItem("Focus Command Center");
            runButton.Enabled = profile != null;
            runButton.Click += delegate { FocusCommandCenter(); };
            ToolStripMenuItem returnCode = new ToolStripMenuItem("Return to Code    F10");
            returnCode.Click += delegate { ShowLastCode(); };
            run.DropDownItems.Add(runButton);
            run.DropDownItems.Add(returnCode);

            ToolStripMenuItem outputItem = new ToolStripMenuItem("Output    Ctrl+J");
            outputItem.Click += delegate { contentSplit.Panel2Collapsed = !contentSplit.Panel2Collapsed; };
            terminal.DropDownItems.Add(outputItem);

            ToolStripMenuItem about = new ToolStripMenuItem("Real editor workspace");
            about.Click += delegate { MessageBox.Show("Application adapters: " + AdapterNames() + "\r\nWorkspace: " + workspaceDirectory, "Workbench Host"); };
            help.DropDownItems.Add(about);

            foreach (ToolStripMenuItem item in menu.Items)
            {
                item.Padding = new Padding(7, 5, 7, 5);
                ApplyMenuTheme(item);
            }
            MainMenuStrip = menu;
        }

        private void ApplyMenuTheme(ToolStripMenuItem item)
        {
            item.ForeColor = textColor;
            item.DropDown.Renderer = new VSCodeToolStripRenderer(VSCodeColors.TitleBar);
            item.DropDown.ForeColor = textColor;
            item.DropDown.BackColor = VSCodeColors.Dropdown;
            foreach (ToolStripItem child in item.DropDownItems)
            {
                child.ForeColor = textColor;
                ToolStripMenuItem sub = child as ToolStripMenuItem;
                if (sub != null) ApplyMenuTheme(sub);
            }
        }

        private void BuildTitleBar()
        {
            titleBar = new Panel();
            titleBar.Dock = DockStyle.Top;
            titleBar.Height = 35;
            titleBar.BackColor = VSCodeColors.TitleBar;
            titleBar.MouseDown += TitleDragMouseDown;
            titleBar.MouseDoubleClick += TitleDragMouseDoubleClick;

            Label icon = new Label();
            icon.Location = new Point(0, 0);
            icon.Size = new Size(28, 35);
            icon.BackColor = VSCodeColors.TitleBar;
            icon.MouseDown += TitleDragMouseDown;
            icon.MouseDoubleClick += TitleDragMouseDoubleClick;
            icon.Paint += delegate(object sender, PaintEventArgs e)
            {
                try
                {
                    Icon appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                    if (appIcon != null) e.Graphics.DrawIcon(appIcon, new Rectangle(6, 9, 16, 16));
                }
                catch { }
            };

            FlowLayoutPanel windowButtons = new FlowLayoutPanel();
            windowButtons.Size = new Size(138, 35);
            windowButtons.FlowDirection = FlowDirection.LeftToRight;
            windowButtons.WrapContents = false;
            windowButtons.Padding = new Padding(0);
            windowButtons.BackColor = VSCodeColors.TitleBar;
            minButton = new TitleBarButton(TitleBarButton.Kind.Minimize);
            maxButton = new TitleBarButton(TitleBarButton.Kind.Maximize);
            closeButton = new TitleBarButton(TitleBarButton.Kind.Close);
            minButton.Margin = new Padding(0);
            maxButton.Margin = new Padding(0);
            closeButton.Margin = new Padding(0);
            minButton.Clicked += delegate { WindowState = FormWindowState.Minimized; };
            maxButton.Clicked += delegate { ToggleMaximize(); };
            closeButton.Clicked += delegate { Close(); };
            windowButtons.Controls.AddRange(new Control[] { minButton, maxButton, closeButton });

            Panel commandCenter = new Panel();
            commandCenter.BackColor = VSCodeColors.Input;
            commandCenter.Cursor = Cursors.IBeam;
            commandCenter.MouseDown += delegate { FocusCommandCenter(); };
            commandCenter.Paint += delegate(object sender, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (Pen pen = new Pen(VSCodeColors.DropdownBorder))
                using (System.Drawing.Drawing2D.GraphicsPath path = RoundedRectangle(commandCenter.ClientRectangle, 6))
                    e.Graphics.DrawPath(pen, path);
                IconPainter.DrawSearch(e.Graphics, new Rectangle(8, 5, 13, 13), VSCodeColors.TextMuted);
            };

            titleText = new TextBox();
            titleText.Dock = DockStyle.None;
            titleText.Text = Path.GetFileName(workspaceDirectory);
            titleText.TextAlign = HorizontalAlignment.Center;
            titleText.ForeColor = VSCodeColors.Text;
            titleText.Font = uiFont;
            titleText.BackColor = VSCodeColors.Input;
            titleText.BorderStyle = BorderStyle.None;
            titleText.Location = new Point(25, 4);
            titleText.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            titleText.Size = new Size(Math.Max(20, commandCenter.Width - 34), titleText.PreferredHeight);
            titleText.KeyDown += CommandCenterKeyDown;
            titleText.Enter += delegate
            {
                if (String.Equals(titleText.Text, WorkspaceCommandText(), StringComparison.Ordinal)) titleText.Clear();
            };
            titleText.Leave += delegate { RestoreCommandCenterText(); };
            commandCenter.Resize += delegate
            {
                titleText.Location = new Point(25, Math.Max(2, (commandCenter.ClientSize.Height - titleText.PreferredHeight) / 2));
                titleText.Size = new Size(Math.Max(20, commandCenter.ClientSize.Width - 34), titleText.PreferredHeight);
            };
            commandCenter.Controls.Add(titleText);

            Panel titleLeft = new Panel();
            titleLeft.Location = new Point(0, 0);
            titleLeft.Size = new Size(450, 35);
            titleLeft.BackColor = VSCodeColors.TitleBar;
            menu.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && menu.GetItemAt(e.Location) == null) BeginTitleDrag();
            };
            titleLeft.Controls.Add(menu);
            titleLeft.Controls.Add(icon);

            titleBar.Controls.Add(titleLeft);
            titleBar.Controls.Add(commandCenter);
            titleBar.Controls.Add(windowButtons);
            titleBar.Resize += delegate
            {
                const int leftWidth = 450;
                const int rightWidth = 138;
                const int gap = 12;
                int available = Math.Max(120, titleBar.ClientSize.Width - leftWidth - rightWidth - gap * 2);
                int commandWidth = Math.Min(600, available);
                int centeredX = (titleBar.ClientSize.Width - commandWidth) / 2;
                int commandX = Math.Max(leftWidth + gap, centeredX);
                commandX = Math.Min(commandX, Math.Max(leftWidth + gap, titleBar.ClientSize.Width - rightWidth - gap - commandWidth));
                titleLeft.Bounds = new Rectangle(0, 0, Math.Min(leftWidth, titleBar.ClientSize.Width), titleBar.ClientSize.Height);
                commandCenter.Bounds = new Rectangle(commandX, 5, commandWidth, 25);
                windowButtons.Location = new Point(Math.Max(0, titleBar.ClientSize.Width - rightWidth), 0);
                windowButtons.Height = titleBar.ClientSize.Height;
            };
            Controls.Add(titleBar);
        }

        private void TitleDragMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) BeginTitleDrag();
        }

        private void TitleDragMouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) ToggleMaximize();
        }

        private void BeginTitleDrag()
        {
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN, new IntPtr(NativeMethods.HTCAPTION), IntPtr.Zero);
        }

        private void ToggleMaximize()
        {
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
            WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            if (WindowState == FormWindowState.Normal) Bounds = ClampToWorkingArea(Bounds);
        }

        private ToolStripMenuItem MenuItem(string text, EventHandler action, Keys shortcut)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += action;
            if (shortcut != Keys.None) item.ShortcutKeys = shortcut;
            return item;
        }

        private void BuildStatus()
        {
            status = new StatusStrip();
            status.Dock = DockStyle.Bottom;
            status.BackColor = VSCodeColors.StatusBar;
            status.ForeColor = Color.White;
            status.SizingGrip = false;
            status.Font = new Font("Segoe UI", 8.25F);
            ToolStripStatusLabel branch = new ToolStripStatusLabel("main");
            ToolStripStatusLabel diagnostics = new ToolStripStatusLabel("0 errors  0 warnings");
            statusText = new ToolStripStatusLabel("Ready");
            statusText.Spring = true;
            statusText.TextAlign = ContentAlignment.MiddleLeft;
            positionStatus = new ToolStripStatusLabel("Ln 1, Col 1");
            encodingStatus = new ToolStripStatusLabel("UTF-8  LF");
            status.Items.AddRange(new ToolStripItem[] { branch, diagnostics, statusText, positionStatus, encodingStatus });
            Controls.Add(status);
        }

        private void BuildWorkspace()
        {
            SplitContainer main = new SplitContainer();
            main.Dock = DockStyle.Fill;
            main.FixedPanel = FixedPanel.Panel1;
            main.SplitterDistance = 300;
            main.SplitterWidth = 1;
            main.Panel1MinSize = 180;
            main.BackColor = Color.FromArgb(43, 43, 43);
            main.Panel1.BackColor = sidebarColor;
            main.Panel2.BackColor = editorColor;

            Panel activity = new Panel();
            activity.Dock = DockStyle.Left;
            activity.Width = 48;
            activity.BackColor = VSCodeColors.ActivityBar;
            activity.Paint += DrawActivityBar;
            activity.MouseMove += ActivityMouseMove;
            activity.Resize += delegate { activity.Invalidate(); };
            activity.MouseLeave += delegate { activityHoverIndex = -1; activity.Invalidate(); };
            activity.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                int index = ActivityIndexAt(activity, e.Y);
                if (index == 0) tree.Focus();
                else if (index == 1) FindInCurrent();
                else if (index == 2) { contentSplit.Panel2Collapsed = false; statusText.Text = "Source Control"; }
                else if (index == 3) FocusCommandCenter();
                else if (index == 4) ImportApplicationProfile();
                else if (index == 5) statusText.Text = "Accounts";
                else if (index == 6) statusText.Text = "Manage";
            };

            Panel explorer = new Panel();
            explorer.Dock = DockStyle.Fill;
            explorer.BackColor = sidebarColor;
            explorerTitle = new Label();
            explorerTitle.Text = "EXPLORER";
            explorerTitle.Dock = DockStyle.Top;
            explorerTitle.Height = 36;
            explorerTitle.Padding = new Padding(12, 11, 0, 0);
            explorerTitle.ForeColor = mutedColor;
            explorerTitle.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            explorerTitle.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(VSCodeColors.Separator))
                    e.Graphics.DrawLine(pen, 0, explorerTitle.Height - 1, explorerTitle.Width, explorerTitle.Height - 1);
                using (SolidBrush dot = new SolidBrush(VSCodeColors.TextMuted))
                {
                    int x = explorerTitle.Width - 24;
                    for (int i = 0; i < 3; i++) e.Graphics.FillEllipse(dot, x + i * 5, 16, 2, 2);
                }
            };

            tree = new TreeView();
            tree.Dock = DockStyle.Fill;
            tree.BorderStyle = BorderStyle.None;
            tree.BackColor = sidebarColor;
            tree.ForeColor = textColor;
            tree.Font = uiFont;
            tree.HideSelection = false;
            tree.ShowLines = false;
            tree.ShowPlusMinus = false;
            tree.ShowRootLines = false;
            tree.FullRowSelect = true;
            tree.Indent = 12;
            tree.ItemHeight = 24;
            tree.DrawMode = TreeViewDrawMode.OwnerDrawText;
            tree.DrawNode += TreeDrawNode;
            tree.HandleCreated += delegate
            {
                NativeMethods.ApplyDarkControlTheme(tree.Handle);
                NativeMethods.SendMessage(tree.Handle, 0x112C, new IntPtr(0x8000), new IntPtr(0x8000));
            };
            explorer.Controls.Add(tree);
            explorer.Controls.Add(explorerTitle);
            main.Panel1.Controls.Add(explorer);
            main.Panel1.Controls.Add(activity);

            contentSplit = new SplitContainer();
            contentSplit.Dock = DockStyle.Fill;
            contentSplit.Orientation = Orientation.Horizontal;
            contentSplit.FixedPanel = FixedPanel.Panel2;
            contentSplit.SplitterWidth = 2;
            contentSplit.Panel1.BackColor = editorColor;
            contentSplit.Panel2.BackColor = windowColor;
            contentSplit.Panel2MinSize = 110;
            contentSplit.Panel2Collapsed = true;

            tabs = new DarkTabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Padding = new Point(10, 3);
            tabs.Font = uiFont;
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.ItemSize = new Size(180, 33);
            tabs.DrawItem += DrawTabItem;
            tabs.HandleCreated += delegate
            {
                if (IsHandleCreated) BeginInvoke((MethodInvoker)UpdateTabLayout);
            };
            tabs.Resize += delegate { UpdateTabLayout(); };
            tabs.ControlAdded += delegate { UpdateTabLayout(); };
            tabs.ControlRemoved += delegate { UpdateTabLayout(); };
            tabs.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                int hover = -1;
                for (int i = 0; i < tabs.TabCount; i++)
                {
                    Rectangle r = tabs.GetTabRect(i);
                    if (e.X >= r.Right - 29 && e.X <= r.Right - 8) hover = i;
                }
                if (hover != closeHoverIndex)
                {
                    closeHoverIndex = hover;
                    tabs.Invalidate();
                }
            };
            tabs.MouseLeave += delegate { closeHoverIndex = -1; tabs.Invalidate(); };

            Panel outputHeader = new Panel();
            outputHeader.Dock = DockStyle.Top;
            outputHeader.Height = 30;
            outputHeader.BackColor = windowColor;
            outputHeader.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(VSCodeColors.Border))
                    e.Graphics.DrawLine(pen, 0, outputHeader.Height - 1, outputHeader.Width, outputHeader.Height - 1);
                using (SolidBrush accent = new SolidBrush(VSCodeColors.Accent))
                    e.Graphics.FillRectangle(accent, 12, outputHeader.Height - 3, 50, 2);
            };
            Label outLabel = new Label();
            outLabel.Text = "OUTPUT";
            outLabel.Location = new Point(12, 7);
            outLabel.Size = new Size(60, 16);
            outLabel.ForeColor = VSCodeColors.TextBright;
            outLabel.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            Label dbgLabel = new Label();
            dbgLabel.Text = "DEBUG CONSOLE";
            dbgLabel.Location = new Point(78, 7);
            dbgLabel.Size = new Size(110, 16);
            dbgLabel.ForeColor = mutedColor;
            dbgLabel.Font = new Font("Segoe UI", 8F);
            Label termLabel = new Label();
            termLabel.Text = "TERMINAL";
            termLabel.Location = new Point(194, 7);
            termLabel.Size = new Size(80, 16);
            termLabel.ForeColor = mutedColor;
            termLabel.Font = new Font("Segoe UI", 8F);
            outputHeader.Controls.AddRange(new Control[] { outLabel, dbgLabel, termLabel });
            output = new RichTextBox();
            output.Dock = DockStyle.Fill;
            output.ReadOnly = true;
            output.BorderStyle = BorderStyle.None;
            output.BackColor = windowColor;
            output.ForeColor = textColor;
            output.Font = codeFont;
            output.HandleCreated += delegate { NativeMethods.ApplyDarkControlTheme(output.Handle); };

            contentSplit.Panel1.Controls.Add(tabs);
            contentSplit.Panel2.Controls.Add(output);
            contentSplit.Panel2.Controls.Add(outputHeader);
            main.Panel2.Controls.Add(contentSplit);
            Controls.Add(main);
            main.BringToFront();
            Shown += delegate
            {
                if (main.Width > 720) main.SplitterDistance = Math.Min(260, main.Width - 400);
            };
            ReloadTree();
        }

        private void UpdateTabLayout()
        {
            if (tabs == null || tabs.IsDisposed || updatingTabLayout) return;
            updatingTabLayout = true;
            try
            {
                int count = Math.Max(1, tabs.TabCount);
                int available = Math.Max(1, tabs.ClientSize.Width - 4);
                int width = Math.Max(92, Math.Min(180, available / count));
                if (tabs.ItemSize.Width != width) tabs.ItemSize = new Size(width, 33);
            }
            finally { updatingTabLayout = false; }

            if (!tabs.IsHandleCreated || tabOverflowUpdatePending) return;
            tabOverflowUpdatePending = true;
            BeginInvoke((MethodInvoker)delegate
            {
                tabOverflowUpdatePending = false;
                if (tabs != null && !tabs.IsDisposed && tabs.IsHandleCreated)
                    NativeMethods.HideTabOverflowButtons(tabs.Handle);
            });
        }

        private void DrawActivityBar(object sender, PaintEventArgs e)
        {
            Control bar = (Control)sender;
            for (int i = 0; i < 7; i++)
            {
                Color color = i == 0 ? VSCodeColors.TextBright : VSCodeColors.ActivityInactive;
                if (activityHoverIndex == i) color = VSCodeColors.TextBright;
                int y = i < 5 ? i * 48 + 12 : bar.Height - (7 - i) * 48 + 12;
                Rectangle icon = new Rectangle(12, y, 24, 24);
                IconPainter.Codicon codicon = IconPainter.Codicon.Files;
                if (i == 1) codicon = IconPainter.Codicon.Search;
                else if (i == 2) codicon = IconPainter.Codicon.SourceControl;
                else if (i == 3) codicon = IconPainter.Codicon.Run;
                else if (i == 4) codicon = IconPainter.Codicon.Extensions;
                else if (i == 5) codicon = IconPainter.Codicon.Account;
                else if (i == 6) codicon = IconPainter.Codicon.Settings;
                IconPainter.DrawCodicon(e.Graphics, icon, codicon, color, 24F);
                if (i == 0)
                {
                    using (SolidBrush b = new SolidBrush(VSCodeColors.Accent)) e.Graphics.FillRectangle(b, 0, 8, 2, 32);
                }
            }
        }

        private void ActivityMouseMove(object sender, MouseEventArgs e)
        {
            int index = ActivityIndexAt((Control)sender, e.Y);
            if (index != activityHoverIndex)
            {
                activityHoverIndex = index;
                string[] names = { "Explorer", "Search", "Source Control", "Run", "Extensions", "Accounts", "Manage" };
                activityTip.SetToolTip((Control)sender, index >= 0 ? names[index] : String.Empty);
                ((Control)sender).Invalidate();
            }
        }

        private static int ActivityIndexAt(Control bar, int y)
        {
            if (y >= 0 && y < 5 * 48) return y / 48;
            if (y >= bar.Height - 96 && y < bar.Height) return 5 + (y - (bar.Height - 96)) / 48;
            return -1;
        }

        private static System.Drawing.Drawing2D.GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();
            int diameter = radius * 2;
            Rectangle arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter - 1;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter - 1;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.X;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void TreeDrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            bool selected = (e.State & TreeNodeStates.Selected) != 0;
            if (selected)
            {
                using (SolidBrush b = new SolidBrush(VSCodeColors.Selected))
                    e.Graphics.FillRectangle(b, 0, e.Bounds.Y, tree.ClientSize.Width, e.Bounds.Height);
            }
            NodeTarget target = e.Node.Tag as NodeTarget;
            bool directory = target != null && target.IsDirectory;
            int iconX = Math.Max(4, e.Bounds.X);
            int textX;
            if (directory)
            {
                Color chevronColor = selected ? VSCodeColors.TextBright : VSCodeColors.TextMuted;
                using (Pen pen = new Pen(chevronColor, 1.2F))
                {
                    if (e.Node.IsExpanded)
                    {
                        e.Graphics.DrawLine(pen, iconX, e.Bounds.Y + 9, iconX + 4, e.Bounds.Y + 13);
                        e.Graphics.DrawLine(pen, iconX + 4, e.Bounds.Y + 13, iconX + 8, e.Bounds.Y + 9);
                    }
                    else
                    {
                        e.Graphics.DrawLine(pen, iconX + 2, e.Bounds.Y + 7, iconX + 6, e.Bounds.Y + 11);
                        e.Graphics.DrawLine(pen, iconX + 6, e.Bounds.Y + 11, iconX + 2, e.Bounds.Y + 15);
                    }
                }
                textX = iconX + 12;
            }
            else
            {
                IconPainter.DrawSetiFile(e.Graphics, new Rectangle(iconX + 1, e.Bounds.Y + 2, 21, 20),
                    target == null ? e.Node.Text : target.Path, selected);
                textX = iconX + 23;
            }

            Color nodeColor = selected ? VSCodeColors.TextBright : e.Node.ForeColor;
            if (nodeColor == Color.Empty || nodeColor == SystemColors.WindowText || nodeColor == Color.Black) nodeColor = textColor;
            TextRenderer.DrawText(e.Graphics, e.Node.Text, e.Node.NodeFont ?? tree.Font,
                new Rectangle(textX, e.Bounds.Y, Math.Max(0, tree.ClientSize.Width - textX - 2), e.Bounds.Height),
                nodeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        private void DrawTabItem(object sender, DrawItemEventArgs e)
        {
            Rectangle r = tabs.GetTabRect(e.Index);
            bool selected = e.Index == tabs.SelectedIndex;
            using (SolidBrush b = new SolidBrush(selected ? editorColor : VSCodeColors.TabInactive))
                e.Graphics.FillRectangle(b, r);
            if (selected)
            {
                using (SolidBrush accent = new SolidBrush(VSCodeColors.Accent))
                    e.Graphics.FillRectangle(accent, r.X, r.Y, r.Width, 2);
            }

            EditorDocument doc = tabs.TabPages[e.Index].Tag as EditorDocument;
            string name = doc == null ? tabs.TabPages[e.Index].Text : doc.DisplayName;
            if (doc != null && doc.Editor.Text != doc.SavedText) name += " *";
            Color fg = selected ? VSCodeColors.TextBright : mutedColor;
            Rectangle textRect = new Rectangle(r.X + 10, r.Y, Math.Max(0, r.Width - 38), r.Height);
            TextRenderer.DrawText(e.Graphics, name, tabs.Font, textRect, fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            Rectangle closeRect = new Rectangle(r.Right - 22, r.Y + (r.Height - 14) / 2, 14, 14);
            IconPainter.DrawClose(e.Graphics, closeRect, closeHoverIndex == e.Index ? VSCodeColors.TextBright : mutedColor);
        }

        private void ReloadTree()
        {
            if (tree == null) return;
            tree.BeginUpdate();
            try
            {
                tree.Nodes.Clear();
                TreeNode rootNode = new TreeNode(Path.GetFileName(workspaceDirectory).ToUpperInvariant());
                rootNode.NodeFont = new Font("Segoe UI", 8F, FontStyle.Bold);
                rootNode.Tag = new NodeTarget { Path = workspaceDirectory, IsDirectory = true };
                tree.Nodes.Add(rootNode);
                PopulateDirectory(rootNode, workspaceDirectory);

                rootNode.Expand();
            }
            finally { tree.EndUpdate(); }
        }

        private void PopulateDirectory(TreeNode parent, string directory)
        {
            try
            {
                DirectoryInfo info = new DirectoryInfo(directory);
                DirectoryInfo[] directories = info.GetDirectories();
                Array.Sort(directories, delegate(DirectoryInfo left, DirectoryInfo right) { return StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name); });
                foreach (DirectoryInfo child in directories)
                {
                    if ((child.Attributes & FileAttributes.Hidden) != 0 || (child.Attributes & FileAttributes.System) != 0) continue;
                    TreeNode node = new TreeNode(child.Name);
                    node.Tag = new NodeTarget { Path = child.FullName, IsDirectory = true };
                    node.Nodes.Add(new TreeNode(""));
                    parent.Nodes.Add(node);
                }

                FileInfo[] files = info.GetFiles();
                Array.Sort(files, delegate(FileInfo left, FileInfo right) { return StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name); });
                foreach (FileInfo child in files)
                {
                    if ((child.Attributes & FileAttributes.Hidden) != 0 || (child.Attributes & FileAttributes.System) != 0) continue;
                    TreeNode node = new TreeNode(child.Name);
                    node.Tag = new NodeTarget { Path = child.FullName };
                    parent.Nodes.Add(node);
                }
            }
            catch (Exception ex)
            {
                WriteOutput("Unable to read folder " + directory + ": " + ex.Message);
            }
        }

        private void BuildEvents()
        {
            HandleCreated += delegate { NativeMethods.ApplyDarkWindowBorder(Handle, VSCodeColors.TitleBar); };
            Move += delegate { if (!resizing) ResizeApplication(); };
            Resize += delegate
            {
                if (WindowState == FormWindowState.Maximized) MaximizedBounds = Screen.FromControl(this).WorkingArea;
                UpdateMaximizeButton();
                if (!resizing) ResizeApplication();
            };
            tree.AfterExpand += delegate(object sender, TreeViewEventArgs e)
            {
                NodeTarget target = e.Node.Tag as NodeTarget;
                if (target == null || !target.IsDirectory || e.Node.Nodes.Count != 1 || e.Node.Nodes[0].Text.Length != 0) return;
                e.Node.Nodes.Clear();
                PopulateDirectory(e.Node, target.Path);
            };
            tree.AfterSelect += delegate(object sender, TreeViewEventArgs e) { OpenTreeTarget(e.Node); };

            tabs.SelectedIndexChanged += delegate
            {
                if (suppressTabSwitch || tabs.SelectedTab == null) return;
                EditorDocument document = tabs.SelectedTab.Tag as EditorDocument;
                if (document == null) return;
                HideApplicationWindow();
                applicationViewVisible = false;
                ApplyGrayscaleState();
                if (applicationHostDocument != null) applicationHostDocument.Editor.Visible = true;
                lastCodeDocument = document;
                UpdatePathStatus(document);
                SaveSessionState();
            };

            tabs.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                for (int i = 0; i < tabs.TabCount; i++)
                {
                    if (!tabs.GetTabRect(i).Contains(e.Location)) continue;
                    Rectangle tabRect = tabs.GetTabRect(i);
                    bool closeClick = e.Button == MouseButtons.Left && e.X >= tabRect.Right - 28;
                    if (e.Button != MouseButtons.Middle && !closeClick) return;
                    tabs.SelectedIndex = i;
                    CloseCurrentTab();
                    break;
                }
            };

            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F10)
                {
                    ShowLastCode();
                    e.SuppressKeyPress = true;
                }
                else if (e.Control && e.KeyCode == Keys.J)
                {
                    contentSplit.Panel2Collapsed = !contentSplit.Panel2Collapsed;
                    e.SuppressKeyPress = true;
                }
                else if (e.Control && e.KeyCode == Keys.S)
                {
                    SaveCurrentDocument();
                    e.SuppressKeyPress = true;
                }
                else if (e.Control && e.KeyCode == Keys.O)
                {
                    OpenFileDialogForWorkspace();
                    e.SuppressKeyPress = true;
                }
                else if (e.Control && e.KeyCode == Keys.N)
                {
                    NewFile();
                    e.SuppressKeyPress = true;
                }
                else if (e.Control && e.KeyCode == Keys.W)
                {
                    CloseCurrentTab();
                    e.SuppressKeyPress = true;
                }
            };

            watchTimer = new System.Windows.Forms.Timer();
            watchTimer.Interval = 120;
            watchTimer.Tick += WatchTimerTick;
            watchTimer.Start();
            Activated += delegate { RepaintCustomChrome(); };
            Deactivate += delegate { RepaintCustomChrome(); };
            FormClosing += MainFormClosing;
            BuildResizeGrips();
        }

        private void BuildResizeGrips()
        {
            resizeRightGrip = CreateResizeGrip(Cursors.SizeWE, 2);
            resizeBottomGrip = CreateResizeGrip(Cursors.SizeNS, 4);
            resizeCornerGrip = CreateResizeGrip(Cursors.SizeNWSE, 6);
            Controls.Add(resizeRightGrip);
            Controls.Add(resizeBottomGrip);
            Controls.Add(resizeCornerGrip);
            Resize += delegate { PositionResizeGrips(); };
            PositionResizeGrips();
        }

        private Panel CreateResizeGrip(Cursor cursor, int edges)
        {
            Panel grip = new Panel();
            grip.BackColor = Color.Transparent;
            grip.Cursor = cursor;
            grip.Tag = edges;
            grip.MouseDown += ResizeGripMouseDown;
            grip.MouseMove += ResizeGripMouseMove;
            grip.MouseUp += ResizeGripMouseUp;
            grip.MouseCaptureChanged += ResizeGripMouseCaptureChanged;
            return grip;
        }

        private void PositionResizeGrips()
        {
            if (resizeRightGrip == null || IsDisposed) return;
            int band = 8;
            resizeRightGrip.Bounds = new Rectangle(Math.Max(0, ClientSize.Width - band), band, band, Math.Max(0, ClientSize.Height - band * 2));
            resizeBottomGrip.Bounds = new Rectangle(band, Math.Max(0, ClientSize.Height - band), Math.Max(0, ClientSize.Width - band * 2), band);
            resizeCornerGrip.Bounds = new Rectangle(Math.Max(0, ClientSize.Width - band), Math.Max(0, ClientSize.Height - band), band, band);
            resizeRightGrip.BringToFront();
            resizeBottomGrip.BringToFront();
            resizeCornerGrip.BringToFront();
        }

        private void ResizeGripMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || WindowState != FormWindowState.Normal) return;
            resizing = true;
            resizeEdges = (int)((Control)sender).Tag;
            resizeStartCursor = Cursor.Position;
            resizeStartBounds = Bounds;
            resizePreviewBounds = resizeStartBounds;
            DrawResizePreview();
            ((Control)sender).Capture = true;
        }

        private void ResizeGripMouseMove(object sender, MouseEventArgs e)
        {
            if (!resizing)
            {
                Cursor.Current = ((Control)sender).Cursor;
                return;
            }
            if (!resizing) return;
            Point cursor = Cursor.Position;
            int dx = cursor.X - resizeStartCursor.X;
            int dy = cursor.Y - resizeStartCursor.Y;
            Rectangle next = resizeStartBounds;
            if ((resizeEdges & 2) != 0) next.Width = resizeStartBounds.Width + dx;
            if ((resizeEdges & 4) != 0) next.Height = resizeStartBounds.Height + dy;
            next.Width = Math.Max(MinimumSize.Width, next.Width);
            next.Height = Math.Max(MinimumSize.Height, next.Height);
            if (next == resizePreviewBounds) return;
            EraseResizePreview();
            resizePreviewBounds = next;
            DrawResizePreview();
        }

        private void ResizeGripMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Rectangle finalBounds = resizePreviewBounds;
            EraseResizePreview();
            resizing = false;
            ((Control)sender).Capture = false;
            Bounds = finalBounds;
            SaveSessionState();
        }

        private void ResizeGripMouseCaptureChanged(object sender, EventArgs e)
        {
            Control grip = (Control)sender;
            if (!resizing || grip.Capture) return;
            EraseResizePreview();
            resizing = false;
        }

        private void DrawResizePreview()
        {
            if (resizePreviewVisible) return;
            ControlPaint.DrawReversibleFrame(resizePreviewBounds, Color.Black, FrameStyle.Thick);
            resizePreviewVisible = true;
        }

        private void EraseResizePreview()
        {
            if (!resizePreviewVisible) return;
            ControlPaint.DrawReversibleFrame(resizePreviewBounds, Color.Black, FrameStyle.Thick);
            resizePreviewVisible = false;
        }

        private int ResizeEdgesAt(Point cursor)
        {
            if (WindowState != FormWindowState.Normal || ClientSize.Width < 1 || ClientSize.Height < 1) return 0;
            Point point = PointToClient(cursor);
            const int band = 8;
            bool right = point.X >= ClientSize.Width - band;
            bool bottom = point.Y >= ClientSize.Height - band;
            if (right && bottom) return 6;
            if (right && point.Y >= band) return 2;
            if (bottom && point.X >= band) return 4;
            return 0;
        }

        private void RepaintCustomChrome()
        {
            if (titleBar == null || titleBar.IsDisposed) return;
            titleBar.Invalidate(true);
            titleBar.Update();
        }

        private void UpdateMaximizeButton()
        {
            if (maxButton != null)
                maxButton.ButtonKind = WindowState == FormWindowState.Maximized ? TitleBarButton.Kind.Restore : TitleBarButton.Kind.Maximize;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x00C00000 | 0x00040000 | 0x00080000 | 0x00020000 | 0x00010000;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0020 && WindowState == FormWindowState.Normal) // WM_SETCURSOR
            {
                int edges = ResizeEdgesAt(Cursor.Position);
                if (edges == 2) Cursor.Current = Cursors.SizeWE;
                else if (edges == 4) Cursor.Current = Cursors.SizeNS;
                else if (edges == 6) Cursor.Current = Cursors.SizeNWSE;
                if (edges != 0)
                {
                    m.Result = new IntPtr(1);
                    return;
                }
            }
            if (m.Msg == 0x0085) // WM_NCPAINT: Windows must not paint over the custom title bar
            {
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == 0x0086) // WM_NCACTIVATE: keep the entire title bar client-drawn
            {
                m.Result = new IntPtr(1);
                if (IsHandleCreated) BeginInvoke((MethodInvoker)RepaintCustomChrome);
                return;
            }
            if (m.Msg == 0x0084 && WindowState == FormWindowState.Normal) // WM_NCHITTEST
            {
                base.WndProc(ref m);
                Point cursor = PointToClient(new Point((short)(m.LParam.ToInt64() & 0xffff), (short)((m.LParam.ToInt64() >> 16) & 0xffff)));
                const int grip = 8;
                bool left = cursor.X < grip;
                bool right = cursor.X >= ClientSize.Width - grip;
                bool top = cursor.Y < grip;
                bool bottom = cursor.Y >= ClientSize.Height - grip;
                if (left && top) m.Result = new IntPtr(13);       // HTTOPLEFT
                else if (right && top) m.Result = new IntPtr(14); // HTTOPRIGHT
                else if (left && bottom) m.Result = new IntPtr(16); // HTBOTTOMLEFT
                else if (right && bottom) m.Result = new IntPtr(17); // HTBOTTOMRIGHT
                else if (left) m.Result = new IntPtr(10);         // HTLEFT
                else if (right) m.Result = new IntPtr(11);        // HTRIGHT
                else if (top) m.Result = new IntPtr(12);          // HTTOP
                else if (bottom) m.Result = new IntPtr(15);       // HTBOTTOM
                return;
            }
            if (m.Msg == 0x0201 && WindowState == FormWindowState.Normal) // WM_LBUTTONDOWN fallback for borderless resize
            {
                Point cursor = PointToClient(Cursor.Position);
                const int grip = 8;
                bool left = cursor.X < grip;
                bool right = cursor.X >= ClientSize.Width - grip;
                bool top = cursor.Y < grip;
                bool bottom = cursor.Y >= ClientSize.Height - grip;
                int sizingEdge = 0;
                if (left && top) sizingEdge = 4;          // WMSZ_TOPLEFT
                else if (right && top) sizingEdge = 5;   // WMSZ_TOPRIGHT
                else if (left && bottom) sizingEdge = 7; // WMSZ_BOTTOMLEFT
                else if (right && bottom) sizingEdge = 8;// WMSZ_BOTTOMRIGHT
                else if (left) sizingEdge = 1;            // WMSZ_LEFT
                else if (right) sizingEdge = 2;           // WMSZ_RIGHT
                else if (top) sizingEdge = 3;             // WMSZ_TOP
                else if (bottom) sizingEdge = 6;          // WMSZ_BOTTOM
                if (sizingEdge != 0)
                {
                    NativeMethods.ReleaseCapture();
                    NativeMethods.SendMessage(Handle, 0x0112, new IntPtr(0xF000 | sizingEdge), IntPtr.Zero); // WM_SYSCOMMAND/SC_SIZE
                    return;
                }
            }
            if (m.Msg == 0x0083) // WM_NCCALCSIZE: remove the standard border while keeping resize support
            {
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        private void OpenTreeTarget(TreeNode node)
        {
            NodeTarget target = node == null ? null : node.Tag as NodeTarget;
            if (target == null || target.IsDirectory) return;
            OpenCodeFile(target.Path, Path.GetFileName(target.Path));
        }

        private void OpenCodeFile(string path, string displayName)
        {
            string fullPath = Path.GetFullPath(path);
            EditorDocument existing;
            if (openDocuments.TryGetValue(fullPath, out existing))
            {
                tabs.SelectedTab = existing.Tab;
                return;
            }

            string text;
            try
            {
                FileInfo file = new FileInfo(fullPath);
                if (file.Length > 5 * 1024 * 1024) throw new InvalidDataException("This file is larger than the 5 MB editor limit.");
                text = File.ReadAllText(fullPath);
                if (text.IndexOf('\0') >= 0) throw new InvalidDataException("This is a binary file and cannot be opened in the text editor.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Unable to open file", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            EditorDocument document = new EditorDocument();
            document.Path = fullPath;
            document.DisplayName = displayName;
            document.Editor = NewEditor(false);
            document.SuppressChanges = true;
            document.Editor.Text = text;
            HighlightEditor(document.Editor, Path.GetExtension(fullPath).ToLowerInvariant());
            document.Tab = new TabPage(displayName + "  x");
            document.Tab.Tag = document;
            document.Tab.BackColor = editorColor;
            BuildEditorSurface(document);
            document.Tab.Controls.Add(document.Surface);
            tabs.TabPages.Add(document.Tab);
            IntPtr editorHandle = document.Editor.Handle; // RichEdit normalizes line endings when its native handle is created.
            document.SavedText = document.Editor.Text;
            document.Editor.Modified = false;
            document.SuppressChanges = false;
            document.Editor.SelectionChanged += delegate { UpdateCursorStatus(document.Editor); if (document.Gutter != null) document.Gutter.Invalidate(); };
            document.Editor.TextChanged += delegate { UpdateDocumentTitle(document); };
            openDocuments[fullPath] = document;
            tabs.SelectedTab = document.Tab;
            lastCodeDocument = document;
            UpdatePathStatus(document);
            SaveSessionState();
        }

        private void RestoreSession()
        {
            restoringSession = true;
            try
            {
                foreach (string path in sessionState.OpenFiles)
                {
                    if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                    OpenCodeFile(path, Path.GetFileName(path));
                }

                if (!String.IsNullOrWhiteSpace(sessionState.ActiveFile))
                {
                    EditorDocument active;
                    string activePath = Path.GetFullPath(sessionState.ActiveFile);
                    if (openDocuments.TryGetValue(activePath, out active)) tabs.SelectedTab = active.Tab;
                }
            }
            finally { restoringSession = false; }
            SaveSessionState();
        }

        private RichTextBox NewEditor(bool readOnly)
        {
            RichTextBox editor = new RichTextBox();
            editor.Dock = DockStyle.Fill;
            editor.BorderStyle = BorderStyle.None;
            editor.BackColor = editorColor;
            editor.ForeColor = textColor;
            editor.Font = codeFont;
            editor.ReadOnly = readOnly;
            editor.WordWrap = false;
            editor.AcceptsTab = true;
            editor.DetectUrls = false;
            editor.ScrollBars = RichTextBoxScrollBars.Both;
            editor.HandleCreated += delegate { NativeMethods.ApplyDarkControlTheme(editor.Handle); };
            return editor;
        }

        private void BuildEditorSurface(EditorDocument document)
        {
            document.Surface = new Panel();
            document.Surface.Dock = DockStyle.Fill;
            document.Surface.BackColor = editorColor;

            document.Breadcrumb = new Label();
            document.Breadcrumb.Dock = DockStyle.Top;
            document.Breadcrumb.Height = 25;
            document.Breadcrumb.Padding = new Padding(12, 5, 0, 0);
            document.Breadcrumb.BackColor = VSCodeColors.Editor;
            document.Breadcrumb.ForeColor = VSCodeColors.TextMuted;
            document.Breadcrumb.Font = new Font("Segoe UI", 8.5F);
            document.Breadcrumb.Text = BreadcrumbText(document);
            document.Breadcrumb.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(VSCodeColors.Border)) e.Graphics.DrawLine(pen, 0, document.Breadcrumb.Height - 1, document.Breadcrumb.Width, document.Breadcrumb.Height - 1);
            };

            Panel editorBody = new Panel();
            editorBody.Dock = DockStyle.Fill;
            editorBody.BackColor = editorColor;
            document.Gutter = new EditorGutter();
            document.Gutter.Editor = document.Editor;
            editorBody.Controls.Add(document.Editor);
            editorBody.Controls.Add(document.Gutter);
            document.Surface.Controls.Add(editorBody);
            document.Surface.Controls.Add(document.Breadcrumb);
        }

        private string BreadcrumbText(EditorDocument document)
        {
            if (document == null || String.IsNullOrEmpty(document.Path)) return document == null ? String.Empty : document.DisplayName;
            string relative = RelativePath(document.Path).Replace("\\", "  >  ");
            return relative;
        }

        private string WorkspaceCommandText()
        {
            string name = Path.GetFileName(workspaceDirectory.TrimEnd(Path.DirectorySeparatorChar));
            return String.IsNullOrWhiteSpace(name) ? "WorkbenchHost" : name;
        }

        private void RestoreCommandCenterText()
        {
            if (titleText == null || titleText.Focused) return;
            titleText.Text = WorkspaceCommandText();
            titleText.ForeColor = VSCodeColors.Text;
        }

        private void FocusCommandCenter()
        {
            if (titleText == null) return;
            titleText.Focus();
            titleText.SelectAll();
        }

        private void CommandCenterKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                titleText.Text = WorkspaceCommandText();
                titleText.ForeColor = VSCodeColors.Text;
                if (tabs.SelectedTab != null) tabs.Focus();
                e.SuppressKeyPress = true;
                return;
            }
            if (e.KeyCode != Keys.Enter) return;
            string command = titleText.Text.Trim();
            WorkbenchProfile selectedProfile = null;
            foreach (WorkbenchProfile candidate in profiles)
            {
                if (String.IsNullOrWhiteSpace(candidate.ActivationPhrase)) continue;
                if (String.Equals(command, candidate.ActivationPhrase.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    selectedProfile = candidate;
                    break;
                }
            }
            e.SuppressKeyPress = true;
            if (selectedProfile == null)
            {
                titleText.ForeColor = Color.FromArgb(244, 135, 113);
                statusText.Text = String.IsNullOrWhiteSpace(command) ? "Enter an application command" : "Command not found";
                titleText.SelectAll();
                return;
            }
            titleText.Text = WorkspaceCommandText();
            titleText.ForeColor = VSCodeColors.Text;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    SelectApplicationProfile(selectedProfile);
                    ActivateApplication();
                });
            }
            catch { }
        }

        private void ShowCodeView()
        {
            HideApplicationWindow();
            applicationViewVisible = false;
            ApplyGrayscaleState();
            EditorDocument document = applicationHostDocument;
            if (document != null && tabs.TabPages.Contains(document.Tab))
            {
                document.Editor.Visible = true;
                document.Editor.BringToFront();
                suppressTabSwitch = true;
                tabs.SelectedTab = document.Tab;
                suppressTabSwitch = false;
                lastCodeDocument = document;
                UpdatePathStatus(document);
            }
            statusText.Text = "Ready";
            if (ContainsFocus && document != null) document.Editor.Focus();
        }

        private void ActivateApplication()
        {
            EditorDocument document = CurrentDocument();
            if (document == null)
            {
                NewFile();
                document = CurrentDocument();
            }
            if (document == null) return;
            suppressTabSwitch = true;
            tabs.SelectedTab = document.Tab;
            suppressTabSwitch = false;
            applicationHost = EnsureApplicationHost(document);
            document.Editor.Visible = false;
            applicationHost.Visible = true;
            applicationHost.BringToFront();
            applicationHost.Invalidate();
            applicationViewVisible = true;
            encodingStatus.Text = "Runtime  Auto  " + opacitySlider.Value + "%";
            statusText.Text = "Waiting for application window...";

            try
            {
                StartAndEmbedApplication();
                NativeMethods.ShowWindow(applicationHandle, NativeMethods.SW_SHOW);
                ResizeApplication();
                NativeMethods.SetOpacity(applicationHandle, opacitySlider.Value, originalApplicationExStyle);
                ApplyGrayscaleState();
                encodingStatus.Text = "Runtime  " + ApplicationModeName() + "  " + opacitySlider.Value + "%";
                statusText.Text = applicationOverlay
                    ? "Application attached in compatible overlay mode - F10 returns to code"
                    : "Application embedded - F10 returns to code";
            }
            catch (Exception ex)
            {
                WriteOutput("ERROR: " + ex.Message);
                contentSplit.Panel2Collapsed = false;
                statusText.Text = "Application failed to attach";
                MessageBox.Show(ex.Message, "Unable to open application", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ShowCodeView();
            }
        }

        private void SelectApplicationProfile(WorkbenchProfile selectedProfile)
        {
            if (selectedProfile == null || Object.ReferenceEquals(profile, selectedProfile)) return;
            CloseApplicationWithHost();
            if (applicationProcess != null) applicationProcess.Dispose();
            applicationProcess = null;
            applicationHandle = IntPtr.Zero;
            applicationEmbedded = false;
            applicationOverlay = false;
            hostingRecoveryAttempts = 0;
            lastHostingRecoveryUtc = DateTime.MinValue;
            applicationViewVisible = false;
            grayscaleButton.Checked = false;
            ApplyGrayscaleState();
            profile = selectedProfile;
            focusProtection = profile.FocusProtection;
            runButton.Enabled = true;
            grayscaleButton.Enabled = profile.EnableGrayscale;
            if (!profile.EnableGrayscale) grayscaleButton.Checked = false;
            opacitySlider.Value = profile.DefaultOpacity;
            WriteOutput("Selected adapter: " + profile.Id);
        }

        private CodePanel EnsureApplicationHost(EditorDocument document)
        {
            if (applicationHost == null)
            {
                applicationHost = new CodePanel();
                applicationHost.Dock = DockStyle.Fill;
                applicationHost.Visible = false;
                applicationHost.CodeFont = codeFont;
                applicationHost.Resize += delegate { if (!resizing) ResizeApplication(); };
            }
            if (applicationHost.Parent != document.Tab) document.Tab.Controls.Add(applicationHost);
            applicationHost.SourceEditor = document.Editor;
            applicationHostDocument = document;
            return applicationHost;
        }

        private void StartAndEmbedApplication()
        {
            if (applicationEmbedded && NativeMethods.IsWindow(applicationHandle)) return;

            ApplicationWindow candidate = profile.AttachExisting ? FindExistingApplicationWindow() : null;
            HashSet<IntPtr> previousWindows = NativeMethods.SnapshotTopLevelWindows();
            if (candidate == null)
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = profile.ResolveApplicationPath(profile.Executable);
                info.WorkingDirectory = profile.ResolveApplicationPath(profile.WorkingDirectory);
                info.Arguments = profile.Arguments;
                info.UseShellExecute = false;
                applicationProcess = Process.Start(info);
                WriteOutput("Starting application process...");
            }
            else
            {
                applicationProcess = candidate.Process;
                applicationHandle = candidate.Handle;
                WriteOutput("Attached to existing process " + candidate.Process.Id + ".");
            }

            DateTime deadline = DateTime.UtcNow.AddSeconds(profile.LaunchTimeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                Process windowProcess = null;
                if (applicationProcess != null)
                {
                    try
                    {
                        applicationProcess.Refresh();
                        if (!applicationProcess.HasExited)
                        {
                            IntPtr handle = NativeMethods.FindTopLevelWindow((uint)applicationProcess.Id, profile.WindowClass);
                            if (handle != IntPtr.Zero)
                            {
                                applicationHandle = handle;
                                windowProcess = applicationProcess;
                            }
                        }
                    }
                    catch { }
                }
                if (windowProcess == null)
                {
                    ApplicationWindow found = FindExistingApplicationWindow();
                    if (found != null)
                    {
                        applicationProcess = found.Process;
                        applicationHandle = found.Handle;
                        windowProcess = found.Process;
                    }
                }
                if (windowProcess == null)
                {
                    uint discoveredProcessId;
                    IntPtr discovered = NativeMethods.FindNewTopLevelWindow(previousWindows, (uint)Process.GetCurrentProcess().Id, profile.WindowClass, out discoveredProcessId);
                    if (discovered != IntPtr.Zero)
                    {
                        try
                        {
                            Process discoveredProcess = Process.GetProcessById((int)discoveredProcessId);
                            if (applicationProcess != null && applicationProcess.Id != discoveredProcess.Id) applicationProcess.Dispose();
                            applicationProcess = discoveredProcess;
                            applicationHandle = discovered;
                            windowProcess = discoveredProcess;
                            WriteOutput("Automatically selected window from process " + discoveredProcess.ProcessName + " (PID " + discoveredProcess.Id + ").");
                        }
                        catch { }
                    }
                }
                if (windowProcess != null) break;
                Application.DoEvents();
                Thread.Sleep(100);
            }
            if (applicationHandle == IntPtr.Zero) throw new TimeoutException("Application window was not available within " + profile.LaunchTimeoutSeconds + " seconds.");

            originalApplicationOwner = NativeMethods.GetStyle(applicationHandle, NativeMethods.GWL_HWNDPARENT);
            applicationOverlay = !NativeMethods.TryEmbed(applicationHandle, applicationHost.Handle, out originalApplicationStyle, out originalApplicationExStyle);
            if (applicationOverlay)
            {
                NativeMethods.PrepareOverlay(applicationHandle, originalApplicationStyle, originalApplicationExStyle, Handle);
                WriteOutput("Native embedding was rejected; using compatible overlay mode.");
            }
            applicationEmbedded = true;
            if (!applicationOverlay) NativeMethods.HideOtherTopLevelWindows((uint)applicationProcess.Id, applicationHandle);
            ResizeApplication();
            WriteOutput("Application attached in " + ApplicationModeName().ToLowerInvariant() + " mode (PID " + applicationProcess.Id + ").");
        }

        private ApplicationWindow FindExistingApplicationWindow()
        {
            Process[] processes = Process.GetProcessesByName(profile.ProcessName);
            foreach (Process process in processes)
            {
                try
                {
                    process.Refresh();
                    if (process.HasExited) continue;
                    IntPtr handle = NativeMethods.FindTopLevelWindow((uint)process.Id, profile.WindowClass);
                    if (handle == IntPtr.Zero && String.IsNullOrWhiteSpace(profile.WindowClass)) handle = process.MainWindowHandle;
                    if (handle != IntPtr.Zero) return new ApplicationWindow { Process = process, Handle = handle };
                    process.Dispose();
                }
                catch
                {
                    process.Dispose();
                }
            }
            return null;
        }

        private void ResizeApplication()
        {
            if (!applicationEmbedded || !NativeMethods.IsWindow(applicationHandle)) return;
            if (applicationOverlay)
            {
                if (WindowState == FormWindowState.Minimized)
                {
                    NativeMethods.ShowWindow(applicationHandle, NativeMethods.SW_HIDE);
                    return;
                }
                if (!applicationViewVisible) return;
                NativeMethods.ShowWindow(applicationHandle, NativeMethods.SW_SHOW);
                NativeMethods.PositionOverlay(applicationHandle, applicationHost.RectangleToScreen(applicationHost.ClientRectangle));
            }
            else NativeMethods.Resize(applicationHandle, applicationHost.ClientSize.Width, applicationHost.ClientSize.Height);
        }

        private string ApplicationModeName()
        {
            return applicationOverlay ? "Overlay" : "Embedded";
        }

        private void HideApplicationWindow()
        {
            if (applicationEmbedded && NativeMethods.IsWindow(applicationHandle)) NativeMethods.ShowWindow(applicationHandle, NativeMethods.SW_HIDE);
            if (applicationHost != null) applicationHost.Visible = false;
        }

        private void ShowLastCode()
        {
            if (applicationViewVisible)
            {
                ShowCodeView();
                return;
            }
            if (lastCodeDocument != null && tabs.TabPages.Contains(lastCodeDocument.Tab)) tabs.SelectedTab = lastCodeDocument.Tab;
            else ShowCodeView();
            Activate();
        }

        private void CloseCurrentTab()
        {
            TabPage tab = tabs.SelectedTab;
            if (tab == null) return;
            EditorDocument document = tab.Tag as EditorDocument;
            if (document == null) return;
            if (!ConfirmCloseDocument(document)) return;
            if (document == applicationHostDocument && applicationViewVisible) ShowCodeView();
            if (document == applicationHostDocument && applicationHost != null)
            {
                applicationHost.Parent = null;
                applicationHostDocument = null;
            }
            if (!String.IsNullOrEmpty(document.Path)) openDocuments.Remove(document.Path);
            tabs.TabPages.Remove(tab);
            tab.Dispose();
            lastCodeDocument = tabs.SelectedTab == null ? null : tabs.SelectedTab.Tag as EditorDocument;
            SaveSessionState();
        }

        private bool ConfirmCloseDocument(EditorDocument document)
        {
            if (document == null || document.Editor.Text == document.SavedText) return true;
            DialogResult result = MessageBox.Show("Save changes to " + document.DisplayName + "?", "Unsaved changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (result == DialogResult.Cancel) return false;
            if (result == DialogResult.Yes) return SaveDocument(document);
            return true;
        }

        private void NewFile()
        {
            untitledCounter++;
            EditorDocument document = new EditorDocument();
            document.DisplayName = "Untitled-" + untitledCounter;
            document.SavedText = String.Empty;
            document.IsUntitled = true;
            document.Editor = NewEditor(false);
            document.Editor.TextChanged += delegate { UpdateDocumentTitle(document); };
            document.Editor.SelectionChanged += delegate { UpdateCursorStatus(document.Editor); if (document.Gutter != null) document.Gutter.Invalidate(); };
            document.Tab = new TabPage(document.DisplayName + "  x");
            document.Tab.Tag = document;
            document.Tab.BackColor = editorColor;
            BuildEditorSurface(document);
            document.Tab.Controls.Add(document.Surface);
            tabs.TabPages.Add(document.Tab);
            tabs.SelectedTab = document.Tab;
            lastCodeDocument = document;
            UpdatePathStatus(document);
            statusText.Text = "New unsaved file";
        }

        private void OpenFileDialogForWorkspace()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.InitialDirectory = workspaceDirectory;
            dialog.Filter = "All files|*.*|Go files|*.go|Source files|*.cs;*.lua;*.json;*.ps1;*.md|Text files|*.txt";
            if (dialog.ShowDialog(this) == DialogResult.OK) OpenCodeFile(dialog.FileName, Path.GetFileName(dialog.FileName));
            dialog.Dispose();
        }

        private void OpenFolder()
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.Description = "Choose a real code workspace folder";
            dialog.SelectedPath = workspaceDirectory;
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                workspaceDirectory = Path.GetFullPath(dialog.SelectedPath);
                ReloadTree();
                titleText.Text = WorkspaceCommandText();
                explorerTitle.Text = "EXPLORER";
                statusText.Text = "Workspace opened: " + workspaceDirectory;
                SaveSessionState();
            }
            dialog.Dispose();
        }

        private void ImportApplicationProfile()
        {
            OpenFileDialog picker = new OpenFileDialog();
            picker.Title = "Select application executable";
            picker.Filter = "Windows applications|*.exe|All files|*.*";
            if (picker.ShowDialog(this) != DialogResult.OK)
            {
                picker.Dispose();
                return;
            }
            string executable = picker.FileName;
            picker.Dispose();

            QuickImportDialog dialog = new QuickImportDialog(executable);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                dialog.Dispose();
                return;
            }
            ImportedProfileSettings settings = dialog.Settings;
            dialog.Dispose();

            foreach (WorkbenchProfile existing in profiles)
            {
                if (!String.Equals(existing.ActivationPhrase, settings.ActivationPhrase, StringComparison.OrdinalIgnoreCase)) continue;
                MessageBox.Show("Another profile already uses this activation phrase.", "Import Application", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                string profilePath = ApplicationProfileImporter.Import(root, settings);
                WorkbenchProfile imported = WorkbenchProfile.Load(profilePath, root);
                profiles.Add(imported);
                runButton.Enabled = true;
                OpenCodeFile(profilePath, Path.GetFileName(profilePath));
                statusText.Text = "Application profile imported: " + imported.DisplayName;
                WriteOutput("Imported adapter: " + imported.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Unable to import application", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private EditorDocument CurrentDocument()
        {
            return tabs == null || tabs.SelectedTab == null ? null : tabs.SelectedTab.Tag as EditorDocument;
        }

        private void EditCurrent(string operation)
        {
            EditorDocument document = CurrentDocument();
            if (document == null || document.Editor == null) return;
            switch (operation)
            {
                case "undo": if (document.Editor.CanUndo) document.Editor.Undo(); break;
                case "redo": document.Editor.Redo(); break;
                case "cut": document.Editor.Cut(); break;
                case "copy": document.Editor.Copy(); break;
                case "paste": document.Editor.Paste(); break;
                case "selectall": document.Editor.SelectAll(); break;
            }
        }

        private void FindInCurrent()
        {
            EditorDocument document = CurrentDocument();
            if (document == null) return;
            string query = PromptForText("Find", "Find in " + document.DisplayName + ":", document.Editor.SelectedText);
            if (String.IsNullOrEmpty(query)) return;
            int start = document.Editor.SelectionStart + document.Editor.SelectionLength;
            int index = document.Editor.Text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0 && start > 0) index = document.Editor.Text.IndexOf(query, 0, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                statusText.Text = "No results for: " + query;
                return;
            }
            document.Editor.Select(index, query.Length);
            document.Editor.ScrollToCaret();
            document.Editor.Focus();
            statusText.Text = "Match found";
        }

        private void GoToLine()
        {
            EditorDocument document = CurrentDocument();
            if (document == null) return;
            string value = PromptForText("Go to Line", "Line number:", String.Empty);
            int line;
            if (!Int32.TryParse(value, out line) || line < 1 || line > document.Editor.Lines.Length)
            {
                if (!String.IsNullOrEmpty(value)) statusText.Text = "Invalid line number";
                return;
            }
            int index = document.Editor.GetFirstCharIndexFromLine(line - 1);
            document.Editor.Select(Math.Max(0, index), 0);
            document.Editor.ScrollToCaret();
            document.Editor.Focus();
        }

        private static void StyleDialogButton(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = primary ? VSCodeColors.Accent : VSCodeColors.Hover;
            button.BackColor = primary ? VSCodeColors.AccentDark : VSCodeColors.Input;
            button.ForeColor = VSCodeColors.TextBright;
        }

        private string PromptForText(string title, string labelText, string initialValue)
        {
            Form dialog = new Form();
            dialog.Text = title;
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
            dialog.MinimizeBox = false;
            dialog.MaximizeBox = false;
            dialog.ShowInTaskbar = false;
            dialog.ClientSize = new Size(420, 118);
            dialog.BackColor = sidebarColor;
            dialog.ForeColor = textColor;

            Label label = new Label();
            label.Text = labelText;
            label.Location = new Point(12, 12);
            label.AutoSize = true;
            label.ForeColor = textColor;
            TextBox input = new TextBox();
            input.Text = initialValue ?? String.Empty;
            input.Location = new Point(12, 36);
            input.Width = 396;
            input.BackColor = VSCodeColors.Input;
            input.ForeColor = VSCodeColors.TextBright;
            input.BorderStyle = BorderStyle.FixedSingle;
            Button ok = new Button();
            ok.Text = "OK";
            ok.DialogResult = DialogResult.OK;
            ok.Location = new Point(252, 76);
            ok.Width = 75;
            StyleDialogButton(ok, true);
            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(333, 76);
            cancel.Width = 75;
            StyleDialogButton(cancel, false);
            dialog.Controls.AddRange(new Control[] { label, input, ok, cancel });
            dialog.AcceptButton = ok;
            dialog.CancelButton = cancel;
            DialogResult result = dialog.ShowDialog(this);
            string value = result == DialogResult.OK ? input.Text : null;
            dialog.Dispose();
            return value;
        }

        private void SaveCurrentDocument()
        {
            EditorDocument document = CurrentDocument();
            if (document != null) SaveDocument(document);
        }

        private void SaveCurrentDocumentAs()
        {
            EditorDocument document = CurrentDocument();
            if (document != null) SaveDocumentAs(document);
        }

        private bool SaveDocument(EditorDocument document)
        {
            if (document == null) return true;
            if (String.IsNullOrEmpty(document.Path)) return SaveDocumentAs(document);
            try
            {
                File.WriteAllText(document.Path, document.Editor.Text, new UTF8Encoding(false));
                document.SavedText = document.Editor.Text;
                document.Editor.Modified = false;
                UpdateDocumentTitle(document);
                statusText.Text = "Saved " + document.Path;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Unable to save file", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private bool SaveDocumentAs(EditorDocument document)
        {
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.InitialDirectory = workspaceDirectory;
            dialog.FileName = document.DisplayName;
            dialog.Filter = "All files|*.*|Go files|*.go|Text files|*.txt";
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                dialog.Dispose();
                return false;
            }
            string newPath = Path.GetFullPath(dialog.FileName);
            dialog.Dispose();
            try
            {
                File.WriteAllText(newPath, document.Editor.Text, new UTF8Encoding(false));
                if (!String.IsNullOrEmpty(document.Path)) openDocuments.Remove(document.Path);
                document.Path = newPath;
                document.DisplayName = Path.GetFileName(newPath);
                document.IsUntitled = false;
                document.SavedText = document.Editor.Text;
                document.Editor.Modified = false;
                openDocuments[newPath] = document;
                if (document.Breadcrumb != null) document.Breadcrumb.Text = BreadcrumbText(document);
                UpdateDocumentTitle(document);
                UpdatePathStatus(document);
                ReloadTree();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Unable to save file", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void UpdateDocumentTitle(EditorDocument document)
        {
            if (document == null || document.Tab == null) return;
            bool dirty = document.Editor.Text != document.SavedText;
            document.Tab.Text = (dirty ? "*" : "") + document.DisplayName + "  x";
        }

        private void UpdatePathStatus(EditorDocument document)
        {
            if (document == null) return;
            string extension = Path.GetExtension(document.Path ?? document.DisplayName).TrimStart('.').ToUpperInvariant();
            encodingStatus.Text = "UTF-8  LF  " + extension;
            UpdateCursorStatus(document.Editor);
            statusText.Text = document.Path == null ? "New unsaved file" : "Ready";
        }

        private void ApplyGrayscaleState()
        {
            bool shouldApply = profile != null && profile.EnableGrayscale && grayscaleButton.Checked && applicationViewVisible && applicationEmbedded;
            if (shouldApply == grayscaleApplied) return;
            bool result = NativeMethods.SetFullscreenGrayscale(shouldApply);
            if (shouldApply && !result)
            {
                grayscaleButton.Checked = false;
                statusText.Text = "System grayscale is unavailable";
                return;
            }
            grayscaleApplied = shouldApply;
        }

        private void WatchTimerTick(object sender, EventArgs e)
        {
            bool f10Down = (NativeMethods.GetAsyncKeyState(0x79) & 0x8000) != 0;
            if (f10Down && !f10WasDown) ShowLastCode();
            f10WasDown = f10Down;

            if (focusProtection && applicationViewVisible && Visible)
            {
                uint foregroundPid = NativeMethods.WindowProcessId(NativeMethods.GetForegroundWindow());
                uint applicationPid = applicationProcess != null && !applicationProcess.HasExited ? (uint)applicationProcess.Id : 0;
                bool inside = foregroundPid == (uint)Process.GetCurrentProcess().Id || (applicationPid != 0 && foregroundPid == applicationPid);
                if (inside) focusAwayTicks = 0;
                else if (++focusAwayTicks >= 3)
                {
                    focusAwayTicks = 0;
                    ShowCodeView();
                }
            }
            else focusAwayTicks = 0;

            if (applicationProcess != null && applicationEmbedded)
            {
                try
                {
                    if (!applicationOverlay && applicationHost != null && !NativeMethods.IsEmbeddedIn(applicationHandle, applicationHost.Handle))
                    {
                        RecoverApplicationWindow();
                    }
                    if (applicationOverlay && applicationViewVisible) ResizeApplication();
                    if (!applicationProcess.HasExited) return;
                    applicationEmbedded = false;
                    applicationOverlay = false;
                    applicationHandle = IntPtr.Zero;
                    statusText.Text = "Application process exited";
                    WriteOutput("Application process exited.");
                    ShowCodeView();
                }
                catch { }
            }
        }

        private void RecoverApplicationWindow()
        {
            if (applicationProcess == null || applicationHost == null) return;

            IntPtr candidate = NativeMethods.FindTopLevelWindow((uint)applicationProcess.Id, profile.WindowClass);
            if (candidate == IntPtr.Zero && NativeMethods.IsWindow(applicationHandle)) candidate = applicationHandle;
            if (candidate == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.ShowWindow(candidate, NativeMethods.SW_HIDE);
            long recoveredOwner = NativeMethods.GetStyle(candidate, NativeMethods.GWL_HWNDPARENT);
            long recoveredStyle;
            long recoveredExStyle;
            DateTime now = DateTime.UtcNow;
            if ((now - lastHostingRecoveryUtc).TotalSeconds > 5) hostingRecoveryAttempts = 0;
            lastHostingRecoveryUtc = now;
            hostingRecoveryAttempts++;
            if (hostingRecoveryAttempts < 8 && NativeMethods.TryEmbed(candidate, applicationHost.Handle, out recoveredStyle, out recoveredExStyle))
            {
                applicationHandle = candidate;
                originalApplicationStyle = recoveredStyle;
                originalApplicationExStyle = recoveredExStyle;
                originalApplicationOwner = recoveredOwner;
                applicationOverlay = false;
                NativeMethods.PrepareHostedExStyle(candidate, originalApplicationExStyle);
                if (applicationViewVisible) NativeMethods.ShowWindow(candidate, NativeMethods.SW_SHOW);
                ResizeApplication();
                NativeMethods.SetOpacity(candidate, opacitySlider.Value, originalApplicationExStyle);
                NativeMethods.SetForegroundWindow(Handle);
                NativeMethods.SetFocus(applicationHost.Handle);
                statusText.Text = "Application re-embedded after window switch";
                encodingStatus.Text = "Runtime  Embedded  " + opacitySlider.Value + "%";
                WriteOutput("Application window was recreated or restored and has been re-embedded.");
                return;
            }

            if (hostingRecoveryAttempts < 8) return;
            recoveredStyle = NativeMethods.GetStyle(candidate, NativeMethods.GWL_STYLE);
            recoveredExStyle = NativeMethods.GetStyle(candidate, NativeMethods.GWL_EXSTYLE);
            applicationHandle = candidate;
            originalApplicationStyle = recoveredStyle;
            originalApplicationExStyle = recoveredExStyle;
            originalApplicationOwner = recoveredOwner;
            NativeMethods.PrepareOverlay(candidate, originalApplicationStyle, originalApplicationExStyle, Handle);
            applicationOverlay = true;
            hostingRecoveryAttempts = 0;
            if (applicationViewVisible) ResizeApplication();
            NativeMethods.SetForegroundWindow(Handle);
            WriteOutput("Repeated re-embedding failed; switched to compatible overlay mode.");
            encodingStatus.Text = "Runtime  Overlay  " + opacitySlider.Value + "%";
        }

        private void MainFormClosing(object sender, FormClosingEventArgs e)
        {
            watchTimer.Stop();
            grayscaleApplied = false;
            NativeMethods.ShutdownMagnification();
            CloseApplicationWithHost();

            List<EditorDocument> documents = new List<EditorDocument>();
            foreach (TabPage tab in tabs.TabPages)
            {
                EditorDocument document = tab.Tag as EditorDocument;
                if (document != null) documents.Add(document);
            }
            foreach (EditorDocument document in documents)
            {
                if (!ConfirmCloseDocument(document))
                {
                    e.Cancel = true;
                    watchTimer.Start();
                    return;
                }
            }
            SaveSessionState();
        }

        private void SaveSessionState()
        {
            if (restoringSession || tabs == null) return;
            try
            {
                sessionState.WorkspaceDirectory = workspaceDirectory;
                sessionState.OpenFiles.Clear();
                foreach (TabPage tab in tabs.TabPages)
                {
                    EditorDocument document = tab.Tag as EditorDocument;
                    if (document != null && !String.IsNullOrEmpty(document.Path) && File.Exists(document.Path))
                        sessionState.OpenFiles.Add(document.Path);
                }
                EditorDocument current = CurrentDocument();
                sessionState.ActiveFile = current == null ? null : current.Path;
                Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                sessionState.WindowX = bounds.X;
                sessionState.WindowY = bounds.Y;
                sessionState.WindowWidth = bounds.Width;
                sessionState.WindowHeight = bounds.Height;
                sessionState.Maximized = WindowState == FormWindowState.Maximized;
                sessionState.Save();
            }
            catch (Exception ex)
            {
                WriteOutput("Unable to save workspace state: " + ex.Message);
            }
        }

        private void CloseApplicationWithHost()
        {
            if (applicationProcess == null) return;
            if (profile.CloseWithHost)
            {
                try
                {
                    if (!applicationProcess.HasExited)
                    {
                        if (NativeMethods.IsWindow(applicationHandle)) NativeMethods.PostMessage(applicationHandle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                        if (!applicationProcess.WaitForExit(profile.KillAfterMilliseconds))
                        {
                            applicationProcess.Kill();
                            applicationProcess.WaitForExit(2000);
                        }
                    }
                }
                catch { }
            }
            else if (applicationEmbedded && NativeMethods.IsWindow(applicationHandle))
            {
                NativeMethods.ShowWindow(applicationHandle, NativeMethods.SW_HIDE);
                NativeMethods.RestoreTopLevelWindow(applicationHandle, originalApplicationStyle, originalApplicationExStyle, originalApplicationOwner);
            }
        }

        private void HighlightEditor(RichTextBox editor, string extension)
        {
            if (editor.TextLength > 180000) return;
            editor.SuspendLayout();
            try
            {
                editor.SelectAll();
                editor.SelectionColor = textColor;
                if (extension == ".json")
                {
                    ApplyColor(editor, "\"(?:\\\\.|[^\"\\\\])*\"(?=\\s*:)", Color.FromArgb(86, 156, 214));
                    ApplyColor(editor, "\"(?:\\\\.|[^\"\\\\])*\"", Color.FromArgb(206, 145, 120));
                    ApplyColor(editor, "\\b(true|false|null)\\b", Color.FromArgb(197, 134, 192));
                }
                else if (extension == ".yaml" || extension == ".yml")
                {
                    ApplyColor(editor, "(?m)^\\s*[A-Za-z_][A-Za-z0-9_.-]*(?=\\s*:)", Color.FromArgb(86, 156, 214));
                    ApplyColor(editor, "(?m)#.*$", Color.FromArgb(106, 153, 85));
                    ApplyColor(editor, "\"(?:\\\\.|[^\"\\\\])*\"|'(?:''|[^'])*'", Color.FromArgb(206, 145, 120));
                    ApplyColor(editor, "\\b(true|false|null|yes|no|on|off)\\b", Color.FromArgb(197, 134, 192));
                    ApplyColor(editor, "(?<![A-Za-z0-9_.-])-?\\b\\d+(?:\\.\\d+)?\\b", Color.FromArgb(181, 206, 168));
                }
                else
                {
                    ApplyColor(editor, "(?m)//.*$|(?m)--.*$", Color.FromArgb(106, 153, 85));
                    ApplyColor(editor, "\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'", Color.FromArgb(206, 145, 120));
                    ApplyColor(editor, "\\b(package|import|func|return|type|struct|var|const|if|else|for|range|switch|case|default|break|continue|true|false|nil|class|public|private|using|namespace|new|void|int|string|bool)\\b", Color.FromArgb(86, 156, 214));
                }
                editor.Select(0, 0);
            }
            finally { editor.ResumeLayout(); }
        }

        private static void ApplyColor(RichTextBox editor, string pattern, Color color)
        {
            foreach (Match match in Regex.Matches(editor.Text, pattern))
            {
                editor.Select(match.Index, match.Length);
                editor.SelectionColor = color;
            }
        }

        private void UpdateCursorStatus(RichTextBox editor)
        {
            if (editor == null || positionStatus == null) return;
            int line = editor.GetLineFromCharIndex(editor.SelectionStart) + 1;
            int lineStart = editor.GetFirstCharIndexOfCurrentLine();
            int column = editor.SelectionStart - lineStart + 1;
            positionStatus.Text = "Ln " + line + ", Col " + column;
        }

        private void WriteOutput(string message)
        {
            if (output == null) return;
            output.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + "\r\n");
            output.SelectionStart = output.TextLength;
            output.ScrollToCaret();
        }

        private string AdapterNames()
        {
            List<string> names = new List<string>();
            foreach (WorkbenchProfile item in profiles) names.Add(item.DisplayName);
            return String.Join(", ", names.ToArray());
        }

        private string RelativePath(string path)
        {
            if (String.IsNullOrEmpty(path)) return String.Empty;
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full.Substring(prefix.Length) : full;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                codeFont.Dispose();
                uiFont.Dispose();
                if (watchTimer != null) watchTimer.Dispose();
                if (applicationProcess != null) applicationProcess.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
