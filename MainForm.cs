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
            internal bool IsTrigger;
        }

        private sealed class EditorDocument
        {
            internal string Path;
            internal string DisplayName;
            internal string SavedText;
            internal RichTextBox Editor;
            internal TabPage Tab;
            internal bool IsTrigger;
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
        private string triggerPath;
        private int untitledCounter;
        private readonly Color windowColor = Color.FromArgb(24, 24, 24);
        private readonly Color sidebarColor = Color.FromArgb(32, 32, 32);
        private readonly Color editorColor = Color.FromArgb(31, 31, 31);
        private readonly Color textColor = Color.FromArgb(204, 204, 204);
        private readonly Color mutedColor = Color.FromArgb(138, 138, 138);
        private readonly Font uiFont = new Font("Segoe UI", 9F);
        private readonly Font codeFont;

        private MenuStrip menu;
        private ToolStrip toolbar;
        private ToolStripLabel pathLabel;
        private ToolStripLabel opacityLabel;
        private ToolStripButton runButton;
        private ToolStripButton grayscaleButton;
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

        private readonly Dictionary<string, EditorDocument> openDocuments = new Dictionary<string, EditorDocument>(StringComparer.OrdinalIgnoreCase);
        private EditorDocument triggerDocument;
        private EditorDocument lastCodeDocument;
        private CodePanel applicationHost;
        private Process applicationProcess;
        private IntPtr applicationHandle = IntPtr.Zero;
        private long originalApplicationStyle;
        private bool applicationEmbedded;
        private bool applicationViewVisible;
        private bool grayscaleApplied;
        private bool f10WasDown;
        private bool focusProtection;
        private int focusAwayTicks;
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
            triggerPath = profile == null ? Path.Combine(root, "db.go") : profile.ResolveTriggerFile();
            EnsureTriggerFile();

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
            BuildToolbar();
            BuildStatus();
            BuildWorkspace();
            BuildEvents();
            RestoreSession();
            WriteOutput(profile == null ? "Host initialized in editor-only mode." : "Host initialized with profile: " + profile.Id);
            WriteOutput("Available adapters: " + AdapterNames());
            WriteOutput("Real workspace: " + workspaceDirectory);
            WriteOutput("Activation file: " + triggerPath);
        }

        private void InitializeWindow()
        {
            Text = profiles.Count == 1 ? profile.WindowTitle : "workspace - Code";
            BackColor = windowColor;
            ForeColor = textColor;
            Font = uiFont;
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
            if (sessionState.Maximized) WindowState = FormWindowState.Maximized;
            KeyPreview = true;
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
            menu.Dock = DockStyle.Top;
            menu.BackColor = windowColor;
            menu.ForeColor = textColor;
            menu.RenderMode = ToolStripRenderMode.System;
            menu.Padding = new Padding(8, 2, 0, 2);

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

            ToolStripMenuItem openRuntime = new ToolStripMenuItem("Open db.go");
            openRuntime.Click += delegate { ShowTriggerCode(); };
            ToolStripMenuItem returnCode = new ToolStripMenuItem("Return to Code    F10");
            returnCode.Click += delegate { ShowLastCode(); };
            run.DropDownItems.Add(openRuntime);
            run.DropDownItems.Add(returnCode);

            ToolStripMenuItem outputItem = new ToolStripMenuItem("Output    Ctrl+J");
            outputItem.Click += delegate { contentSplit.Panel2Collapsed = !contentSplit.Panel2Collapsed; };
            terminal.DropDownItems.Add(outputItem);

            ToolStripMenuItem about = new ToolStripMenuItem("Real editor workspace");
            about.Click += delegate { MessageBox.Show("Application adapters: " + AdapterNames() + "\r\nWorkspace: " + workspaceDirectory, "Workbench Host"); };
            help.DropDownItems.Add(about);

            Controls.Add(menu);
            MainMenuStrip = menu;
        }

        private ToolStripMenuItem MenuItem(string text, EventHandler action, Keys shortcut)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += action;
            if (shortcut != Keys.None) item.ShortcutKeys = shortcut;
            return item;
        }

        private void BuildToolbar()
        {
            toolbar = new ToolStrip();
            toolbar.Dock = DockStyle.Top;
            toolbar.GripStyle = ToolStripGripStyle.Hidden;
            toolbar.BackColor = editorColor;
            toolbar.ForeColor = textColor;
            toolbar.Height = 36;
            toolbar.Padding = new Padding(8, 4, 8, 4);

            ToolStripButton back = new ToolStripButton("<");
            back.ToolTipText = "Previous editor";
            back.Click += delegate { if (tabs.TabCount > 0) tabs.SelectedIndex = Math.Max(0, tabs.SelectedIndex - 1); };
            ToolStripButton forward = new ToolStripButton(">");
            forward.ToolTipText = "Next editor";
            forward.Click += delegate { if (tabs.TabCount > 0) tabs.SelectedIndex = Math.Min(tabs.TabCount - 1, tabs.SelectedIndex + 1); };
            ToolStripButton openFolder = new ToolStripButton("OPEN FOLDER");
            openFolder.ToolTipText = "Open a real workspace folder";
            openFolder.Click += delegate { OpenFolder(); };
            ToolStripButton save = new ToolStripButton("SAVE");
            save.ToolTipText = "Save current file (Ctrl+S)";
            save.Click += delegate { SaveCurrentDocument(); };
            pathLabel = new ToolStripLabel("  " + RelativePath(workspaceDirectory));
            pathLabel.ForeColor = mutedColor;

            ToolStripButton code = new ToolStripButton("CODE");
            code.Alignment = ToolStripItemAlignment.Right;
            code.ToolTipText = "Return to db.go (F10)";
            code.Click += delegate { ShowLastCode(); };
            runButton = new ToolStripButton("RUN");
            runButton.Alignment = ToolStripItemAlignment.Right;
            runButton.Enabled = profile != null;
            runButton.ToolTipText = "Open db.go activation file";
            runButton.Click += delegate { ShowTriggerCode(); };

            opacitySlider = new TrackBar();
            opacitySlider.Minimum = 0;
            opacitySlider.Maximum = 100;
            int initialOpacity = profile == null ? 100 : profile.DefaultOpacity;
            opacitySlider.Value = initialOpacity;
            opacitySlider.TickStyle = TickStyle.None;
            opacitySlider.AutoSize = false;
            opacitySlider.Size = new Size(96, 24);
            ToolStripControlHost opacityHost = new ToolStripControlHost(opacitySlider);
            opacityHost.Alignment = ToolStripItemAlignment.Right;
            opacityHost.AutoSize = false;
            opacityHost.Size = new Size(102, 24);
            opacityHost.ToolTipText = "Application opacity";
            opacityLabel = new ToolStripLabel("OPACITY " + initialOpacity + "%");
            opacityLabel.Alignment = ToolStripItemAlignment.Right;
            opacityLabel.ForeColor = mutedColor;

            grayscaleButton = new ToolStripButton("B/W");
            grayscaleButton.Alignment = ToolStripItemAlignment.Right;
            grayscaleButton.CheckOnClick = true;
            grayscaleButton.Enabled = profile != null && profile.EnableGrayscale;
            grayscaleButton.ToolTipText = "Toggle low-overhead system grayscale";

            toolbar.Items.AddRange(new ToolStripItem[]
            {
                back, forward, openFolder, save, pathLabel, code, runButton, opacityHost, opacityLabel, grayscaleButton
            });
            Controls.Add(toolbar);
        }

        private void BuildStatus()
        {
            status = new StatusStrip();
            status.Dock = DockStyle.Bottom;
            status.BackColor = windowColor;
            status.ForeColor = textColor;
            status.SizingGrip = false;
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
            main.BackColor = Color.FromArgb(43, 43, 43);
            main.Panel1.BackColor = sidebarColor;
            main.Panel2.BackColor = editorColor;

            Panel activity = new Panel();
            activity.Dock = DockStyle.Left;
            activity.Width = 48;
            activity.BackColor = windowColor;
            string[] activityNames = { "EX", "SR" };
            string[] activityTips = { "Explorer", "Search in current file" };
            ToolTip tips = new ToolTip();
            for (int i = 0; i < activityNames.Length; i++)
            {
                Button button = new Button();
                button.Text = activityNames[i];
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 0;
                button.ForeColor = i == 0 ? textColor : mutedColor;
                button.BackColor = windowColor;
                button.Size = new Size(48, 46);
                button.Location = new Point(0, i * 48 + 4);
                button.TabStop = false;
                if (i == 0) button.Click += delegate { tree.Focus(); };
                else button.Click += delegate { FindInCurrent(); };
                tips.SetToolTip(button, activityTips[i]);
                activity.Controls.Add(button);
            }

            Panel explorer = new Panel();
            explorer.Dock = DockStyle.Fill;
            explorer.BackColor = sidebarColor;
            explorerTitle = new Label();
            explorerTitle.Text = "EXPLORER  " + Path.GetFileName(workspaceDirectory).ToUpperInvariant();
            explorerTitle.Dock = DockStyle.Top;
            explorerTitle.Height = 36;
            explorerTitle.Padding = new Padding(12, 10, 0, 0);
            explorerTitle.ForeColor = textColor;
            explorerTitle.Font = new Font("Segoe UI", 8F);

            tree = new TreeView();
            tree.Dock = DockStyle.Fill;
            tree.BorderStyle = BorderStyle.None;
            tree.BackColor = sidebarColor;
            tree.ForeColor = textColor;
            tree.Font = uiFont;
            tree.HideSelection = false;
            tree.ShowLines = false;
            tree.ShowPlusMinus = true;
            tree.Indent = 16;
            tree.ItemHeight = 24;
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

            tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Padding = new Point(16, 5);
            tabs.Font = uiFont;
            tabs.BackColor = editorColor;

            Label outputHeader = new Label();
            outputHeader.Text = "OUTPUT     DEBUG CONSOLE     TERMINAL";
            outputHeader.Dock = DockStyle.Top;
            outputHeader.Height = 30;
            outputHeader.Padding = new Padding(12, 8, 0, 0);
            outputHeader.BackColor = windowColor;
            outputHeader.ForeColor = textColor;
            outputHeader.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            output = new RichTextBox();
            output.Dock = DockStyle.Fill;
            output.ReadOnly = true;
            output.BorderStyle = BorderStyle.None;
            output.BackColor = windowColor;
            output.ForeColor = textColor;
            output.Font = codeFont;

            contentSplit.Panel1.Controls.Add(tabs);
            contentSplit.Panel2.Controls.Add(output);
            contentSplit.Panel2.Controls.Add(outputHeader);
            main.Panel2.Controls.Add(contentSplit);
            Controls.Add(main);
            main.BringToFront();
            ReloadTree();
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

                string triggerParent = Path.GetDirectoryName(triggerPath);
                if (!String.Equals(triggerParent, workspaceDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    TreeNode special = new TreeNode("WORKBENCH");
                    special.NodeFont = new Font("Segoe UI", 8F, FontStyle.Bold);
                    TreeNode trigger = new TreeNode(Path.GetFileName(triggerPath));
                    trigger.ForeColor = Color.FromArgb(220, 220, 170);
                    trigger.Tag = new NodeTarget { Path = triggerPath, IsTrigger = true };
                    special.Nodes.Add(trigger);
                    tree.Nodes.Add(special);
                }
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
                    node.Tag = new NodeTarget { Path = child.FullName, IsTrigger = String.Equals(child.FullName, triggerPath, StringComparison.OrdinalIgnoreCase) };
                    if (((NodeTarget)node.Tag).IsTrigger) node.ForeColor = Color.FromArgb(220, 220, 170);
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
                if (document.IsTrigger)
                {
                    document.Editor.Visible = true;
                    document.Editor.BringToFront();
                }
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

            opacitySlider.ValueChanged += delegate
            {
                opacityLabel.Text = "OPACITY " + opacitySlider.Value + "%";
                if (applicationEmbedded && NativeMethods.IsWindow(applicationHandle)) NativeMethods.SetOpacity(applicationHandle, opacitySlider.Value);
                if (applicationViewVisible) encodingStatus.Text = "Runtime  Embedded  " + opacitySlider.Value + "%";
            };
            grayscaleButton.CheckedChanged += delegate
            {
                ApplyGrayscaleState();
                if (applicationViewVisible) statusText.Text = grayscaleButton.Checked ? "Application attached - grayscale enabled" : "Application attached - color enabled";
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
            FormClosing += MainFormClosing;
        }

        private void OpenTreeTarget(TreeNode node)
        {
            NodeTarget target = node == null ? null : node.Tag as NodeTarget;
            if (target == null || target.IsDirectory) return;
            OpenCodeFile(target.Path, Path.GetFileName(target.Path), target.IsTrigger);
        }

        private void OpenCodeFile(string path, string displayName, bool isTrigger)
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
            document.IsTrigger = isTrigger || String.Equals(fullPath, triggerPath, StringComparison.OrdinalIgnoreCase);
            document.SavedText = text;
            document.Editor = NewEditor(false);
            document.SuppressChanges = true;
            document.Editor.Text = text;
            HighlightEditor(document.Editor, Path.GetExtension(fullPath).ToLowerInvariant());
            document.SuppressChanges = false;
            document.Editor.Modified = false;
            document.Editor.SelectionChanged += delegate { UpdateCursorStatus(document.Editor); };
            document.Editor.TextChanged += delegate { DocumentTextChanged(document); };
            document.Tab = new TabPage(displayName + "  x");
            document.Tab.Tag = document;
            document.Tab.BackColor = editorColor;
            document.Tab.Controls.Add(document.Editor);
            tabs.TabPages.Add(document.Tab);
            openDocuments[fullPath] = document;
            if (document.IsTrigger) triggerDocument = document;
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
                    OpenCodeFile(path, Path.GetFileName(path), String.Equals(Path.GetFullPath(path), triggerPath, StringComparison.OrdinalIgnoreCase));
                }
                if (tabs.TabCount == 0) OpenCodeFile(triggerPath, Path.GetFileName(triggerPath), true);

                if (!String.IsNullOrWhiteSpace(sessionState.ActiveFile))
                {
                    EditorDocument active;
                    if (openDocuments.TryGetValue(Path.GetFullPath(sessionState.ActiveFile), out active)) tabs.SelectedTab = active.Tab;
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
            return editor;
        }

        private void EnsureTriggerFile()
        {
            string directory = Path.GetDirectoryName(triggerPath);
            if (!String.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
            if (!File.Exists(triggerPath))
            {
                string source = "package workspace\r\n\r\n// Application command gateway.\r\n// Type the configured command here to run the selected adapter.\r\n\r\nfunc Open() error {\r\n\treturn nil\r\n}\r\n";
                File.WriteAllText(triggerPath, source, new UTF8Encoding(false));
            }
        }

        private EditorDocument EnsureTriggerDocument()
        {
            if (triggerDocument == null || !tabs.TabPages.Contains(triggerDocument.Tab))
                OpenCodeFile(triggerPath, Path.GetFileName(triggerPath), true);
            return triggerDocument;
        }

        private void DocumentTextChanged(EditorDocument document)
        {
            if (document.SuppressChanges) return;
            UpdateDocumentTitle(document);
            if (!document.IsTrigger) return;
            WorkbenchProfile selectedProfile = null;
            int index = -1;
            foreach (WorkbenchProfile candidate in profiles)
            {
                if (String.IsNullOrWhiteSpace(candidate.ActivationPhrase)) continue;
                int candidateIndex = document.Editor.Text.IndexOf(candidate.ActivationPhrase, StringComparison.OrdinalIgnoreCase);
                if (candidateIndex < 0) continue;
                selectedProfile = candidate;
                index = candidateIndex;
                break;
            }
            if (selectedProfile == null) return;
            document.SuppressChanges = true;
            document.Editor.Select(index, selectedProfile.ActivationPhrase.Length);
            document.Editor.SelectedText = String.Empty;
            document.SuppressChanges = false;
            UpdateDocumentTitle(document);
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

        private void ShowTriggerCode()
        {
            EditorDocument document = EnsureTriggerDocument();
            if (document == null) return;
            HideApplicationWindow();
            applicationViewVisible = false;
            ApplyGrayscaleState();
            document.Editor.Visible = true;
            document.Editor.BringToFront();
            suppressTabSwitch = true;
            tabs.SelectedTab = document.Tab;
            suppressTabSwitch = false;
            lastCodeDocument = document;
            UpdatePathStatus(document);
            statusText.Text = "Ready";
            if (ContainsFocus) document.Editor.Focus();
        }

        private void ActivateApplication()
        {
            EditorDocument document = EnsureTriggerDocument();
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
            pathLabel.Text = "  " + profile.Id + " / " + Path.GetFileName(triggerPath);
            encodingStatus.Text = "Runtime  Embedded  " + opacitySlider.Value + "%";
            statusText.Text = "Waiting for application window...";

            try
            {
                StartAndEmbedApplication();
                NativeMethods.ShowWindow(applicationHandle, NativeMethods.SW_SHOW);
                ResizeApplication();
                NativeMethods.SetOpacity(applicationHandle, opacitySlider.Value);
                ApplyGrayscaleState();
                statusText.Text = "Application attached - F10 returns to code";
            }
            catch (Exception ex)
            {
                WriteOutput("ERROR: " + ex.Message);
                contentSplit.Panel2Collapsed = false;
                statusText.Text = "Application failed to attach";
                MessageBox.Show(ex.Message, "Unable to open application", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ShowTriggerCode();
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
            if (applicationHost != null) return applicationHost;
            applicationHost = new CodePanel();
            applicationHost.Dock = DockStyle.Fill;
            applicationHost.Visible = false;
            applicationHost.CodeFont = codeFont;
            applicationHost.SourceEditor = document.Editor;
            applicationHost.Resize += delegate { ResizeApplication(); };
            document.Tab.Controls.Add(applicationHost);
            return applicationHost;
        }

        private void StartAndEmbedApplication()
        {
            if (applicationEmbedded && NativeMethods.IsWindow(applicationHandle)) return;

            ApplicationWindow candidate = profile.AttachExisting ? FindExistingApplicationWindow() : null;
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
                if (windowProcess != null) break;
                if (applicationProcess != null && applicationProcess.HasExited)
                    throw new InvalidOperationException("Application process exited before creating a window.");
                Application.DoEvents();
                Thread.Sleep(100);
            }
            if (applicationHandle == IntPtr.Zero) throw new TimeoutException("Application window was not available within " + profile.LaunchTimeoutSeconds + " seconds.");

            originalApplicationStyle = NativeMethods.Embed(applicationHandle, applicationHost.Handle);
            applicationEmbedded = true;
            NativeMethods.HideOtherTopLevelWindows((uint)applicationProcess.Id, applicationHandle);
            ResizeApplication();
            WriteOutput("Application attached (PID " + applicationProcess.Id + ").");
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
            NativeMethods.Resize(applicationHandle, applicationHost.ClientSize.Width, applicationHost.ClientSize.Height);
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
                ShowTriggerCode();
                return;
            }
            if (lastCodeDocument != null && tabs.TabPages.Contains(lastCodeDocument.Tab)) tabs.SelectedTab = lastCodeDocument.Tab;
            else ShowTriggerCode();
            Activate();
        }

        private void CloseCurrentTab()
        {
            TabPage tab = tabs.SelectedTab;
            if (tab == null) return;
            EditorDocument document = tab.Tag as EditorDocument;
            if (document == null) return;
            if (!ConfirmCloseDocument(document)) return;
            if (document == triggerDocument && applicationViewVisible) ShowTriggerCode();
            if (document == triggerDocument && applicationEmbedded)
            {
                CloseApplicationWithHost();
                applicationEmbedded = false;
                applicationHandle = IntPtr.Zero;
                applicationProcess = null;
            }
            if (!String.IsNullOrEmpty(document.Path)) openDocuments.Remove(document.Path);
            if (document == triggerDocument)
            {
                triggerDocument = null;
                applicationHost = null;
            }
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
            document.Editor.SelectionChanged += delegate { UpdateCursorStatus(document.Editor); };
            document.Tab = new TabPage(document.DisplayName + "  x");
            document.Tab.Tag = document;
            document.Tab.BackColor = editorColor;
            document.Tab.Controls.Add(document.Editor);
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
            if (dialog.ShowDialog(this) == DialogResult.OK) OpenCodeFile(dialog.FileName, Path.GetFileName(dialog.FileName), false);
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
                explorerTitle.Text = "EXPLORER  " + Path.GetFileName(workspaceDirectory).ToUpperInvariant();
                pathLabel.Text = "  " + RelativePath(workspaceDirectory);
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
                OpenCodeFile(profilePath, Path.GetFileName(profilePath), false);
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
            dialog.BackColor = windowColor;
            dialog.ForeColor = textColor;

            Label label = new Label();
            label.Text = labelText;
            label.Location = new Point(12, 12);
            label.AutoSize = true;
            TextBox input = new TextBox();
            input.Text = initialValue ?? String.Empty;
            input.Location = new Point(12, 36);
            input.Width = 396;
            Button ok = new Button();
            ok.Text = "OK";
            ok.DialogResult = DialogResult.OK;
            ok.Location = new Point(252, 76);
            ok.Width = 75;
            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(333, 76);
            cancel.Width = 75;
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
                if (String.Equals(document.Path, triggerPath, StringComparison.OrdinalIgnoreCase)) ReloadTree();
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
                if (String.Equals(newPath, triggerPath, StringComparison.OrdinalIgnoreCase))
                {
                    document.IsTrigger = true;
                    triggerDocument = document;
                }
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
            pathLabel.Text = document.Path == null
                ? "  " + document.DisplayName
                : "  " + RelativePath(document.Path).Replace("\\", " / ");
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
                    ShowTriggerCode();
                }
            }
            else focusAwayTicks = 0;

            if (applicationProcess != null && applicationEmbedded)
            {
                try
                {
                    if (!applicationProcess.HasExited) return;
                    applicationEmbedded = false;
                    applicationHandle = IntPtr.Zero;
                    statusText.Text = "Application process exited";
                    WriteOutput("Application process exited.");
                    ShowTriggerCode();
                }
                catch { }
            }
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
                CloseSiblingApplicationProcesses();
            }
            else if (applicationEmbedded && NativeMethods.IsWindow(applicationHandle))
            {
                NativeMethods.ShowWindow(applicationHandle, NativeMethods.SW_HIDE);
                NativeMethods.SetParent(applicationHandle, IntPtr.Zero);
                NativeMethods.SetStyle(applicationHandle, NativeMethods.GWL_STYLE, originalApplicationStyle);
                NativeMethods.SetWindowPos(applicationHandle, IntPtr.Zero, 100, 100, 1280, 720, NativeMethods.SWP_SHOWWINDOW);
            }
        }

        private void CloseSiblingApplicationProcesses()
        {
            int primaryId = applicationProcess == null ? 0 : applicationProcess.Id;
            Process[] siblings = Process.GetProcessesByName(profile.ProcessName);
            foreach (Process sibling in siblings)
            {
                try
                {
                    if (sibling.Id == primaryId) continue;
                    sibling.Refresh();
                    if (sibling.HasExited) continue;
                    IntPtr handle = NativeMethods.FindTopLevelWindow((uint)sibling.Id, profile.WindowClass);
                    if (handle != IntPtr.Zero) NativeMethods.PostMessage(handle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    if (!sibling.WaitForExit(600))
                    {
                        sibling.Kill();
                        sibling.WaitForExit(1200);
                    }
                }
                catch { }
                finally { sibling.Dispose(); }
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
