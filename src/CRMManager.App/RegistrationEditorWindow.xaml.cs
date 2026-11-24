using CRMManager.Core;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CRMManager.App
{
    public partial class RegistrationEditorWindow : Window
    {
        private readonly CrmSession session;
        private readonly StepRegistration step;
        private readonly ImageRegistration? image;
        private bool busy;
        private bool initializing = true;
        private bool saved;
        private string[] initialFields;

        public RegistrationEditorWindow(CrmSession session, StepRegistration step, ImageRegistration? image = null)
        {
            InitializeComponent();
            this.session = session;
            this.step = step;
            this.image = image;
            EnvironmentText.Text = $"DEVELOPMENT: {session.OrganizationName}\n{session.Endpoint}";
            NameBox.Text = image?.Name ?? step.Name;
            Title = image == null ? "CRM Manager - Plugin step" : "CRM Manager - Step image";
            EditorTitle.Text = image == null
                ? (step.Id == Guid.Empty ? "New plugin step" : "Edit plugin step")
                : (image.Id == Guid.Empty ? "New step image" : "Edit step image");
            StepFields.Visibility = image == null ? Visibility.Visible : Visibility.Collapsed;
            ImageFields.Visibility = image == null ? Visibility.Collapsed : Visibility.Visible;
            if (image == null)
            {
                SelectChoice(StageSelector, step.Stage);
                SelectChoice(ModeSelector, step.Mode);
                OrderBox.Text = step.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture);
                FilteringBox.Text = step.FilteringAttributes;
                DescriptionBox.Text = step.Description;
                ConfigurationBox.Text = step.UnsecureConfiguration;
                AsyncDelete.IsChecked = step.AsyncAutoDelete;
            }
            else
            {
                AliasBox.Text = image.Alias;
                SelectChoice(ImageSelector, image.ImageType);
                MessagePropertyBox.Text = image.MessagePropertyName;
                AttributesBox.Text = image.Attributes;
                AllAttributes.IsChecked = image.AllAttributes;
            }
            initialFields = CaptureFields();
            StageSelector.SelectionChanged += Execution_Changed;
            ModeSelector.SelectionChanged += Execution_Changed;
            AllAttributes.Checked += AllAttributes_Changed;
            AllAttributes.Unchecked += AllAttributes_Changed;
            UpdateFieldGuidance();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (image != null)
            {
                initializing = false;
                StatusText.Text = "Review the image. Saving requires confirmation.";
                return;
            }
            await RunAsync(async () =>
            {
                var messages = await session.GetRegistrationMessagesAsync();
                MessageSelector.ItemsSource = messages;
                if (step.Id != Guid.Empty)
                {
                    if (!messages.Any(message => message.Id == step.MessageId))
                        throw new InvalidOperationException("The registered message is not in the allowed message list. This step cannot be edited here.");
                    MessageSelector.SelectedValue = step.MessageId;
                    await LoadFiltersAsync();
                    EntitySelector.SelectedValue = step.FilterId ?? Guid.Empty;
                    if (EntitySelector.SelectedItem == null)
                        throw new InvalidOperationException("The registered entity filter is not currently allowed. This step cannot be edited here.");
                    MessageSelector.IsEnabled = false;
                    EntitySelector.IsEnabled = false;
                }
                initializing = false;
                initialFields = CaptureFields();
                StatusText.Text = "Review the registration. Saving changes CRM immediately after confirmation.";
            });
        }

        private async void Message_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (initializing || busy || MessageSelector.SelectedItem == null) return;
            await RunAsync(LoadFiltersAsync);
        }

        private async Task LoadFiltersAsync()
        {
            if (!(MessageSelector.SelectedItem is RegistrationOption message))
                throw new InvalidOperationException("Select a message first.");
            EntitySelector.ItemsSource = await session.GetMessageFiltersAsync(message.Id);
            EntitySelector.SelectedIndex = -1;
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync(async () =>
            {
                string summary;
                if (image == null)
                {
                    if (!(MessageSelector.SelectedItem is RegistrationOption message)
                        || !(EntitySelector.SelectedItem is RegistrationOption filter))
                        throw new ArgumentException("Select a message and entity filter explicitly.");
                    if (!int.TryParse(OrderBox.Text, out var rank)) throw new ArgumentException("Execution order must be a whole number.");
                    step.Name = NameBox.Text.Trim();
                    step.MessageId = message.Id;
                    step.MessageName = message.Name;
                    step.FilterId = filter.Id == Guid.Empty ? (Guid?)null : filter.Id;
                    step.Stage = Choice(StageSelector);
                    step.Mode = Choice(ModeSelector);
                    step.Rank = rank;
                    step.FilteringAttributes = FilteringBox.Text;
                    step.Description = DescriptionBox.Text;
                    step.UnsecureConfiguration = ConfigurationBox.Text;
                    step.AsyncAutoDelete = AsyncDelete.IsChecked == true;
                    RegistrationRules.ValidateStep(step);
                    summary = $"{step.Name}\n{message.Name} | {filter.Name}\n{step.StageName} | {step.ModeName} | Order {step.Rank}";
                }
                else
                {
                    image.Name = NameBox.Text.Trim();
                    image.Alias = AliasBox.Text.Trim();
                    image.ImageType = Choice(ImageSelector);
                    image.MessagePropertyName = MessagePropertyBox.Text.Trim();
                    image.Attributes = AttributesBox.Text;
                    image.AllAttributes = AllAttributes.IsChecked == true;
                    RegistrationRules.ValidateImage(image, step);
                    summary = $"{image.Name}\n{image.TypeName} | Alias: {image.Alias}\nParent step: {step.Name}"
                        + (image.AllAttributes ? "\nWARNING: All attributes will be included." : "");
                }
                if (MessageBox.Show(this, $"{EnvironmentText.Text}\n\nSave this registration?\n\n{summary}", "Confirm development registration",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                if (image == null) await session.SaveStepAsync(step);
                else await session.SaveImageAsync(image);
                saved = true;
                busy = false;
                DialogResult = true;
            });
        }

        private async Task RunAsync(Func<Task> action)
        {
            if (busy) return;
            busy = true;
            Fields.IsEnabled = false;
            SaveButton.IsEnabled = false;
            CancelButton.IsEnabled = false;
            StatusText.Text = "Working...";
            try { await action(); }
            catch (Exception exception)
            {
                StatusText.Text = "FAILED: " + exception.Message;
                MessageBox.Show(this, exception.Message + "\n\nAfter a timeout, verify server state before retrying.", "Registration failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                busy = false;
                Fields.IsEnabled = true;
                SaveButton.IsEnabled = !initializing;
                CancelButton.IsEnabled = true;
                UpdateFieldGuidance();
                if (StatusText.Text == "Working...") StatusText.Text = "Ready. Saving requires confirmation.";
            }
        }

        private void Execution_Changed(object sender, SelectionChangedEventArgs e) => UpdateFieldGuidance();

        private void AllAttributes_Changed(object sender, RoutedEventArgs e) => UpdateFieldGuidance();

        private void UpdateFieldGuidance()
        {
            AttributesBox.IsEnabled = AllAttributes.IsChecked != true;
            var async = Choice(ModeSelector) == 1;
            var validStage = !async || Choice(StageSelector) == 40;
            ExecutionHint.Text = !validStage ? "Choose Post-operation to use asynchronous execution."
                : async ? "Runs in the background after the operation completes."
                : "Runs within the request. Keep execution fast to avoid blocking users.";
            ExecutionHint.Foreground = new SolidColorBrush(validStage ? Color.FromRgb(82, 98, 122) : Color.FromRgb(146, 83, 12));
        }

        private string[] CaptureFields() => new[]
        {
            NameBox.Text, MessageSelector.SelectedValue?.ToString() ?? "", EntitySelector.SelectedValue?.ToString() ?? "",
            StageSelector.SelectedIndex.ToString(), ModeSelector.SelectedIndex.ToString(), OrderBox.Text,
            FilteringBox.Text, DescriptionBox.Text, ConfigurationBox.Text, AsyncDelete.IsChecked.ToString(),
            AliasBox.Text, ImageSelector.SelectedIndex.ToString(), MessagePropertyBox.Text, AttributesBox.Text,
            AllAttributes.IsChecked.ToString()
        };

        private static int Choice(ComboBox selector)
        {
            if (!(selector.SelectedItem is ComboBoxItem item) || !int.TryParse(item.Tag?.ToString(), out var value))
                throw new ArgumentException("Select a stage, execution mode, and image type as applicable.");
            return value;
        }

        private static void SelectChoice(ComboBox selector, int value) =>
            selector.SelectedItem = selector.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag?.ToString() == value.ToString());

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (busy)
            {
                e.Cancel = true;
                MessageBox.Show(this, "Wait for the current registration operation to finish.", "Operation in progress");
            }
            else if (!saved && !initialFields.SequenceEqual(CaptureFields()))
                e.Cancel = MessageBox.Show(this, "Discard your unsaved registration changes and close?", "Unsaved registration",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes;
        }
    }
}
