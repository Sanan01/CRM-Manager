using System;

namespace BAJPluginManager.Core
{
    public sealed class PluginTypeInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsWorkflowActivity { get; set; }
    }

    public sealed class RegistrationOption
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
    }

    public sealed class StepRegistration
    {
        public Guid Id { get; set; }
        public Guid PluginTypeId { get; set; }
        public Guid MessageId { get; set; }
        public Guid? FilterId { get; set; }
        public string MessageName { get; set; } = "";
        public string PrimaryEntity { get; set; } = "";
        public string Name { get; set; } = "";
        public int Stage { get; set; } = 20;
        public int Mode { get; set; }
        public int Rank { get; set; } = 1;
        public bool Enabled { get; set; } = true;
        public bool CanEdit { get; set; }
        public bool AsyncAutoDelete { get; set; }
        public string FilteringAttributes { get; set; } = "";
        public string Description { get; set; } = "";
        public string UnsecureConfiguration { get; set; } = "";
        public string StageName => Stage == 10 ? "Pre-validation" : Stage == 20 ? "Pre-operation" : Stage == 40 ? "Post-operation" : Stage.ToString();
        public string ModeName => Mode == 0 ? "Synchronous" : Mode == 1 ? "Asynchronous" : Mode.ToString();
    }

    public sealed class ImageRegistration
    {
        public Guid Id { get; set; }
        public Guid StepId { get; set; }
        public string Name { get; set; } = "";
        public string Alias { get; set; } = "";
        public int ImageType { get; set; }
        public string MessagePropertyName { get; set; } = "Target";
        public bool AllAttributes { get; set; }
        public string Attributes { get; set; } = "";
        public bool CanEdit { get; set; }
        public string TypeName => ImageType == 0 ? "Pre-image" : ImageType == 1 ? "Post-image" : ImageType == 2 ? "Pre + post image" : ImageType.ToString();
    }
}
