using System.Collections.Generic;

namespace CRMManager.Core
{
    public sealed class TableInfo
    {
        public string LogicalName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Label => $"{DisplayName} ({LogicalName})";
    }

    public sealed class ColumnInfo
    {
        public string LogicalName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Label => $"{DisplayName} ({LogicalName})";
    }

    public sealed class RelationshipInfo
    {
        public string SchemaName { get; set; } = "";
        public string TargetEntity { get; set; } = "";
        public string Label => $"{SchemaName} (with {TargetEntity})";
    }

    public sealed class TableDetails
    {
        public string PrimaryId { get; set; } = "";
        public List<ColumnInfo> Columns { get; } = new List<ColumnInfo>();
        public List<RelationshipInfo> Relationships { get; } = new List<RelationshipInfo>();
    }

    public sealed class FilterCondition
    {
        public string Column { get; set; } = "";
        public string Operator { get; set; } = "eq";
        public string Value { get; set; } = "";
    }
}
