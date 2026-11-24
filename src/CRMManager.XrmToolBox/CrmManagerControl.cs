using BAJPluginManager.Core;
using McTools.Xrm.Connection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Tooling.Connector;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using XrmToolBox.Extensibility;
using Label = System.Windows.Forms.Label;

namespace CRMManager.XrmToolBox
{
    public sealed class CrmManagerControl : PluginControlBase
    {
        private readonly TabControl pages = new TabControl { Dock = DockStyle.Fill };
        private readonly Label connectionStatus = ToolUi.Hint("Select an on-premises CRM 9.1 connection in XrmToolBox. Development use only.");
        private readonly Label operationStatus = new Label { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(4) };
        private readonly TextBox log = new TextBox { Dock = DockStyle.Bottom, Height = 90, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        private readonly TreeView tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, BorderStyle = BorderStyle.None };
        private readonly TextBox details = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White };
        private readonly DataGridView resources = ToolUi.Grid(("Name", "Name"), ("Display name", "DisplayName"), ("Type", "Details"));
        private readonly DataGridView solutions = ToolUi.Grid(("Solution", "DisplayName"), ("Unique name", "Name"), ("Version", "Version"));
        private readonly CheckBox managed = new CheckBox { Text = "Managed package", AutoSize = true, Margin = new Padding(8, 8, 8, 0) };
        private readonly TextBox version = new TextBox { Width = 150, AccessibleName = "New solution version", Margin = new Padding(8) };
        private CrmSession? workspace;
        private int connectionRevision;
        private bool busy;
        private bool shuttingDown;

        public CrmManagerControl()
        {
            Font = new Font("Segoe UI", 9);
            ForeColor = ToolUi.Ink;
            BackColor = ToolUi.Canvas;
            Padding = new Padding(16);
            MinimumSize = new Size(650, 450);
            PluginIcon = CrmManagerPlugin.LoadIcon();
            TabIcon = PluginIcon.ToBitmap();
            Controls.Add(pages);
            Controls.Add(log);
            Controls.Add(operationStatus);
            var header = new Label { Text = "CRM Manager  /  Development workspace", Dock = DockStyle.Top, Height = 38, Font = new Font("Segoe UI", 16, FontStyle.Bold) };
            Controls.Add(connectionStatus);
            Controls.Add(header);
            BuildPlugins();
            BuildResources();
            BuildSolutions();
            tree.BeforeExpand += Tree_BeforeExpand;
            tree.AfterSelect += (sender, e) => ShowDetails(e.Node);
            solutions.SelectionChanged += (sender, e) => version.Clear();
            operationStatus.Text = "Ready. CRM actions will ask XrmToolBox for a connection when needed.";
        }

