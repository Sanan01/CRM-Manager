using CRMManager.Core;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace CRMManager.XrmToolBox
{
    internal sealed class CodeEditorForm : Form
    {
        private readonly TextBox code = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, AcceptsTab = true,
            WordWrap = false, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 11)
        };
        private readonly string initialText;
        private bool accepted;

        internal CodeEditorForm(WebResourceDocument document)
        {
            ToolUi.ConfigureForm(this, "CRM Manager - " + document.Name, 1000, 740);
            Controls.Add(code);
            Controls.Add(ToolUi.Hint($"{document.EncodingName} | Unpublished content. Save requires confirmation; it does not publish."));
            var footer = ToolUi.Toolbar();
            footer.Dock = DockStyle.Bottom;
            var save = ToolUi.Button("Review & save unpublished", Save, true);
            footer.Controls.Add(save);
            footer.Controls.Add(ToolUi.Button("Cancel", Close));
            var wrap = new CheckBox { Text = "Wrap lines", AutoSize = true, Margin = new Padding(8) };
            wrap.CheckedChanged += (sender, e) => code.WordWrap = wrap.Checked;
            footer.Controls.Add(wrap);
            Controls.Add(footer);
            code.Text = document.Text;
            initialText = code.Text;
            Code = document.Text;
            KeyPreview = true;
            KeyDown += (sender, e) =>
            {
                if (e.Control && e.KeyCode == Keys.S) { e.SuppressKeyPress = true; Save(); }
            };
            Shown += (sender, e) => code.Focus();
            FormClosing += (sender, e) =>
            {
                if (!accepted && code.Text != initialText)
                    e.Cancel = MessageBox.Show(this, "Discard unsaved local edits?", "Unsaved code", MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes;
            };
        }

        internal string Code { get; private set; }

        internal void RetainForRetry() => accepted = false;

        private void Save()
        {
            if (code.Text != initialText) Code = code.Text;
            accepted = true;
            DialogResult = DialogResult.OK;
        }
    }

    internal sealed class AssemblyRegistrationForm : Form
    {
        private readonly TextBox names = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
        private readonly CheckBox sandbox = new CheckBox { Text = "Sandbox isolation", Checked = true, AutoSize = true, Margin = new Padding(8) };

        internal AssemblyRegistrationForm()
        {
            ToolUi.ConfigureForm(this, "CRM Manager - Register signed assembly", 720, 480);
            Controls.Add(names);
            Controls.Add(ToolUi.Hint("Enter fully qualified IPlugin type names, one per line. Choose the signed DLL next. Steps are created separately."));
            var footer = ToolUi.Toolbar();
            footer.Dock = DockStyle.Bottom;
            footer.Controls.Add(ToolUi.Button("Choose DLL next", () =>
            {
                try
                {
                    DeploymentRules.ParseTypeNames(names.Text);
                    DialogResult = DialogResult.OK;
                }
                catch (ArgumentException exception)
                {
                    MessageBox.Show(this, exception.Message, "Check plugin types", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }, true));
            footer.Controls.Add(ToolUi.Button("Cancel", Close));
            footer.Controls.Add(sandbox);
            Controls.Add(footer);
        }

        internal string TypeNames => names.Text;
        internal bool Sandbox => sandbox.Checked;
    }
}
