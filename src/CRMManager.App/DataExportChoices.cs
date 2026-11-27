using CRMManager.Core;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace CRMManager.App
{
    public sealed class SelectableItem
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public bool IsSelected { get; set; }
    }

    public sealed class OperatorChoice
    {
        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            ["eq"] = "equals",
            ["ne"] = "does not equal",
            ["like"] = "matches pattern",
            ["not-like"] = "does not match pattern",
            ["begins-with"] = "starts with",
            ["ends-with"] = "ends with",
            ["gt"] = "is greater than",
            ["ge"] = "is greater or equal",
            ["lt"] = "is less than",
            ["le"] = "is less or equal",
            ["null"] = "is empty",
            ["not-null"] = "is not empty"
        };

        public static readonly IReadOnlyList<OperatorChoice> All = DataExporter.FilterOperators
            .Select(key => new OperatorChoice { Key = key, Label = Labels.TryGetValue(key, out var label) ? label : key })
            .ToList();

        public string Key { get; private set; } = "";
        public string Label { get; private set; } = "";
    }

    public sealed class FilterRow : INotifyPropertyChanged
    {
        private string column = "";
        private string operatorKey = "eq";
        private string text = "";

        public FilterRow(IReadOnlyList<ColumnInfo> columns) => Columns = columns;

        public event PropertyChangedEventHandler? PropertyChanged;

        public IReadOnlyList<ColumnInfo> Columns { get; }
        public IReadOnlyList<OperatorChoice> Operators => OperatorChoice.All;
        public bool NeedsValue => operatorKey != "null" && operatorKey != "not-null";

        public string Column { get => column; set => Set(ref column, value, nameof(Column)); }

        public string Operator
        {
            get => operatorKey;
            set
            {
                if (Set(ref operatorKey, value, nameof(Operator)))
                {
                    Raise(nameof(NeedsValue));
                }
            }
        }

        public string Value { get => text; set => Set(ref text, value, nameof(Value)); }

        private bool Set(ref string field, string newValue, string name)
        {
            newValue ??= "";
            if (field == newValue)
            {
                return false;
            }

            field = newValue;
            Raise(name);
            return true;
        }

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
