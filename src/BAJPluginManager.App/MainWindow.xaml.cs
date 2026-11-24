using BAJPluginManager.Core;
using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Net;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Xml;

namespace BAJPluginManager.App
{
    public partial class MainWindow : Window
    {
        private readonly CrmSession session = new CrmSession();
        private bool busy;
        private int currentPage;
        private readonly ConnectionProfileStore profileStore;
        private SavedConnections savedConnections = new SavedConnections();
        private bool loadingProfile;
        private bool loadedPassword;
        private bool profileLoadFailed;

        public MainWindow() : this(new ConnectionProfileStore(ConnectionProfileStore.DefaultPath)) { }

        public MainWindow(ConnectionProfileStore profileStore)
        {
            this.profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
            InitializeComponent();
            AccountDomain.TextChanged += ConnectionSettings_Changed;
            AccountUsername.TextChanged += ConnectionSettings_Changed;
            AccountPassword.PasswordChanged += ConnectionSettings_Changed;
            OrganizationUrl.TextChanged += ConnectionSettings_Changed;
            SavedConnectionName.TextChanged += ConnectionSettings_Changed;
            foreach (var item in new[] { ConnectionNav, PluginsNav, ResourcesNav, SolutionsNav })
                item.Checked += Navigation_Changed;
            AssemblySearch.TextChanged += AssemblySearch_Changed;
            ResourceSearch.TextChanged += ResourceSearch_Changed;
            SolutionSearch.TextChanged += SolutionSearch_Changed;
            SetEnabledState();
            UpdateListStates();
            Log("Client libraries: CRM SDK "
                + FileVersionInfo.GetVersionInfo(typeof(Microsoft.Xrm.Sdk.IOrganizationService).Assembly.Location).FileVersion
                + "; XRM Tooling "
                + FileVersionInfo.GetVersionInfo(typeof(Microsoft.Xrm.Tooling.Connector.CrmServiceClient).Assembly.Location).FileVersion);
            LoadSavedConnections();
            SetEnabledState();
        }

        private void LoadSavedConnections()
        {
            try
            {
                savedConnections = profileStore.Load();
                RefreshSavedConnections();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
                || exception is CryptographicException || exception is SerializationException
                || exception is XmlException || exception is ArgumentException)
            {
                profileLoadFailed = true;
                AccountPassword.Clear();
                ConnectionStatus.Text = "Saved connections could not be loaded. You can connect manually, or use Forget all to reset the saved file.";
                Log("Saved connection loading failed: " + exception.GetType().Name + ". No saved data was replaced.");
                ActivityPanel.IsExpanded = true;
            }
        }

        private void RefreshSavedConnections()
        {
            loadingProfile = true;
            try
            {
                SavedConnectionSelector.ItemsSource = savedConnections.Connections.ToArray();
                var selected = savedConnections.Connections.FirstOrDefault(connection => connection.Id == savedConnections.SelectedId);
                SavedConnectionSelector.SelectedItem = selected;
                ApplySavedConnection(selected);
            }
            finally { loadingProfile = false; }
        }

        private void ApplySavedConnection(SavedConnection? connection)
        {
            AccountPassword.Clear();
            loadedPassword = false;
            SavedConnectionName.Text = connection?.Name ?? "";
            OrganizationUrl.Text = connection?.Address ?? "";
            AccountDomain.Text = connection?.Domain ?? "";
            AccountUsername.Text = connection?.Username ?? "";
            if (connection != null)
            {
                using (var password = connection.OpenPassword())
                    AccountPassword.Password = new NetworkCredential("", password).Password;
                loadedPassword = true;
            }
            ConnectionStatus.Text = connection == null ? "Enter your organization URL, username, and password to connect."
                : "Saved connection loaded. Check the organization URL, then click Connect to CRM.";
        }

        private async void SavedConnection_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (loadingProfile || !(SavedConnectionSelector.SelectedItem is SavedConnection selected)) return;
            await ExecuteAsync("Selecting saved connection", () =>
            {
                RequireProfileEditing();
                loadingProfile = true;
                try { ApplySavedConnection(selected); }
                finally { loadingProfile = false; }
                var updated = savedConnections.Copy();
                updated.SelectedId = selected.Id;
                profileStore.Save(updated);
                savedConnections = updated;
                return Task.FromResult(true);
            });
        }

        private void NewConnection_Click(object sender, RoutedEventArgs e)
        {
            loadingProfile = true;
            try
            {
                SavedConnectionSelector.SelectedItem = null;
                ApplySavedConnection(null);
            }
            finally { loadingProfile = false; }
            SetEnabledState();
            OrganizationUrl.Focus();
        }