        public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName, object parameter)
        {
            connectionRevision++;
            var previous = workspace;
            workspace = null;
            if (previous != null) _ = Task.Run(() => previous.Dispose());
            base.UpdateConnection(newService, detail, actionName, parameter);
            tree.Nodes.Clear();
            details.Clear();
            resources.DataSource = null;
            solutions.DataSource = null;
            version.Clear();
            connectionStatus.Text = "Connection changed. Checking XrmToolBox connection metadata...";
            if (!busy) _ = AttachHostConnectionAsync();
        }

        private async Task AttachHostConnectionAsync()
        {
            if (busy || shuttingDown || Service == null || ConnectionDetail == null) return;
            busy = true;
            pages.Enabled = false;
            var revision = connectionRevision;
            try
            {
                if (ConnectionDetail.AuthType != AuthenticationProviderType.ActiveDirectory)
                    throw new NotSupportedException("This first edition supports on-premises CRM 9.1 Active Directory connections only. Online and IFD connections are not supported.");
                var hostProxy = (Service as CrmServiceClient)?.OrganizationServiceProxy ?? Service as OrganizationServiceProxy
                    ?? throw new NotSupportedException("The connected host did not provide an on-premises organization service proxy.");
                var requestTimeout = ConnectionDetail.Timeout > TimeSpan.Zero
                    ? ConnectionDetail.Timeout : TimeSpan.FromMinutes(2);
                var address = ConnectionDetail.OrganizationServiceUrl;
                var name = ConnectionDetail.OrganizationFriendlyName;
                var serverVersion = ConnectionDetail.OrganizationVersion;
                connectionStatus.Text = "Preparing an independent on-premises service channel...";
                var creation = Task.Run(() =>
                {
                    var toolProxy = new OrganizationServiceProxy(hostProxy.ServiceManagement, hostProxy.ClientCredentials.Clone())
                    {
                        CallerId = hostProxy.CallerId,
                        Timeout = requestTimeout
                    };
                    return CrmSession.AttachOwned(toolProxy, address, name, serverVersion);
                });
                if (await Task.WhenAny(creation, Task.Delay(TimeSpan.FromSeconds(30))) != creation)
                {
                    _ = DisposeCreatedChannelAsync(creation);
                    throw new TimeoutException("The independent CRM service channel could not be prepared within 30 seconds. Reconnect in XrmToolBox and retry.");
                }
                var attached = await creation;
                if (revision != connectionRevision || shuttingDown)
                {
                    _ = DisposeCreatedChannelAsync(Task.FromResult(attached));
                    return;
                }
                workspace = attached;
                connectionStatus.Text = $"{workspace.OrganizationName} | {workspace.ServerVersion} | Development use only";
                operationStatus.Text = "Ready. Refresh the component list you need.";
                AppendLog($"Independent on-premises service channel ready. CRM request timeout: {requestTimeout.TotalSeconds:F0}s. No startup CRM requests.");
            }
            catch (Exception exception)
            {
                if (revision == connectionRevision && !shuttingDown)
                {
                    connectionStatus.Text = "The independent CRM connection could not be prepared. Reconnect using XrmToolBox.";
                    ReportError("Connection preparation", exception);
                }
            }
            finally
            {
                busy = false;
                if (!shuttingDown)
                {
                    pages.Enabled = true;
                    if (revision != connectionRevision) _ = AttachHostConnectionAsync();
                }
            }
        }

        private async Task DisposeCreatedChannelAsync(Task<CrmSession> creation)
        {
            try
            {
                var session = await creation.ConfigureAwait(false);
                await Task.Run(() => session.Dispose()).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                LogError("Discarded CRM channel preparation or disposal failed: " + exception.Message);
            }
        }

        private void BuildPlugins()
        {
            var page = Page("Plugins");
            var split = new SplitContainer { Size = new Size(850, 400), Dock = DockStyle.Fill, SplitterDistance = 360, Panel1MinSize = 180, Panel2MinSize = 180 };
            split.Panel1.Controls.Add(tree);
            split.Panel2.Controls.Add(details);
            var tools = ToolUi.Toolbar();
            AddAction(tools, "Refresh assemblies", LoadAssembliesAsync);
            AddAction(tools, "Refresh branch", RefreshBranchAsync);
            AddAction(tools, "Update DLL", UpdateAssemblyAsync);
            AddAction(tools, "Register DLL", RegisterAssemblyAsync);
            AddAction(tools, "New step", NewStepAsync);
            AddAction(tools, "Edit step / image", EditRegistrationAsync);
            AddAction(tools, "New image", NewImageAsync);
            AddAction(tools, "Enable / disable", SetStepStateAsync);
            page.Controls.Add(split);
            page.Controls.Add(ToolUi.Hint("Expand assembly > type > step > image. Select a registration to view details. Managed registrations are read-only."));
            page.Controls.Add(tools);
        }

        private void BuildResources()
        {
            var page = Page("Web resources");
            var tools = ToolUi.Toolbar();
            AddAction(tools, "Refresh resources", async crm =>
            {
                var items = await crm.GetWebResourcesAsync();
                EnsureCurrent(crm);
                resources.DataSource = items.ToList();
            });
            AddAction(tools, "Upload file", UploadResourceAsync);
            AddAction(tools, "Edit code", EditCodeAsync, true);
            AddAction(tools, "Publish selected", PublishResourceAsync);
            page.Controls.Add(resources);
            page.Controls.Add(ToolUi.Hint("Saving or uploading changes unpublished content only. Publish the selected resource separately when ready."));
            page.Controls.Add(tools);
        }

        private void BuildSolutions()
        {
            var page = Page("Solution exports");
            var tools = ToolUi.Toolbar();
            AddAction(tools, "Refresh solutions", async crm =>
            {
                var items = await crm.GetSolutionsAsync();
                EnsureCurrent(crm);
                solutions.DataSource = items.ToList();
            });
            AddAction(tools, "Export selected", ExportAsync, true);
            tools.Controls.Add(managed);
            tools.Controls.Add(new Label { Text = "New version (optional):", AutoSize = true, Margin = new Padding(8, 8, 0, 0) });
            tools.Controls.Add(version);
            page.Controls.Add(solutions);
            page.Controls.Add(ToolUi.Hint("Export published changes. New version format: 1.0.0.50. A version change is saved permanently in CRM before export."));
            page.Controls.Add(tools);
        }

        private TabPage Page(string name)
        {
            var page = new TabPage(name) { Padding = new Padding(12), BackColor = ToolUi.Canvas };
            pages.TabPages.Add(page);
            return page;
        }

        private void AddAction(FlowLayoutPanel toolbar, string title, Func<CrmSession, Task> action, bool primary = false) =>
            toolbar.Controls.Add(ToolUi.Button(title, () => ExecuteMethod(() => { _ = RunAsync(title, action); }), primary));

        private async Task RunAsync(string title, Func<CrmSession, Task> action)
        {
            if (shuttingDown) return;
            if (busy)
            {
                operationStatus.Text = "Wait for the current operation to finish, then retry.";
                return;
            }
            if (workspace == null) await AttachHostConnectionAsync();
            if (workspace == null)
            {
                AppendLog("Operation blocked: use a verified on-premises CRM 9.1 Active Directory connection.");
                operationStatus.Text = "Blocked: select a supported CRM connection in XrmToolBox.";
                return;
            }
            var crm = workspace;
            var revision = connectionRevision;
            busy = true;
            pages.Enabled = false;
            operationStatus.Text = title + "...";
            var timer = Stopwatch.StartNew();
            AppendLog(title + " started.");
            try
            {
                await action(crm);
                if (revision == connectionRevision && !shuttingDown)
                {
                    operationStatus.Text = $"{title} finished ({timer.Elapsed.TotalSeconds:F1}s)";
                    AppendLog(operationStatus.Text);
                }
            }
            catch (Exception exception)
            {
                if (!shuttingDown) ReportError(title, exception);
            }
            finally
            {
                busy = false;
                if (!shuttingDown)
                {
                    pages.Enabled = true;
                    if (revision != connectionRevision) await AttachHostConnectionAsync();
                }
            }
        }

        private void EnsureCurrent(CrmSession crm)
        {
            if (shuttingDown || !ReferenceEquals(crm, workspace))
                throw new InvalidOperationException("The XrmToolBox connection changed. Refresh the list and retry on the intended organization.");
        }

        private bool Confirm(CrmSession crm, string message)
        {
            EnsureCurrent(crm);
            var accepted = MessageBox.Show(this, $"Development organization: {crm.OrganizationName}\n{crm.Endpoint}\n\n{message}",
                "Confirm CRM Manager operation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            EnsureCurrent(crm);
            return accepted;
        }

        private void ReportError(string operation, Exception exception)
        {
            operationStatus.Text = operation + " failed.";
            AppendLog(operation + " FAILED: " + exception.Message);
            LogError(operation + ": " + exception.Message);
            ShowErrorDialog(exception, operation + " failed", "After a timeout, refresh and verify CRM state before retrying.", false);
        }

        private void AppendLog(string message)
        {
            if (!shuttingDown) log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }

        private void PopulateAssemblies(IReadOnlyList<CrmItem> assemblies)
        {
            tree.Nodes.Clear();
            foreach (var assembly in assemblies) tree.Nodes.Add(Node(assembly));
        }

        private async Task LoadAssembliesAsync(CrmSession crm)
        {
            var items = await crm.GetAssembliesAsync();
            EnsureCurrent(crm);
            PopulateAssemblies(items);
        }

        private static TreeNode Node(object value)
        {
            var label = value switch
            {
                CrmItem assembly => $"{assembly.Name} ({assembly.Version})",
                PluginTypeInfo type => (type.IsWorkflowActivity ? "Workflow: " : "Type: ") + type.Name,
                StepRegistration step => $"{step.Name} [{step.StageName}, {step.ModeName}, {(step.Enabled ? "Enabled" : "Disabled")}]",
                ImageRegistration image => $"{image.Name} [{image.TypeName}, {image.Alias}]",
                _ => throw new ArgumentException("Unsupported registration.")
            };
            var node = new TreeNode(label) { Tag = value };
            if (value is CrmItem || value is StepRegistration || (value is PluginTypeInfo plugin && !plugin.IsWorkflowActivity))
                node.Nodes.Add(new TreeNode("Expand to load..."));
            return node;
        }

        private void Tree_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            if (e.Node.Nodes.Count != 1 || e.Node.Nodes[0].Tag != null) return;
            e.Cancel = true;
            if (!busy) ExecuteMethod(() => { _ = RunAsync("Load registrations", crm => LoadChildrenAsync(crm, e.Node)); });
        }

        private async Task LoadChildrenAsync(CrmSession crm, TreeNode node)
        {
            IEnumerable<object> values;
            switch (node.Tag)
            {
                case CrmItem assembly: values = await crm.GetPluginTypesAsync(assembly.Id); break;
                case PluginTypeInfo type when !type.IsWorkflowActivity: values = await crm.GetStepsAsync(type.Id); break;
                case StepRegistration step: values = await crm.GetImagesAsync(step.Id); break;
                default: throw new InvalidOperationException("Select an assembly, plugin type, or step.");
            }
            EnsureCurrent(crm);
            node.Nodes.Clear();
            foreach (var value in values) node.Nodes.Add(Node(value));
            node.Expand();
        }

        private Task RefreshBranchAsync(CrmSession crm)
        {
            var node = SelectedNode();
            return LoadChildrenAsync(crm, node.Tag is ImageRegistration ? node.Parent : node);
        }

        private TreeNode SelectedNode() => tree.SelectedNode ?? throw new InvalidOperationException("Select a plugin registration first.");

        private TreeNode Ancestor<T>()
        {
            for (var node = SelectedNode(); node != null; node = node.Parent)
                if (node.Tag is T) return node;
            throw new InvalidOperationException("Select a " + typeof(T).Name + " or one of its registrations.");
        }

        private void ShowDetails(TreeNode node)
        {
            details.Text = node.Tag switch
            {
                CrmItem assembly => $"Assembly: {assembly.Name}\r\nVersion: {assembly.Version}\r\nIsolation: {assembly.Details}\r\nID: {assembly.Id}",
                PluginTypeInfo type => $"Type: {type.Name}\r\nWorkflow activity: {type.IsWorkflowActivity}\r\nID: {type.Id}",
                StepRegistration step => $"Step: {step.Name}\r\nMessage: {step.MessageName}\r\nEntity: {step.PrimaryEntity}\r\nStage: {step.StageName}\r\nMode: {step.ModeName}\r\nOrder: {step.Rank}\r\nEnabled: {step.Enabled}\r\nEditable: {step.CanEdit}\r\nFiltering attributes: {step.FilteringAttributes}\r\nID: {step.Id}\r\n\r\n{step.Description}",
                ImageRegistration image => $"Image: {image.Name}\r\nType: {image.TypeName}\r\nAlias: {image.Alias}\r\nProperty: {image.MessagePropertyName}\r\nAttributes: {(image.AllAttributes ? "(all)" : image.Attributes)}\r\nEditable: {image.CanEdit}\r\nID: {image.Id}",
                _ => ""
            };
        }

        private static string? ChooseFile(IWin32Window owner, string filter)
        {
            using (var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true })
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
        }

        private async Task UpdateAssemblyAsync(CrmSession crm)
        {
            var assembly = (CrmItem)Ancestor<CrmItem>().Tag;
            var path = ChooseFile(this, "Plugin assembly (*.dll)|*.dll");
            if (path == null || !Confirm(crm, $"Update '{assembly.Name}' from '{Path.GetFileName(path)}'?\nExisting steps and images are retained.")) return;
            AppendLog(await crm.UpdateAssemblyAsync(assembly, path) ? "Assembly updated." : "Skipped: unchanged assembly.");
            EnsureCurrent(crm);
            await LoadAssembliesAsync(crm);
        }

        private async Task RegisterAssemblyAsync(CrmSession crm)
        {
            using (var form = new AssemblyRegistrationForm())
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                var path = ChooseFile(this, "Signed plugin assembly (*.dll)|*.dll");
                if (path == null || !Confirm(crm, $"Register '{Path.GetFileName(path)}' and entered types?\nNo execution steps are created.")) return;
                await crm.RegisterAssemblyAsync(path, form.TypeNames, form.Sandbox);
                EnsureCurrent(crm);
                await LoadAssembliesAsync(crm);
            }
        }

        private async Task NewStepAsync(CrmSession crm)
        {
            var node = Ancestor<PluginTypeInfo>();
            var type = (PluginTypeInfo)node.Tag;
            if (type.IsWorkflowActivity) throw new InvalidOperationException("Workflow activity registration is not supported.");
            var model = new StepRegistration { PluginTypeId = type.Id };
            using (var editor = new RegistrationForm(crm, model))
            {
                if (!await SaveWithReviewAsync(crm, editor, () => StepSummary(model),
                    async () => { await crm.SaveStepAsync(model); }, editor.RetainForRetry)) return;
                await LoadChildrenAsync(crm, node);
            }
        }

        private async Task EditRegistrationAsync(CrmSession crm)
        {
            var node = SelectedNode();
            StepRegistration step;
            ImageRegistration? image = null;
            if (node.Tag is StepRegistration selectedStep) step = await crm.GetStepAsync(selectedStep.Id);
            else if (node.Tag is ImageRegistration selectedImage)
            {
                step = await crm.GetStepAsync(selectedImage.StepId, false);
                image = await crm.GetImageAsync(selectedImage.Id);
            }
            else throw new InvalidOperationException("Select a step or image to edit.");
            EnsureCurrent(crm);
            if (!step.CanEdit || (image != null && !image.CanEdit)) throw new InvalidOperationException("This registration is read-only.");
            using (var editor = new RegistrationForm(crm, step, image))
            {
                if (!await SaveWithReviewAsync(crm, editor, () => image == null ? StepSummary(step) : ImageSummary(image, step),
                    async () => { if (image == null) await crm.SaveStepAsync(step); else await crm.SaveImageAsync(image); }, editor.RetainForRetry)) return;
                await LoadChildrenAsync(crm, node.Parent);
            }
        }

        private async Task NewImageAsync(CrmSession crm)
        {
            var node = Ancestor<StepRegistration>();
            var step = await crm.GetStepAsync(((StepRegistration)node.Tag).Id, false);
            EnsureCurrent(crm);
            if (!step.CanEdit) throw new InvalidOperationException("This step is read-only.");
            var image = new ImageRegistration { StepId = step.Id, ImageType = step.MessageName == "Create" ? 1 : 0 };
            if (step.MessageName != "Create" && step.MessageName != "Update" && step.MessageName != "Delete") image.MessagePropertyName = "";
            using (var editor = new RegistrationForm(crm, step, image))
            {
                if (!await SaveWithReviewAsync(crm, editor, () => ImageSummary(image, step),
                    async () => { await crm.SaveImageAsync(image); }, editor.RetainForRetry)) return;
                await LoadChildrenAsync(crm, node);
            }
        }

        private async Task SetStepStateAsync(CrmSession crm)
        {
            var node = Ancestor<StepRegistration>();
            var step = await crm.GetStepAsync(((StepRegistration)node.Tag).Id, false);
            EnsureCurrent(crm);
            if (!Confirm(crm, $"{(step.Enabled ? "Disable" : "Enable")} step '{step.Name}'?")) return;
            await crm.SetStepEnabledAsync(step.Id, !step.Enabled);
            await LoadChildrenAsync(crm, node.Parent);
        }

        private static CrmItem Selected(DataGridView grid) => grid.CurrentRow?.DataBoundItem as CrmItem
            ?? throw new InvalidOperationException("Select a row first.");

        private async Task UploadResourceAsync(CrmSession crm)
        {
            var item = Selected(resources);
            var path = ChooseFile(this, "Web resource files|*.js;*.html;*.htm;*.css;*.xml;*.png;*.jpg;*.jpeg;*.gif;*.xap;*.xsl;*.xslt;*.ico;*.svg;*.resx|All files|*.*");
            if (path == null || !Confirm(crm, $"Replace unpublished content of '{item.Name}' from '{Path.GetFileName(path)}'?\nThis does not publish.")) return;
            AppendLog(await crm.UpdateWebResourceAsync(item, path) ? "Uploaded. NOT published." : "Skipped: unchanged content.");
        }

        private async Task EditCodeAsync(CrmSession crm)
        {
            var item = Selected(resources);
            var document = await crm.GetWebResourceTextAsync(item.Id);
            EnsureCurrent(crm);
            using (var editor = new CodeEditorForm(document))
            {
                await SaveWithReviewAsync(crm, editor, () => $"Save unpublished content for '{item.Name}'?\nThis does not publish.",
                    async () => AppendLog(await crm.SaveWebResourceTextAsync(document, editor.Code) ? "Saved. NOT published." : "Skipped: unchanged content."),
                    editor.RetainForRetry);
            }
        }

        private async Task<bool> SaveWithReviewAsync(CrmSession crm, Form editor, Func<string> summary, Func<Task> save, Action retain)
        {
            while (true)
            {
                EnsureCurrent(crm);
                if (editor.ShowDialog(this) != DialogResult.OK || !Confirm(crm, summary())) return false;
                try
                {
                    await save();
                    return true;
                }

                catch (Exception exception)
                {
                    EnsureCurrent(crm);
                    ReportError("Save", exception);
                    retain();
                }
            }
        }

        private static string StepSummary(StepRegistration step) =>
            $"Save step '{step.Name}'?\n{step.MessageName} | Filter ID: {step.FilterId?.ToString() ?? "(global / no entity)"}\n"
            + $"{step.StageName} | {step.ModeName} | Order {step.Rank}";

        private static string ImageSummary(ImageRegistration image, StepRegistration step) =>
            $"Save image '{image.Name}' for step '{step.Name}'?\n{image.TypeName} | Alias: {image.Alias}\n"
            + (image.AllAttributes ? "WARNING: All attributes are included." : "Attributes: " + image.Attributes);

        private async Task PublishResourceAsync(CrmSession crm)
        {
            var item = Selected(resources);
            if (Confirm(crm, "Publish pending changes for '" + item.Name + "'?")) await crm.PublishWebResourceAsync(item.Id);
        }

        private async Task ExportAsync(CrmSession crm)
        {
            var item = Selected(solutions);
            var newVersion = string.IsNullOrWhiteSpace(version.Text) ? null : DeploymentRules.NormalizeSolutionVersion(version.Text);
            using (var dialog = new SaveFileDialog
            {
                Filter = "Solution ZIP (*.zip)|*.zip", AddExtension = true, DefaultExt = ".zip", OverwritePrompt = true,
                FileName = $"{item.Name}_{newVersion ?? item.Version}_{(managed.Checked ? "managed" : "unmanaged")}.zip"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || !Confirm(crm, $"Export '{item.DisplayName}' as {(managed.Checked ? "managed" : "unmanaged")}?"
                    + (newVersion != null && newVersion != item.Version ? $"\nVersion: {item.Version} -> {newVersion}\nPermanent CRM change; remains if export fails." : "\nVersion unchanged.")
                    + "\nOnly published changes are exported. No deployment is performed.")) return;
                if (newVersion != null && await crm.UpdateSolutionVersionAsync(item, newVersion))
                {
                    EnsureCurrent(crm);
                    item.Version = newVersion;
                    AppendLog("Version saved in CRM: " + newVersion + ". This remains if export fails.");
                    solutions.Refresh();
                }
                EnsureCurrent(crm);
                await crm.ExportSolutionAsync(item, managed.Checked, dialog.FileName);
                AppendLog("Export saved: " + dialog.FileName);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                shuttingDown = true;
                connectionRevision++;
                var previous = workspace;
                workspace = null;
                if (previous != null) _ = Task.Run(() => previous.Dispose());
            }
            base.Dispose(disposing);
        }
    }
}
