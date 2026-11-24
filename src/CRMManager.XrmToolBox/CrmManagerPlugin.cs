using System;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace CRMManager.XrmToolBox
{
    [Export(typeof(IXrmToolBoxPlugin))]
    [CrmManagerMetadata]
    public sealed class CrmManagerPlugin : PluginBase
    {
        public override IXrmToolBoxPluginControl GetControl() => new CrmManagerControl();

        internal static Icon LoadIcon()
        {
            using (var stream = typeof(CrmManagerPlugin).Assembly.GetManifestResourceStream("CRMManager.Icon.ico")
                ?? throw new InvalidOperationException("The CRM Manager icon is missing."))
            using (var icon = new Icon(stream))
                return (Icon)icon.Clone();
        }
    }

    [MetadataAttribute]
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CrmManagerMetadataAttribute : Attribute, IPluginMetadata
    {
        public string Name => "CRM Manager";
        public string Description => "Manage plugins, web resources and solution exports in on-premises CRM 9.1 development organizations.";
        public string BackgroundColor => "#EDF3FF";
        public string PrimaryFontColor => "#172B4D";
        public string SecondaryFontColor => "#52627A";
        public string SmallImageBase64 => CreateImage(32);
        public string BigImageBase64 => CreateImage(80);

        private static string CreateImage(int size)
        {
            using (var icon = CrmManagerPlugin.LoadIcon())
            using (var source = icon.ToBitmap())
            using (var bitmap = new Bitmap(source, size, size))
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                return Convert.ToBase64String(stream.ToArray());
            }
        }
    }
}