        private void RequireProfileEditing()
        {
            if (session.IsConnected) throw new InvalidOperationException("Disconnect before changing saved connections.");
            if (profileLoadFailed) throw new InvalidOperationException("Use Forget all to reset the unreadable saved connection file before saving.");
        }

        private async void SaveConnection_Click(object sender, RoutedEventArgs e) =>
            await ExecuteAsync("Saving connection", () =>
            {
                RequireProfileEditing();
                var selected = SavedConnectionSelector.SelectedItem as SavedConnection;
                SavedConnection connection;
                using (var password = AccountPassword.SecurePassword)
                    connection = SavedConnection.Create(selected?.Id ?? Guid.NewGuid(), SavedConnectionName.Text,
                        OrganizationUrl.Text, AccountDomain.Text, AccountUsername.Text, password);
                if (MessageBox.Show(this,
                    $"{(selected == null ? "Save" : "Replace")} connection '{connection.Name}'?\n\n"
                    + connection.Address + "\n\n"
                    + "This saves the URL, domain, username and password on this PC, encrypted for your Windows account. "
                    + "Anyone using that Windows account may access them. Saving does not verify or connect to CRM.",
                    "Save connection credentials", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                    return Task.FromResult(false);
                var updated = savedConnections.Copy();
                var index = updated.Connections.FindIndex(item => item.Id == connection.Id);
                if (index < 0) updated.Connections.Add(connection);
                else updated.Connections[index] = connection;
                updated.SelectedId = connection.Id;
                profileStore.Save(updated);
                savedConnections = updated;
                RefreshSavedConnections();
                ConnectionStatus.Text = "Connection saved for this Windows account. Click Connect to CRM when ready.";
                return Task.FromResult(true);
            });

        private async void DeleteConnection_Click(object sender, RoutedEventArgs e) =>
            await ExecuteAsync("Deleting saved connection", () =>
            {
                RequireProfileEditing();
                var selected = SavedConnectionSelector.SelectedItem as SavedConnection
                    ?? throw new InvalidOperationException("Select a saved connection first.");
                if (MessageBox.Show(this, $"Forget saved connection '{selected.Name}' and its stored password?\n\n"
                    + "Only this local profile is removed. No CRM data is changed.", "Delete saved connection",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                    return Task.FromResult(false);
                var updated = savedConnections.Copy();
                updated.Connections.RemoveAll(connection => connection.Id == selected.Id);
                updated.SelectedId = updated.Connections.FirstOrDefault()?.Id ?? Guid.Empty;
                if (updated.Connections.Count == 0) profileStore.ForgetAll();
                else profileStore.Save(updated);
                savedConnections = updated;
                RefreshSavedConnections();
                return Task.FromResult(true);
            });

        private async void ForgetConnections_Click(object sender, RoutedEventArgs e) =>
            await ExecuteAsync("Forgetting saved connections", () =>
            {
                if (session.IsConnected) throw new InvalidOperationException("Disconnect before forgetting saved connections.");
                if (MessageBox.Show(this, "Forget all locally saved connections and passwords?\n\nNo CRM data is changed.",
                    "Forget saved connections", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                    return Task.FromResult(false);
                profileStore.ForgetAll();
                savedConnections = new SavedConnections();
                profileLoadFailed = false;
                RefreshSavedConnections();
                return Task.FromResult(true);
            });

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteAsync(session.IsConnected ? "Disconnecting" : "Connecting", async () =>
            {
                if (session.IsConnected)
                {
                    await session.DisconnectAsync();
                    Plugins.ItemsSource = null;
                    PluginDetails.Text = "";
                    WebResources.ItemsSource = null;
                    Solutions.ItemsSource = null;
                    ConnectionStatus.Text = "Disconnected.";
                    AssemblySearch.Clear();
                    ResourceSearch.Clear();
                    SolutionSearch.Clear();
                    ConnectionNav.IsChecked = true;
                    UpdateListStates();
                    if (SavedConnectionSelector.SelectedItem is SavedConnection selected
                        && OrganizationUrl.Text.Trim() == selected.Address
                        && AccountDomain.Text.Trim() == selected.Domain
                        && AccountUsername.Text.Trim() == selected.Username)
                    {
                        using (var password = selected.OpenPassword())
                            AccountPassword.Password = new NetworkCredential("", password).Password;
                        loadedPassword = true;
                    }
                }
                else
                {
                    var progress = new Progress<string>(message =>
                    {
                        OperationStatus.Text = message + "...";
                        Log(message);
                    });
                    using (var password = AccountPassword.SecurePassword)
                    {
                        AccountPassword.Clear();
                        await session.ConnectAsync(OrganizationUrl.Text, AccountDomain.Text, AccountUsername.Text, password, progress);
                    }
                    ConnectionStatus.Text = $"{session.OrganizationName} | {session.ServerVersion} | {session.Endpoint}";
                    PluginsNav.IsChecked = true;
                    Log("Connected. Refresh the component list you need.");
                }

                return true;
            });
        }

        private async void RefreshAssemblies_Click(object sender, RoutedEventArgs e) =>
            await ExecuteAsync("Loading assemblies", LoadAssembliesAsync);

        private async Task LoadAssembliesAsync()
        {
            Plugins.ItemsSource = (await session.GetAssembliesAsync()).Select(assembly => new PluginNode(assembly)).ToArray();
            PluginDetails.Text = "Expand an assembly to load plugin types, then expand a plugin type to load its execution steps.";
            ApplyAssemblyFilter();
            UpdatePluginActions();
        }

        private async void PluginNode_Expanded(object sender, RoutedEventArgs e)
        {
            if (!(e.OriginalSource is TreeViewItem item) || !(item.DataContext is PluginNode node)
                || node.Loaded || node.Value == null || busy) return;
            e.Handled = true;
            await ExecuteAsync("Loading plugin registrations", () => LoadPluginChildrenAsync(node));
        }

        private async Task LoadPluginChildrenAsync(PluginNode node)
        {
            IEnumerable<PluginNode> children;
            switch (node.Value)
            {
                case CrmItem assembly:
                    children = (await session.GetPluginTypesAsync(assembly.Id)).Select(value => new PluginNode(value, node));
                    break;
                case PluginTypeInfo type when !type.IsWorkflowActivity:
                    children = (await session.GetStepsAsync(type.Id)).Select(value => new PluginNode(value, node));
                    break;
                case StepRegistration step:
                    children = (await session.GetImagesAsync(step.Id)).Select(value => new PluginNode(value, node));
                    break;
                default: throw new InvalidOperationException("Select an assembly, plugin type, or step to refresh its children.");
            }
            var loaded = children.ToArray();
            node.Children.Clear();
            foreach (var child in loaded) node.Children.Add(child);
            node.Loaded = true;
            node.IsExpanded = true;
            ShowPluginDetails(Plugins.SelectedItem as PluginNode);
        }

        private async void RefreshPluginNode_Click(object sender, RoutedEventArgs e) =>
            await ExecuteAsync("Refreshing selected registration", async () =>
            {
                var node = SelectedPluginNode();
                await LoadPluginChildrenAsync(node.Value is ImageRegistration ? node.Parent! : node);
            });

        private PluginNode SelectedPluginNode() =>
            Plugins.SelectedItem as PluginNode ?? throw new InvalidOperationException("Select a plugin registration first.");

        private void PluginSelection_Changed(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            ShowPluginDetails(e.NewValue as PluginNode);
            UpdatePluginActions();
        }

        private void ShowPluginDetails(PluginNode? node)
        {
            PluginDetails.Text = node?.Value switch
            {
                CrmItem assembly => $"Assembly: {assembly.Name}\nVersion: {assembly.Version}\nIsolation: {assembly.Details}\nID: {assembly.Id}\n\nExpand to load registered plugin types.",
                PluginTypeInfo type => $"Type: {type.Name}\nID: {type.Id}\n\n{(type.IsWorkflowActivity ? "Workflow activities do not have plugin execution steps." : "Expand to load steps. Select New step to register an execution step.")}",
                StepRegistration step => $"Step: {step.Name}\nMessage: {step.MessageName}\nEntity: {(string.IsNullOrEmpty(step.PrimaryEntity) ? "(global / no primary entity)" : step.PrimaryEntity)}\nStage: {step.StageName}\nMode: {step.ModeName}\nOrder: {step.Rank}\nState: {(step.Enabled ? "Enabled" : "Disabled")}\nFiltering attributes: {(string.IsNullOrEmpty(step.FilteringAttributes) ? "(all updates)" : step.FilteringAttributes)}\nEditable: {step.CanEdit}\nID: {step.Id}\n\n{step.Description}\n\nExpand to load images. Configuration is loaded only when opening the editor.",
                ImageRegistration image => $"Image: {image.Name}\nType: {image.TypeName}\nAlias: {image.Alias}\nMessage property: {image.MessagePropertyName}\nAttributes: {(image.AllAttributes ? "(all attributes)" : image.Attributes)}\nEditable: {image.CanEdit}\nID: {image.Id}",
                _ => "Select an assembly, plugin type, execution step, or image."
            };
        }

        private void UpdatePluginActions()
        {
            var node = Plugins.SelectedItem as PluginNode;
            var type = node?.Ancestor<PluginTypeInfo>()?.Value as PluginTypeInfo;
            var step = node?.Ancestor<StepRegistration>()?.Value as StepRegistration;
            NewStepButton.IsEnabled = !busy && session.IsConnected && type != null && !type.IsWorkflowActivity;
            NewImageButton.IsEnabled = !busy && session.IsConnected && step?.CanEdit == true;
            StepStateButton.IsEnabled = !busy && session.IsConnected && step?.CanEdit == true;
            UnregisterStepButton.IsEnabled = !busy && session.IsConnected
                && node?.Value is StepRegistration selectedRegistration && selectedRegistration.CanEdit;
            UnregisterPluginTypeButton.IsEnabled = !busy && session.IsConnected
                && node?.Value is PluginTypeInfo selectedType && !selectedType.IsWorkflowActivity;
            StepStateButton.Content = step?.Enabled == true ? "Disable step..." : "Enable step...";
            EditRegistrationButton.IsEnabled = !busy && session.IsConnected
                && ((node?.Value is StepRegistration selectedStep && selectedStep.CanEdit)
                    || (node?.Value is ImageRegistration image && image.CanEdit && step?.CanEdit == true));
            UpdateAssemblyButton.IsEnabled = !busy && session.IsConnected && node?.Ancestor<CrmItem>() != null;
            RefreshNodeButton.IsEnabled = !busy && session.IsConnected
                && (node?.Value is CrmItem || node?.Value is StepRegistration || node?.Value is ImageRegistration
                    || (node?.Value is PluginTypeInfo plugin && !plugin.IsWorkflowActivity));
        }

        private async void NewStep_Click(object sender, RoutedEventArgs e) =>
            await ExecuteAsync("Registering plugin step", async () =>
            {
                var typeNode = SelectedPluginNode().Ancestor<PluginTypeInfo>()
                    ?? throw new InvalidOperationException("Select a plugin type first.");
                if (!(typeNode.Value is PluginTypeInfo type) || type.IsWorkflowActivity)
                    throw new InvalidOperationException("Select an IPlugin type, not a workflow activity.");
                var editor = new RegistrationEditorWindow(session, new StepRegistration { PluginTypeId = type.Id }) { Owner = this };
                if (editor.ShowDialog() != true) return false;
                await LoadPluginChildrenAsync(typeNode);
                return true;
            });

        private async void EditRegistration_Click(object sender, RoutedEventArgs e) =>
            await ExecuteAsync("Editing plugin registration", async () =>
            {
                var node = SelectedPluginNode();
                RegistrationEditorWindow editor;
                if (node.Value is StepRegistration step)
                    editor = new RegistrationEditorWindow(session, await session.GetStepAsync(step.Id));
                else if (node.Value is ImageRegistration image)
                    editor = new RegistrationEditorWindow(session, await session.GetStepAsync(image.StepId, false), await session.GetImageAsync(image.Id));
                else throw new InvalidOperationException("Select a step or image to edit.");
                editor.Owner = this;
                if (editor.ShowDialog() != true) return false;
                await LoadPluginChildrenAsync(node.Parent ?? throw new InvalidOperationException("The registration has no parent."));
                return true;
            });

        private async void NewImage_Click(object sender, RoutedEventArgs e) =>
            await ExecuteAsync("Registering step image", async () =>
            {
                var node = SelectedPluginNode().Ancestor<StepRegistration>()
                    ?? throw new InvalidOperationException("Select a plugin step first.");
                var selected = (StepRegistration)node.Value!;
                var step = await session.GetStepAsync(selected.Id, false);
                var targetMessage = step.MessageName == "Create" || step.MessageName == "Update" || step.MessageName == "Delete";
                var image = new ImageRegistration
                {
                    StepId = step.Id, ImageType = step.MessageName == "Create" ? 1 : 0,
                    MessagePropertyName = targetMessage ? "Target" : ""
                };
                var editor = new RegistrationEditorWindow(session, step, image) { Owner = this };
                if (editor.ShowDialog() != true) return false;
                await LoadPluginChildrenAsync(node);
                return true;
            });

        private async void StepState_Click(object sender, RoutedEventArgs e) =>
            await ExecuteAsync("Changing step state", async () =>
            {
                var node = SelectedPluginNode().Ancestor<StepRegistration>()
                    ?? throw new InvalidOperationException("Select a step first.");
                var step = await session.GetStepAsync(((StepRegistration)node.Value!).Id, false);
                if (!Confirm($"{(step.Enabled ? "Disable" : "Enable")} step '{step.Name}'?\n\nThis changes whether the plugin executes.")) return false;
                await session.SetStepEnabledAsync(step.Id, !step.Enabled);
                await LoadPluginChildrenAsync(node.Parent ?? throw new InvalidOperationException("The step has no plugin type."));
                return true;
            });

        private async void UnregisterStep_Click(object sender, RoutedEventArgs e) =>
            await ExecuteAsync("Unregistering plugin step", async () =>
            {
                var node = SelectedPluginNode();
                if (!(node.Value is StepRegistration selected))
                    throw new InvalidOperationException("Select the step itself to unregister it.");
                var parent = node.Parent ?? throw new InvalidOperationException("The step has no plugin type.");
                var step = await session.GetStepAsync(selected.Id, false);
                if (!step.CanEdit)
                    throw new InvalidOperationException("Only unmanaged, customizable registrations can be unregistered.");
                if (!Confirm($"Permanently unregister step '{step.Name}'?\n\n"
                    + $"Message: {step.MessageName}\nStage: {step.StageName}\nMode: {step.ModeName}\nStep ID: {step.Id}\n\n"
                    + "CRM also removes images belonging to this step. Its plugin type and assembly are retained.\n"
                    + "This cannot be undone here. Disable the step instead if you only want to stop execution."))
                    return false;
                await session.UnregisterStepAsync(step);
                Log("Plugin step unregistered. Refreshing the parent plugin type.");
                parent.IsSelected = true;
                ShowPluginDetails(parent);
                await LoadPluginChildrenAsync(parent);
                return true;
            });

        private async void UnregisterPluginType_Click(object sender, RoutedEventArgs e) =>
            await ExecuteAsync("Unregistering plugin type", async () =>
            {
                var node = SelectedPluginNode();
                if (!(node.Value is PluginTypeInfo type) || type.IsWorkflowActivity)
                    throw new InvalidOperationException("Select the IPlugin type itself, not an assembly, step, image or workflow activity.");
                var parent = node.Parent ?? throw new InvalidOperationException("The plugin type has no assembly.");
                if (!(parent.Value is CrmItem assembly))
                    throw new InvalidOperationException("Select a plugin type belonging to an assembly.");
                if (!Confirm($"Permanently unregister plugin type '{type.Name}'?\n\n"
                    + $"Assembly: {assembly.Name}\nType ID: {type.Id}\n\n"
                    + "All steps for this type and their images will also be removed. The assembly DLL and other types are retained.\n"
                    + "This cannot be undone here. Disable individual steps instead if you only want to stop execution."))
                    return false;
                await session.UnregisterPluginTypeAsync(type, assembly.Id);
                Log("Plugin type and its steps unregistered. Refreshing the parent assembly.");
                parent.IsSelected = true;
                ShowPluginDetails(parent);
                await LoadPluginChildrenAsync(parent);
                return true;
            });

        private async void RefreshResources_Click(object sender, RoutedEventArgs e) =>
            await ExecuteAsync("Loading web resources", async () =>
            {
                WebResources.ItemsSource = await session.GetWebResourcesAsync();
                ApplyRecordFilter(WebResources, ResourceSearch.Text);
            });

        private async void RefreshSolutions_Click(object sender, RoutedEventArgs e) =>
            await ExecuteAsync("Loading solutions", async () =>
            {
                Solutions.ItemsSource = await session.GetSolutionsAsync();
                ApplyRecordFilter(Solutions, SolutionSearch.Text);
            });

        private async void UpdateAssembly_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteAsync("Updating plugin assembly", async () =>
            {
                var selected = SelectedPluginNode().Ancestor<CrmItem>()?.Value as CrmItem
                    ?? throw new InvalidOperationException("Select an assembly or one of its registrations first.");
                var file = SelectFile("Plugin assemblies (*.dll)|*.dll");
                if (file == null || !Confirm($"Update assembly '{selected.Name}' using '{Path.GetFileName(file)}'?\n\nExisting steps and images will be retained."))
                {
                    return false;
                }

                var changed = await session.UpdateAssemblyAsync(selected, file);
                Log(changed ? "Assembly updated. Existing registrations retained." : "Skipped: assembly content is unchanged.");
                if (changed)
                {
                    await LoadAssembliesAsync();
                }

                return true;
            });
        }

        private async void RegisterAssembly_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteAsync("Registering plugin assembly", async () =>
            {
                DeploymentRules.ParseTypeNames(PluginTypeNames.Text);
                var file = SelectFile("Plugin assemblies (*.dll)|*.dll");
                if (file == null || !Confirm($"Register '{Path.GetFileName(file)}' and the entered plugin types?\n\nAssembly/type creation is transactional. Execution steps are NOT created."))
                {
                    return false;
                }

                await session.RegisterAssemblyAsync(file, PluginTypeNames.Text, SandboxIsolation.IsChecked == true);
                Log("Assembly and plugin types registered. Expand the assembly, select a plugin type, then choose New step.");
                await LoadAssembliesAsync();
                return true;
            });
        }

        private async void UploadResource_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteAsync("Uploading web resource", async () =>
            {
                var selected = RequireSelection(WebResources, "web resource");
                var file = SelectFile("Web resource files|*.js;*.html;*.htm;*.css;*.xml;*.png;*.jpg;*.jpeg;*.gif;*.xap;*.xsl;*.xslt;*.ico;*.svg;*.resx|All files|*.*");
                if (file == null || !Confirm($"Replace the unpublished content of '{selected.Name}' using '{Path.GetFileName(file)}'?\n\nThis does not publish the resource."))
                {
                    return false;
                }

                var changed = await session.UpdateWebResourceAsync(selected, file);
                Log(changed ? "Resource uploaded, NOT published. Use Publish selected when ready." : "Skipped: unpublished content is unchanged. Publish separately if needed.");
                return true;
            });
        }

        private async void EditResource_Click(object sender, RoutedEventArgs e) =>
            await ExecuteAsync("Editing web resource", async () =>
            {
                var resource = RequireSelection(WebResources, "text web resource");
                var document = await session.GetWebResourceTextAsync(resource.Id);
                var editor = new WebResourceEditorWindow(session, document) { Owner = this };
                if (editor.ShowDialog() != true) return false;
                Log(editor.Changed ? "Edited resource saved, NOT published. Use Publish selected when ready." : "Skipped: unpublished content is unchanged.");
                return true;
            });

        private void WebResourceSelection_Changed(object sender, SelectionChangedEventArgs e) => UpdateWebResourceActions();

        private void UpdateWebResourceActions()
        {
            var resource = WebResources.SelectedItem as CrmItem;
            var selected = !busy && session.IsConnected && resource != null;
            UploadResourceButton.IsEnabled = selected;
            PublishResourceButton.IsEnabled = selected;
            EditResourceButton.IsEnabled = selected && WebResourceDocument.IsTextType(resource!.Type);
        }

        private void SolutionSelection_Changed(object sender, SelectionChangedEventArgs e)
        {
            SolutionVersion.Clear();
            UpdateSolutionActions();
        }

        private void UpdateSolutionActions()
        {
            ExportSolutionButton.IsEnabled = !busy && session.IsConnected && Solutions.SelectedItem is CrmItem;
            SolutionVersion.IsEnabled = ExportSolutionButton.IsEnabled;
        }

        private void Navigation_Changed(object sender, RoutedEventArgs e)
        {
            if (!(sender is RadioButton item) || !int.TryParse(item.Tag?.ToString(), out var page)) return;
            currentPage = page;
            ConnectionPage.Visibility = page == 0 ? Visibility.Visible : Visibility.Collapsed;
            PluginWorkspace.Visibility = page == 1 ? Visibility.Visible : Visibility.Collapsed;
            ResourceWorkspace.Visibility = page == 2 ? Visibility.Visible : Visibility.Collapsed;
            SolutionWorkspace.Visibility = page == 3 ? Visibility.Visible : Visibility.Collapsed;
            var titles = new[] { "Connect to your CRM", "Plugins & registrations", "Web resources", "Solution exports" };
            var descriptions = new[]
            {
                "A secure connection to your on-premises development organization.",
                "Explore assemblies, manage execution steps, and keep images in context.",
                "Find your resource. Edit or upload. Publish when you are ready.",
                "Package published changes for your manual deployment workflow."
            };
            PageTitle.Text = titles[page];
            PageDescription.Text = descriptions[page];
            UpdateConnectionNotice();
        }

        private void GoToConnection_Click(object sender, RoutedEventArgs e) => ConnectionNav.IsChecked = true;

        private void UpdateConnectionNotice() =>
            ConnectionRequired.Visibility = !session.IsConnected && currentPage != 0 ? Visibility.Visible : Visibility.Collapsed;

        private void AssemblySearch_Changed(object sender, TextChangedEventArgs e) => ApplyAssemblyFilter();

        private void ApplyAssemblyFilter()
        {
            if (Plugins.ItemsSource != null)
                CollectionViewSource.GetDefaultView(Plugins.ItemsSource).Filter = value =>
                    value is PluginNode node && node.Value is CrmItem assembly && Matches(AssemblySearch.Text, assembly.Name);
            UpdateListStates();
            UpdatePluginActions();
        }

        private void ResourceSearch_Changed(object sender, TextChangedEventArgs e) => ApplyRecordFilter(WebResources, ResourceSearch.Text);

        private void SolutionSearch_Changed(object sender, TextChangedEventArgs e) => ApplyRecordFilter(Solutions, SolutionSearch.Text);

        private void ApplyRecordFilter(DataGrid grid, string query)
        {
            if (grid.ItemsSource != null)
                CollectionViewSource.GetDefaultView(grid.ItemsSource).Filter = value => value is CrmItem item
                    && Matches(query, item.Name, item.DisplayName, item.Details, item.Version);
            UpdateListStates();
            UpdateWebResourceActions();
            UpdateSolutionActions();
        }

        private static bool Matches(string query, params string[] fields) =>
            fields.Any(field => field.IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);

        private void UpdateListStates()
        {
            UpdateListState(Plugins, AssemblyCount, PluginEmpty, PluginEmptyText, "assemblies",
                "Refresh assemblies to begin. Expand an assembly to explore types, steps, and images.");
            UpdateListState(WebResources, ResourceCount, ResourceEmpty, ResourceEmptyText, "web resources",
                "Refresh to browse existing customizable resources, then select a file to edit or upload.");
            UpdateListState(Solutions, SolutionCount, SolutionEmpty, SolutionEmptyText, "solutions",
                "Refresh solutions, select one, and export a managed or unmanaged ZIP. Deploy it manually when ready.");
        }

        private static void UpdateListState(ItemsControl control, TextBlock count, FrameworkElement empty,
            TextBlock hint, string noun, string initialHint)
        {
            var total = control.ItemsSource?.Cast<object>().Count() ?? 0;
            count.Text = control.ItemsSource == null ? "Not loaded" : $"{control.Items.Count} of {total} {noun}";
            empty.Visibility = control.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            hint.Text = control.ItemsSource == null ? initialHint
                : total == 0 ? $"No {noun} were returned for this organization."
                : "No matches. Try a different filter or clear the search field.";
        }

        private async void PublishResource_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteAsync("Publishing web resource", async () =>
            {
                var selected = RequireSelection(WebResources, "web resource");
                if (!Confirm($"Publish all pending changes to '{selected.Name}'?"))
                {
                    return false;
                }

                await session.PublishWebResourceAsync(selected.Id);
                return true;
            });
        }

        private async void ExportSolution_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteAsync("Exporting solution", async () =>
            {
                var selected = RequireSelection(Solutions, "solution");
                var managed = ExportManaged.IsChecked == true;
                var newVersion = string.IsNullOrWhiteSpace(SolutionVersion.Text) ? null
                    : DeploymentRules.NormalizeSolutionVersion(SolutionVersion.Text);
                var changesVersion = newVersion != null && newVersion != selected.Version;
                var dialog = new SaveFileDialog
                {
                    Filter = "Solution ZIP (*.zip)|*.zip",
                    DefaultExt = ".zip",
                    AddExtension = true,
                    OverwritePrompt = true,
                    FileName = $"{selected.Name}_{newVersion ?? selected.Version}_{(managed ? "managed" : "unmanaged")}.zip"
                };
                if (dialog.ShowDialog(this) != true
                    || !Confirm($"Export '{selected.DisplayName}' as {(managed ? "MANAGED" : "UNMANAGED")}?"
                        + (changesVersion ? $"\n\nUpdate CRM solution version: {selected.Version} -> {newVersion}\nThis is permanent and remains even if export fails." : $"\n\nVersion: {selected.Version} (unchanged)")
                        + "\n\nOnly published changes are exported. No import or deployment will occur."))
                {
                    return false;
                }

                if (newVersion != null)
                {
                    var changed = await session.UpdateSolutionVersionAsync(selected, newVersion);
                    if (changed)
                    {
                        selected.Version = newVersion;
                        Log("Solution version saved in CRM: " + newVersion + ". This change remains if export fails.");
                        Solutions.Items.Refresh();
                        SolutionVersion.Clear();
                    }
                }
                await session.ExportSolutionAsync(selected, managed, dialog.FileName);
                Log("Export saved: " + dialog.FileName);
                return true;
            });
        }

