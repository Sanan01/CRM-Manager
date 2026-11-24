using CRMManager.Core;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CRMManager.App
{
    public partial class WebResourceEditorWindow : Window
    {
        private readonly CrmSession session;
        private readonly WebResourceDocument document;
        private readonly string initialEditorText;
        private bool busy;
        private bool saved;

        public WebResourceEditorWindow(CrmSession session, WebResourceDocument document)
        {
            InitializeComponent();
            this.session = session;
            this.document = document;
            ResourceName.Text = document.Name;
            EnvironmentText.Text = $"DEVELOPMENT: {session.OrganizationName} | {document.EncodingName}\n{session.Endpoint}";
            CodeBox.Text = document.Text;
            initialEditorText = CodeBox.Text;
            EncodingInfo.Text = document.EncodingName;
            CodeBox.TextChanged += Editor_Changed;
            CodeBox.SelectionChanged += Editor_Changed;
            WrapCode.Checked += WrapCode_Changed;
            WrapCode.Unchecked += WrapCode_Changed;
            Loaded += (sender, e) => CodeBox.Focus();
            UpdateEditorFeedback();
        }

        public bool Changed { get; private set; }

        private void SaveCommand_Executed(object sender, ExecutedRoutedEventArgs e) => Save_Click(sender, e);

        private void SaveCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !busy;

        private void Editor_Changed(object sender, RoutedEventArgs e) => UpdateEditorFeedback();

        private void UpdateEditorFeedback()
        {
            var dirty = CodeBox.Text != initialEditorText;
            DirtyState.Text = dirty ? "Unsaved local changes" : "No local changes";
            DirtyState.Foreground = new SolidColorBrush(dirty ? Color.FromRgb(146, 83, 12) : Color.FromRgb(82, 98, 122));
            var line = CodeBox.GetLineIndexFromCharacterIndex(CodeBox.CaretIndex);
            var start = line >= 0 ? CodeBox.GetCharacterIndexFromLineIndex(line) : 0;
            var lineLabel = WrapCode.IsChecked == true ? "View line" : "Ln";
            CaretPosition.Text = $"{lineLabel} {Math.Max(0, line) + 1}, Col {CodeBox.CaretIndex - Math.Max(0, start) + 1}  |  {CodeBox.Text.Length:N0} characters";
        }

        private void WrapCode_Changed(object sender, RoutedEventArgs e)
        {
            CodeBox.TextWrapping = WrapCode.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap;
            CodeBox.HorizontalScrollBarVisibility = WrapCode.IsChecked == true
                ? System.Windows.Controls.ScrollBarVisibility.Disabled : System.Windows.Controls.ScrollBarVisibility.Auto;
            UpdateEditorFeedback();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (busy) return;
            if (MessageBox.Show(this, $"Save unpublished content for '{document.Name}'?\n\n{EnvironmentText.Text}\n\nThis does NOT publish the resource.",
                "Confirm web resource update", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            busy = true;
            SaveButton.IsEnabled = false;
            CloseButton.IsEnabled = false;
            CodeBox.IsReadOnly = true;
            StatusText.Text = "Saving...";
            try
            {
                var text = CodeBox.Text == initialEditorText ? document.Text : CodeBox.Text;
                Changed = await session.SaveWebResourceTextAsync(document, text);
                saved = true;
                busy = false;
                DialogResult = true;
            }
            catch (Exception exception)
            {
                StatusText.Text = "Save FAILED.";
                MessageBox.Show(this, exception.Message + "\n\nYour edits remain in this window. After a timeout, verify server state before retrying.",
                    "Web resource save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                busy = false;
                SaveButton.IsEnabled = true;
                CloseButton.IsEnabled = true;
                CodeBox.IsReadOnly = false;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (busy)
            {
                e.Cancel = true;
                MessageBox.Show(this, "Wait for the save operation to finish.", "Operation in progress");
                return;
            }
            if (!saved && CodeBox.Text != initialEditorText)
                e.Cancel = MessageBox.Show(this, "Discard your unsaved local edits and close?", "Unsaved code",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes;
        }
    }
}
