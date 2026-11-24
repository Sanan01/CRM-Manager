using CRMManager.App;
using CRMManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CRMManager.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class WorkspaceTests
    {
        [TestMethod]
        public void TestOfflineWorkspacesAndEditorFeedback()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                var application = new CRMManager.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var profileDirectory = Path.Combine(Path.GetTempPath(), "CRMManager-Workspace-" + Guid.NewGuid().ToString("N"));
                try
                {
                    application.InitializeComponent();
                    var profileStore = new ConnectionProfileStore(Path.Combine(profileDirectory, "connections.dat"));
                    var window = new MainWindow(profileStore);
                    var plugins = (TreeView)window.FindName("Plugins");
                    plugins.ItemsSource = new[]
                    {
                        new PluginNode(new CrmItem { Name = "Finance.Plugins" }),
                        new PluginNode(new CrmItem { Name = "Reporting.Plugins" })
                    };
                    var assemblySearch = (TextBox)window.FindName("AssemblySearch");
                    assemblySearch.Text = " FINANCE ";
                    Assert.AreEqual(1, plugins.Items.Count);
                    assemblySearch.Text = "missing";
                    Assert.AreEqual(0, plugins.Items.Count);
                    StringAssert.Contains(((TextBlock)window.FindName("PluginEmptyText")).Text, "No matches");
                    assemblySearch.Clear();
                    Assert.AreEqual(2, plugins.Items.Count);

                    var resource = new CrmItem { Name = "vrp_main.js", DisplayName = "Customer scripts", Details = "JavaScript", Type = 3 };
                    var resources = (DataGrid)window.FindName("WebResources");
                    resources.ItemsSource = new[] { resource, new CrmItem { Name = "vrp_logo.png", Details = "PNG" } };
                    var resourceSearch = (TextBox)window.FindName("ResourceSearch");
                    resourceSearch.Text = "CUSTOMER";
                    Assert.AreEqual(1, resources.Items.Count);
                    resources.SelectedItem = resource;
                    resourceSearch.Text = "png";
                    Assert.AreEqual(1, resources.Items.Count);
                    Assert.IsNull(resources.SelectedItem);

                    var solutions = (DataGrid)window.FindName("Solutions");
                    solutions.ItemsSource = new[]
                    {
                        new CrmItem { Name = "Finance", DisplayName = "Corporate", Version = "2.1.0.0" },
                        new CrmItem { Name = "Sales", Version = "1.0.0.0" }
                    };
                    ((RadioButton)window.FindName("SolutionsNav")).IsChecked = true;
                    var selectionRoot = (FrameworkElement)window.Content;
                    selectionRoot.Measure(new Size(1384, 860));
                    selectionRoot.Arrange(new Rect(0, 0, 1384, 860));
                    selectionRoot.UpdateLayout();
                    var firstRow = (DataGridRow)solutions.ItemContainerGenerator.ContainerFromIndex(0);
                    var secondRow = (DataGridRow)solutions.ItemContainerGenerator.ContainerFromIndex(1);
                    var firstCheck = FindChild<CheckBox>(solutions.Columns[0].GetCellContent(firstRow));
                    var secondCheck = FindChild<CheckBox>(solutions.Columns[0].GetCellContent(secondRow));
                    solutions.SelectedItem = solutions.Items[0];
                    selectionRoot.UpdateLayout();
                    Assert.IsTrue(firstCheck.IsChecked);
                    Assert.IsFalse(secondCheck.IsChecked);
                    solutions.SelectedItem = solutions.Items[1];
                    selectionRoot.UpdateLayout();
                    Assert.IsFalse(firstCheck.IsChecked);
                    Assert.IsTrue(secondCheck.IsChecked);
                    solutions.SelectedItem = null;
                    selectionRoot.UpdateLayout();
                    Assert.IsFalse(secondCheck.IsChecked);
                    ((TextBox)window.FindName("SolutionSearch")).Text = "2.1";
                    Assert.AreEqual(1, solutions.Items.Count);
                    solutions.SelectedItem = solutions.Items[0];
                    var versionInput = (TextBox)window.FindName("SolutionVersion");
                    versionInput.Text = "2.1.0.1";
                    solutions.SelectedItem = null;
                    Assert.AreEqual("", versionInput.Text, "Version input must not carry over to another selection.");

                    foreach (var name in new[] { "PluginsNav", "ResourcesNav", "SolutionsNav" })
                    {
                        ((RadioButton)window.FindName(name)).IsChecked = true;
                        Assert.AreEqual(Visibility.Visible, ((FrameworkElement)window.FindName("ConnectionRequired")).Visibility);
                    }
                    Assert.IsFalse(((FrameworkElement)window.FindName("PluginWorkspace")).IsEnabled);
                    Assert.IsFalse(((FrameworkElement)window.FindName("ResourceWorkspace")).IsEnabled);
                    Assert.IsFalse(((FrameworkElement)window.FindName("SolutionWorkspace")).IsEnabled);
                    Assert.IsFalse(((Button)window.FindName("UnregisterStepButton")).IsEnabled);
                    Assert.IsFalse(((Button)window.FindName("UnregisterPluginTypeButton")).IsEnabled);
                    ((RadioButton)window.FindName("ConnectionNav")).IsChecked = true;
                    Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)window.FindName("ConnectionRequired")).Visibility);
                    Assert.AreEqual(Visibility.Visible, ((FrameworkElement)window.FindName("ConnectionPage")).Visibility);
                    Assert.IsNull(window.FindName("ConnectionMethod"));
                    Assert.IsNull(window.FindName("UseCurrentWindowsAccount"));
                    Assert.IsNull(window.FindName("DevelopmentConfirmed"));
                    Assert.IsNull(window.FindName("DiscoverButton"));
                    var connect = (Button)window.FindName("ConnectButton");
                    var username = (TextBox)window.FindName("AccountUsername");
                    var passwordInput = (PasswordBox)window.FindName("AccountPassword");
                    Assert.IsFalse(connect.IsEnabled);
                    ((TextBox)window.FindName("OrganizationUrl")).Text = "https://crm.invalid/Development";
                    username.Text = "developer@example.test";
                    Assert.IsFalse(connect.IsEnabled);
                    passwordInput.Password = "test-only";
                    Assert.IsTrue(connect.IsEnabled);
                    username.Clear();
                    Assert.IsFalse(connect.IsEnabled);
                    ((TextBox)window.FindName("AccountDomain")).Text = "EXAMPLE";
                    username.Text = "developer";
                    Assert.IsTrue(connect.IsEnabled);
                    passwordInput.Clear();
                    Assert.IsFalse(connect.IsEnabled);
                    var root = (FrameworkElement)window.Content;
                    root.Measure(new Size(1084, 680));
                    root.Arrange(new Rect(0, 0, 1084, 680));
                    root.UpdateLayout();
                    Assert.AreEqual(Visibility.Visible, ((FrameworkElement)window.FindName("ConnectionFields")).Visibility);
                    root.Measure(new Size(1384, 860));
                    root.Arrange(new Rect(0, 0, 1384, 860));
                    root.UpdateLayout();
                    Assert.IsTrue(((ScrollViewer)window.FindName("ConnectionPage")).ScrollableHeight < 1,
                        "The default connection layout should fit without vertical scrolling.");
                    window.Close();

                    SavedConnection first;
                    SavedConnection second;
                    using (var secret = ConnectionProfileTests.Secret("first-offline-password"))
                        first = SavedConnection.Create(Guid.NewGuid(), "First development", "https://crm.example.test/First",
                            "EXAMPLE", "first-developer", secret);
                    using (var secret = ConnectionProfileTests.Secret("second-offline-password"))
                        second = SavedConnection.Create(Guid.NewGuid(), "Second development", "https://crm.example.test/Second",
                            "", "second-developer@example.test", secret);
                    profileStore.Save(new SavedConnections { Connections = { first, second }, SelectedId = second.Id });
                    var remembered = new MainWindow(profileStore);
                    try
                    {
                        Assert.AreEqual(second.Address, ((TextBox)remembered.FindName("OrganizationUrl")).Text);
                        Assert.AreEqual(second.Username, ((TextBox)remembered.FindName("AccountUsername")).Text);
                        Assert.AreEqual("second-offline-password", ((PasswordBox)remembered.FindName("AccountPassword")).Password);
                        Assert.AreEqual("Connect to CRM", ((Button)remembered.FindName("ConnectButton")).Content);
                        Assert.IsFalse(((FrameworkElement)remembered.FindName("PluginWorkspace")).IsEnabled);
                        var selector = (ComboBox)remembered.FindName("SavedConnectionSelector");
                        selector.SelectedIndex = 0;
                        Assert.AreEqual(first.Address, ((TextBox)remembered.FindName("OrganizationUrl")).Text);
                        Assert.AreEqual(first.Domain, ((TextBox)remembered.FindName("AccountDomain")).Text);
                        Assert.AreEqual(first.Id, profileStore.Load().SelectedId);
                        ((TextBox)remembered.FindName("OrganizationUrl")).Text = "https://other.example.test/Development";
                        Assert.AreEqual("", ((PasswordBox)remembered.FindName("AccountPassword")).Password);
                        Assert.IsFalse(((Button)remembered.FindName("ConnectButton")).IsEnabled);
                        ((Button)remembered.FindName("NewConnectionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Assert.IsNull(selector.SelectedItem);
                        Assert.AreEqual("", ((TextBox)remembered.FindName("AccountUsername")).Text);
                    }
                    finally { remembered.Close(); }
                    var reopened = new MainWindow(profileStore);
                    try
                    {
                        Assert.AreEqual(first.Address, ((TextBox)reopened.FindName("OrganizationUrl")).Text,
                            "Unsaved edits must not replace the selected stored profile.");
                        ((TextBox)reopened.FindName("AccountUsername")).Text = "different-developer";
                        Assert.AreEqual("", ((PasswordBox)reopened.FindName("AccountPassword")).Password);
                    }
                    finally { reopened.Close(); }
                    File.WriteAllBytes(Path.Combine(profileDirectory, "connections.dat"), new byte[32]);
                    var unreadable = new MainWindow(profileStore);
                    try
                    {
                        StringAssert.Contains(((TextBlock)unreadable.FindName("ConnectionStatus")).Text, "could not be loaded");
                        ((TextBox)unreadable.FindName("SavedConnectionName")).Text = "Manual development";
                        ((TextBox)unreadable.FindName("OrganizationUrl")).Text = first.Address;
                        ((TextBox)unreadable.FindName("AccountUsername")).Text = "developer@example.test";
                        ((PasswordBox)unreadable.FindName("AccountPassword")).Password = "offline-only";
                        Assert.IsTrue(((Button)unreadable.FindName("ConnectButton")).IsEnabled);
                        Assert.IsFalse(((Button)unreadable.FindName("SaveConnectionButton")).IsEnabled);
                        Assert.IsTrue(((Button)unreadable.FindName("ForgetConnectionsButton")).IsEnabled);
                        CollectionAssert.AreEqual(new byte[32], File.ReadAllBytes(Path.Combine(profileDirectory, "connections.dat")));
                    }
                    finally { unreadable.Close(); }

                    using (var session = new CrmSession())
                    {
                        var document = WebResourceDocument.Open(Guid.NewGuid(), "test.js", 3,
                            Convert.ToBase64String(Encoding.UTF8.GetBytes("const original = true;")));
                        var editor = new WebResourceEditorWindow(session, document);
                        var code = (TextBox)editor.FindName("CodeBox");
                        var original = code.Text;
                        try
                        {
                            code.Text += "\nconst edited = true;";
                            StringAssert.Contains(((TextBlock)editor.FindName("DirtyState")).Text, "Unsaved");
                            ((CheckBox)editor.FindName("WrapCode")).IsChecked = true;
                            Assert.AreEqual(TextWrapping.Wrap, code.TextWrapping);
                            StringAssert.Contains(code.Text, "const edited");
                            code.Text = original;
                            Assert.AreEqual("No local changes", ((TextBlock)editor.FindName("DirtyState")).Text);
                        }
                        finally
                        {
                            code.Text = original;
                            editor.Close();
                        }

                        var registration = new RegistrationEditorWindow(session, new StepRegistration());
                        var mode = (ComboBox)registration.FindName("ModeSelector");
                        var stage = (ComboBox)registration.FindName("StageSelector");
                        var initialMode = mode.SelectedIndex;
                        var initialStage = stage.SelectedIndex;
                        try
                        {
                            mode.SelectedIndex = 1;
                            stage.SelectedIndex = 1;
                            StringAssert.Contains(((TextBlock)registration.FindName("ExecutionHint")).Text, "Choose Post-operation");
                            stage.SelectedIndex = 2;
                            StringAssert.Contains(((TextBlock)registration.FindName("ExecutionHint")).Text, "background");
                        }
                        finally
                        {
                            mode.SelectedIndex = initialMode;
                            stage.SelectedIndex = initialStage;
                            registration.Close();
                        }
                    }
                }
                catch (Exception exception) { failure = exception; }
                finally
                {
                    application.Shutdown();
                    if (Directory.Exists(profileDirectory)) Directory.Delete(profileDirectory, true);
                }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "Offline WPF checks timed out.");
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent is T match) return match;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var found = FindChildOrNull<T>(child);
                if (found != null) return found;
            }
            throw new AssertFailedException("Expected visual control was not generated: " + typeof(T).Name);
        }

        private static T? FindChildOrNull<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent is T match) return match;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var found = FindChildOrNull<T>(VisualTreeHelper.GetChild(parent, i));
                if (found != null) return found;
            }
            return null;
        }
    }
}
