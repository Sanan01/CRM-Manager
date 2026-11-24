using BAJPluginManager.Core;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace BAJPluginManager.App
{
    public sealed class PluginNode : INotifyPropertyChanged
    {
        private bool expanded;
        private bool selected;

        public PluginNode(object? value, PluginNode? parent = null)
        {
            Value = value;
            Parent = parent;
            Label = value switch
            {
                CrmItem assembly => $"Assembly: {assembly.Name} ({assembly.Version}, {assembly.Details})",
                PluginTypeInfo type => $"{(type.IsWorkflowActivity ? "Workflow activity" : "Plugin type")}: {type.Name}",
                StepRegistration step => $"Step: {step.Name} [{step.StageName}, {step.ModeName}, {(step.Enabled ? "Enabled" : "Disabled")}]",
                ImageRegistration image => $"Image: {image.Name} [{image.TypeName}, alias: {image.Alias}]",
                _ => "Expand to load..."
            };
            if (value is CrmItem || value is StepRegistration || (value is PluginTypeInfo plugin && !plugin.IsWorkflowActivity))
                Children.Add(new PluginNode(null, this));
        }

        public object? Value { get; }
        public PluginNode? Parent { get; }
        public string Label { get; }
        public string Kind => Value switch
        {
            CrmItem _ => "DLL",
            PluginTypeInfo type => type.IsWorkflowActivity ? "WF" : "TYPE",
            StepRegistration _ => "STEP",
            ImageRegistration _ => "IMG",
            _ => "..."
        };
        public bool Loaded { get; set; }
        public ObservableCollection<PluginNode> Children { get; } = new ObservableCollection<PluginNode>();
        public bool IsExpanded
        {
            get => expanded;
            set { expanded = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded))); }
        }
        public bool IsSelected
        {
            get => selected;
            set { selected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;

        public PluginNode? Ancestor<T>()
        {
            for (var node = this; node != null; node = node.Parent)
                if (node.Value is T) return node;
            return null;
        }
    }
}
