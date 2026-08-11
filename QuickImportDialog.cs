using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WorkbenchHost
{
    internal sealed class QuickImportDialog : Form
    {
        private readonly TextBox displayName;
        private readonly TextBox activationPhrase;
        private readonly TextBox processName;
        private readonly TextBox arguments;
        private readonly TextBox windowClass;
        private readonly CheckBox attachExisting;
        private readonly CheckBox closeWithHost;
        private readonly CheckBox focusProtection;
        private readonly CheckBox enableGrayscale;
        private readonly NumericUpDown opacity;
        private readonly string executable;

        internal QuickImportDialog(string executablePath)
        {
            executable = Path.GetFullPath(executablePath);
            string fileName = Path.GetFileNameWithoutExtension(executable);
            string productName = String.Empty;
            try { productName = System.Diagnostics.FileVersionInfo.GetVersionInfo(executable).ProductName; } catch { }
            if (String.IsNullOrWhiteSpace(productName)) productName = fileName;

            Text = "Import Application";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(580, 438);
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.FromArgb(220, 220, 220);
            Font = new Font("Segoe UI", 9F);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(14);
            layout.ColumnCount = 2;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowCount = 12;
            for (int i = 0; i < 10; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

            TextBox executableBox = Input(executable, true);
            displayName = Input(productName, false);
            activationPhrase = Input("hello " + fileName.ToLowerInvariant(), false);
            processName = Input(fileName, false);
            arguments = Input(String.Empty, false);
            windowClass = Input(String.Empty, false);
            opacity = new NumericUpDown();
            opacity.Minimum = 0;
            opacity.Maximum = 100;
            opacity.Value = 100;
            opacity.Dock = DockStyle.Fill;
            attachExisting = Option("Attach existing instance", true);
            closeWithHost = Option("Close application with host", true);
            focusProtection = Option("Return to code on focus loss", true);
            enableGrayscale = Option("Enable B/W control", false);

            AddRow(layout, 0, "Executable", executableBox);
            AddRow(layout, 1, "Display name", displayName);
            AddRow(layout, 2, "Activation phrase", activationPhrase);
            AddRow(layout, 3, "Process name", processName);
            AddRow(layout, 4, "Arguments", arguments);
            AddRow(layout, 5, "Window class", windowClass);
            AddRow(layout, 6, "Default opacity", opacity);
            AddRow(layout, 7, String.Empty, attachExisting);
            AddRow(layout, 8, String.Empty, closeWithHost);
            AddRow(layout, 9, String.Empty, focusProtection);
            layout.Controls.Add(enableGrayscale, 1, 10);

            FlowLayoutPanel commands = new FlowLayoutPanel();
            commands.FlowDirection = FlowDirection.RightToLeft;
            commands.Dock = DockStyle.Fill;
            Button import = new Button();
            import.Text = "Import";
            import.DialogResult = DialogResult.OK;
            import.Size = new Size(88, 30);
            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Size = new Size(88, 30);
            commands.Controls.Add(import);
            commands.Controls.Add(cancel);
            layout.Controls.Add(commands, 0, 11);
            layout.SetColumnSpan(commands, 2);
            Controls.Add(layout);
            AcceptButton = import;
            CancelButton = cancel;
            FormClosing += ValidateInput;
        }

        internal ImportedProfileSettings Settings
        {
            get
            {
                return new ImportedProfileSettings
                {
                    Executable = executable,
                    DisplayName = displayName.Text.Trim(),
                    ActivationPhrase = activationPhrase.Text.Trim(),
                    ProcessName = processName.Text.Trim(),
                    Arguments = arguments.Text,
                    WindowClass = windowClass.Text.Trim(),
                    AttachExisting = attachExisting.Checked,
                    CloseWithHost = closeWithHost.Checked,
                    FocusProtection = focusProtection.Checked,
                    EnableGrayscale = enableGrayscale.Checked,
                    DefaultOpacity = (int)opacity.Value
                };
            }
        }

        private static TextBox Input(string value, bool readOnly)
        {
            TextBox input = new TextBox();
            input.Text = value;
            input.ReadOnly = readOnly;
            input.Dock = DockStyle.Fill;
            return input;
        }

        private static CheckBox Option(string text, bool value)
        {
            CheckBox option = new CheckBox();
            option.Text = text;
            option.Checked = value;
            option.Dock = DockStyle.Fill;
            option.AutoSize = true;
            return option;
        }

        private static void AddRow(TableLayoutPanel layout, int row, string labelText, Control control)
        {
            Label label = new Label();
            label.Text = labelText;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Dock = DockStyle.Fill;
            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(control, 1, row);
        }

        private void ValidateInput(object sender, FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.OK) return;
            if (String.IsNullOrWhiteSpace(displayName.Text) || String.IsNullOrWhiteSpace(activationPhrase.Text))
            {
                MessageBox.Show("Display name and activation phrase are required.", "Import Application", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Font != null) Font.Dispose();
            base.Dispose(disposing);
        }
    }
}
