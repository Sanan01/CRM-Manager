using System;

namespace CRMManager.Core
{
    public sealed class CrmItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Version { get; set; } = "";
        public string Culture { get; set; } = "neutral";
        public string PublicKeyToken { get; set; } = "";
        public int Type { get; set; }
        public string Details { get; set; } = "";
    }
}