        private Task ExecuteAsync(string operation, Func<Task> action) =>
            ExecuteAsync(operation, async () => { await action(); return true; });

        private async Task ExecuteAsync(string operation, Func<Task<bool>> action)
        {
            if (busy)
            {
                return;
            }

            busy = true;
            SetEnabledState();
            OperationStatus.Text = operation + "...";
            Progress.Visibility = Visibility.Visible;
            Progress.IsIndeterminate = true;
            var timer = Stopwatch.StartNew();
            Log(operation + " started.");
            try
            {
                var completed = await action();
                OperationStatus.Text = completed ? $"{operation} completed ({timer.Elapsed.TotalSeconds:F1}s)" : operation + " cancelled";
                Log(OperationStatus.Text);
            }
            catch (Exception exception)
            {
                OperationStatus.Text = operation + " FAILED. Check the log.";
                Log($"{operation} FAILED ({timer.Elapsed.TotalSeconds:F1}s): {exception.GetType().Name}: {exception.Message}");
                ActivityPanel.IsExpanded = true;
                MessageBox.Show(this, exception.Message + "\n\nIf a server request timed out, refresh and verify its state before retrying.", operation + " failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                busy = false;
                Progress.IsIndeterminate = false;
                Progress.Visibility = Visibility.Collapsed;
                SetEnabledState();
            }
        }

        private void SetEnabledState()
        {
            Workspace.IsEnabled = !busy;
            Navigation.IsEnabled = !busy;
            PluginWorkspace.IsEnabled = !busy && session.IsConnected;
            ResourceWorkspace.IsEnabled = !busy && session.IsConnected;
            SolutionWorkspace.IsEnabled = !busy && session.IsConnected;
            using (var password = AccountPassword.SecurePassword)
            {
                ConnectButton.IsEnabled = !busy && (session.IsConnected || (!string.IsNullOrWhiteSpace(OrganizationUrl.Text)
                    && !string.IsNullOrWhiteSpace(AccountUsername.Text) && password.Length > 0));
                SaveConnectionButton.IsEnabled = !busy && !session.IsConnected && !profileLoadFailed
                    && !string.IsNullOrWhiteSpace(SavedConnectionName.Text) && !string.IsNullOrWhiteSpace(OrganizationUrl.Text)
                    && !string.IsNullOrWhiteSpace(AccountUsername.Text) && password.Length > 0;
            }
            ConnectButton.Content = session.IsConnected ? "Disconnect" : "Connect to CRM";
            ConnectButton.ToolTip = session.IsConnected ? "Disconnect and clear loaded lists."
                : "Enter your organization URL, username, and password. Domain may be blank for DOMAIN\\username or user@domain.";
            ConnectionFields.IsEnabled = !busy && !session.IsConnected;
            SavedConnectionControls.IsEnabled = !busy && !session.IsConnected;
            SavedConnectionSelector.IsEnabled = !profileLoadFailed;
            DeleteConnectionButton.IsEnabled = !profileLoadFailed && SavedConnectionSelector.SelectedItem != null;
            ForgetConnectionsButton.IsEnabled = profileLoadFailed || savedConnections.Connections.Count > 0;
            ConnectionBadgeText.Text = session.IsConnected ? session.OrganizationName : "Not connected";
            ConnectionBadge.ToolTip = session.IsConnected ? $"{session.OrganizationName}\n{session.Endpoint}" : "No active CRM connection";
            ConnectionBadge.Background = new SolidColorBrush(session.IsConnected ? Color.FromRgb(232, 247, 239) : Color.FromRgb(240, 243, 248));
            ConnectionDot.Fill = new SolidColorBrush(session.IsConnected ? Color.FromRgb(21, 128, 83) : Color.FromRgb(120, 134, 155));
            UpdateConnectionNotice();
            UpdatePluginActions();
            UpdateWebResourceActions();
            UpdateSolutionActions();
        }

        private void ConnectionSettings_Changed(object sender, RoutedEventArgs e)
        {
            if (loadingProfile) return;
            var clearedSavedPassword = loadedPassword && (ReferenceEquals(sender, OrganizationUrl)
                || ReferenceEquals(sender, AccountDomain) || ReferenceEquals(sender, AccountUsername));
            if (clearedSavedPassword)
            {
                loadedPassword = false;
                AccountPassword.Clear();
            }
            else if (ReferenceEquals(sender, AccountPassword)) loadedPassword = false;
            if (!busy && !session.IsConnected)
            {
                ConnectionStatus.Text = clearedSavedPassword
                    ? "The URL or account changed. Enter the password for this connection before connecting or saving."
                    : "Enter your organization URL, username, and password to connect.";
                SetEnabledState();
            }
        }

        private static CrmItem RequireSelection(DataGrid grid, string label) =>
            grid.SelectedItem as CrmItem ?? throw new InvalidOperationException("Select a " + label + " first.");

        private string? SelectFile(string filter)
        {
            var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true, Multiselect = false };
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        }

        private bool Confirm(string message) =>
            MessageBox.Show(this, $"Development organization: {session.OrganizationName}\n{session.Endpoint}\n\n{message}",
                "Confirm development operation", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

        private void Log(string message)
        {
            OperationLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            OperationLog.ScrollToEnd();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (busy)
            {
                e.Cancel = true;
                MessageBox.Show(this, "Wait for the current operation to finish before closing.", "Operation in progress");
                return;
            }

            AccountPassword.Clear();
            session.Dispose();
        }
    }
}
