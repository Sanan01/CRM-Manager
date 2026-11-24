using CRMManager.Core;
using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CRMManager.XrmToolBox
{
    internal sealed class RegistrationForm : Form
    {
        private readonly CrmSession crm;
        private readonly StepRegistration step;
        private readonly ImageRegistration? image;
        private readonly TableLayoutPanel fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(8) };
        private readonly TextBox name = new TextBox();
        private readonly ComboBox message = Selector();
        private readonly ComboBox entity = Selector();
        private readonly ComboBox stage = Selector();
        private readonly ComboBox mode = Selector();
        private readonly TextBox rank = new TextBox();
        private readonly TextBox filtering = new TextBox();
        private readonly TextBox description = new TextBox { Multiline = true, Height = 55, ScrollBars = ScrollBars.Vertical };
        private readonly TextBox configuration = new TextBox { Multiline = true, Height = 80, ScrollBars = ScrollBars.Vertical };
        private readonly CheckBox asyncDelete = new CheckBox { Text = "Delete successful asynchronous jobs", AutoSize = true };
        private readonly TextBox alias = new TextBox();
        private readonly ComboBox imageType = Selector();
        private readonly TextBox property = new TextBox();
        private readonly TextBox attributes = new TextBox { Multiline = true, Height = 65, ScrollBars = ScrollBars.Vertical };
        private readonly CheckBox allAttributes = new CheckBox { Text = "All attributes (larger payload)", AutoSize = true };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(8) };
        private readonly Button save;
        private bool initializing = true;
        private bool loading;
        private bool accepted;
        private string initialValues = "";

        internal void RetainForRetry()
        {
            accepted = false;
            status.Text = "Save failed. Your local edits are retained; review server state before retrying.";
        }

        internal RegistrationForm(CrmSession crm, StepRegistration step, ImageRegistration? image = null)
        {
            this.crm = crm;
            this.step = step;
            this.image = image;
            ToolUi.ConfigureForm(this, image == null ? "CRM Manager - Plugin step" : "CRM Manager - Step image", 760, 820);
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12) };
            scroll.Controls.Add(fields);
            var footer = ToolUi.Toolbar();
            footer.Dock = DockStyle.Bottom;
            save = ToolUi.Button("Review & save", Save, true);
            footer.Controls.Add(save);
            var cancel = ToolUi.Button("Cancel", Close);
            footer.Controls.Add(cancel);
            CancelButton = cancel;
            Controls.Add(scroll);
            Controls.Add(status);
            Controls.Add(footer);
            Controls.Add(ToolUi.Hint("Development: " + crm.OrganizationName + ". Saving requires confirmation in the main tool."));
            name.Text = image?.Name ?? step.Name;
            Add("Registration name", name);
            if (image == null)
            {
                Add("Message (fixed after registration)", message);
                Add("Primary entity / filter (global selection is explicit)", entity);
                stage.Items.AddRange(new object[] { "Pre-validation (10)", "Pre-operation (20)", "Post-operation (40)" });
                stage.SelectedIndex = Array.IndexOf(new[] { 10, 20, 40 }, step.Stage);
                mode.Items.AddRange(new object[] { "Synchronous", "Asynchronous" });
                mode.SelectedIndex = step.Mode;
                Add("Stage", stage);
                Add("Execution mode (asynchronous requires Post-operation)", mode);
                rank.Text = step.Rank.ToString();
                Add("Execution order", rank);
                asyncDelete.Checked = step.AsyncAutoDelete;
                Add("", asyncDelete);
                filtering.Text = step.FilteringAttributes;
                Add("Update filtering attributes (comma-separated logical names; blank = all updates)", filtering);
                description.Text = step.Description;
                Add("Description", description);
                configuration.Text = step.UnsecureConfiguration;
                Add("Unsecure configuration (never enter secrets)", configuration);
                Add("", ToolUi.Hint("Existing secure configuration, run-as identity, deployment and state are preserved."));
                message.DisplayMember = entity.DisplayMember = "Name";
                message.SelectionChangeCommitted += async (sender, e) => await LoadFiltersSafelyAsync();
            }
            else
            {
                alias.Text = image.Alias;
                Add("Entity alias", alias);
                imageType.Items.AddRange(new object[] { "Pre-image", "Post-image", "Pre-image and post-image" });
                imageType.SelectedIndex = image.ImageType;
                Add("Image type", imageType);
                property.Text = image.MessagePropertyName;
                Add("Message property name (normally Target for Create / Update / Delete)", property);
                attributes.Text = image.Attributes;
                Add("Image attributes (comma-separated logical names)", attributes);
                allAttributes.Checked = image.AllAttributes;
                allAttributes.CheckedChanged += (sender, e) => attributes.Enabled = !allAttributes.Checked;
                attributes.Enabled = !allAttributes.Checked;
                Add("", allAttributes);
                Add("", ToolUi.Hint("Create: no pre-image. Delete: no post-image. Post-images require Post-operation. CRM validates message-specific support."));
            }
            Shown += async (sender, e) => await InitializeAsync();
            FormClosing += ClosingForm;
            save.Enabled = false;
        }

        private static ComboBox Selector() => new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };

        private void Add(string title, Control control)
        {
            if (title.Length > 0)
                fields.Controls.Add(new Label { Text = title, AutoSize = true, Margin = new Padding(0, 12, 0, 5) });
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            control.Margin = new Padding(0, 0, 0, 4);
            control.AccessibleName = title;
            fields.Controls.Add(control);
        }

        private async Task InitializeAsync()
        {
            loading = true;
            fields.Enabled = false;
            status.Text = "Loading registration metadata...";
            try
            {
                if (image == null)
                {
                    var messages = (await crm.GetRegistrationMessagesAsync()).ToList();
                    message.DataSource = messages;
                    message.SelectedIndex = -1;
                    if (step.Id != Guid.Empty)
                    {
                        message.SelectedItem = messages.FirstOrDefault(option => option.Id == step.MessageId)
                            ?? throw new InvalidOperationException("The registered message is no longer allowed.");
                        await LoadFiltersAsync();
                        entity.SelectedItem = entity.Items.Cast<RegistrationOption>().FirstOrDefault(option => option.Id == (step.FilterId ?? Guid.Empty))
                            ?? throw new InvalidOperationException("The registered entity filter is no longer allowed.");
                        message.Enabled = entity.Enabled = false;
                    }
                }
                initializing = false;
                initialValues = CaptureFields();
                status.Text = "Review the fields. Changes apply only after confirmation.";
            }
            catch (Exception exception)
            {
                status.Text = "Metadata load failed: " + exception.Message;
                MessageBox.Show(this, exception.Message, "Registration metadata failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                loading = false;
                fields.Enabled = true;
                save.Enabled = !initializing;
            }
        }

        private async Task LoadFiltersSafelyAsync()
        {
            loading = true;
            fields.Enabled = false;
            save.Enabled = false;
            try
            {
                await LoadFiltersAsync();
                status.Text = "Select a primary entity filter explicitly.";
            }
            catch (Exception exception)
            {
                entity.DataSource = null;
                status.Text = "Entity filter load failed: " + exception.Message;
                MessageBox.Show(this, exception.Message, "Filter metadata failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { loading = false; fields.Enabled = true; save.Enabled = !initializing; }
        }

        private async Task LoadFiltersAsync()
        {
            if (!(message.SelectedItem is RegistrationOption selected)) throw new InvalidOperationException("Select a message first.");
            entity.DataSource = (await crm.GetMessageFiltersAsync(selected.Id)).ToList();
            entity.SelectedIndex = -1;
        }

        private void Save()
        {
            if (loading || initializing) return;
            try
            {
                if (image == null)
                {
                    if (!(message.SelectedItem is RegistrationOption selectedMessage) || !(entity.SelectedItem is RegistrationOption selectedEntity))
                        throw new ArgumentException("Select a message and entity filter explicitly.");
                    if (!int.TryParse(rank.Text, out var order)) throw new ArgumentException("Execution order must be a whole number.");
                    step.Name = name.Text.Trim();
                    step.MessageId = selectedMessage.Id;
                    step.MessageName = selectedMessage.Name;
                    step.FilterId = selectedEntity.Id == Guid.Empty ? (Guid?)null : selectedEntity.Id;
                    step.Stage = stage.SelectedIndex >= 0 ? new[] { 10, 20, 40 }[stage.SelectedIndex] : 0;
                    step.Mode = mode.SelectedIndex;
                    step.Rank = order;
                    step.FilteringAttributes = filtering.Text;
                    step.Description = description.Text;
                    step.UnsecureConfiguration = configuration.Text;
                    step.AsyncAutoDelete = asyncDelete.Checked;
                    RegistrationRules.ValidateStep(step);
                }
                else
                {
                    image.Name = name.Text.Trim();
                    image.Alias = alias.Text.Trim();
                    image.ImageType = imageType.SelectedIndex;
                    image.MessagePropertyName = property.Text.Trim();
                    image.Attributes = attributes.Text;
                    image.AllAttributes = allAttributes.Checked;
                    RegistrationRules.ValidateImage(image, step);
                }
                accepted = true;
                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                status.Text = exception.Message;
                MessageBox.Show(this, exception.Message, "Check registration", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private string CaptureFields() => string.Join("\n", new[]
        {
            name.Text, message.SelectedIndex.ToString(), entity.SelectedIndex.ToString(), stage.SelectedIndex.ToString(),
            mode.SelectedIndex.ToString(), rank.Text, filtering.Text, description.Text, configuration.Text,
            asyncDelete.Checked.ToString(), alias.Text, imageType.SelectedIndex.ToString(), property.Text,
            attributes.Text, allAttributes.Checked.ToString()
        });

        private void ClosingForm(object sender, FormClosingEventArgs e)
        {
            if (loading) { e.Cancel = true; status.Text = "Wait for metadata loading to finish."; }
            else if (!accepted && !initializing && CaptureFields() != initialValues)
                e.Cancel = MessageBox.Show(this, "Discard your unsaved registration edits?", "Unsaved registration",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes;
        }
    }
}
