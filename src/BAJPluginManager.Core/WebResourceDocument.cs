using System;
using System.IO;
using System.Linq;
using System.Text;

namespace BAJPluginManager.Core
{
    public sealed class WebResourceDocument
    {
        private readonly byte[] original;
        private readonly Encoding encoding;
        private readonly byte[] preamble;

        private WebResourceDocument(Guid id, string name, int resourceType, byte[] bytes)
        {
            Id = id;
            Name = name;
            ResourceType = resourceType;
            original = bytes;
            if (StartsWith(bytes, new byte[] { 0xff, 0xfe, 0, 0 }))
                encoding = new UTF32Encoding(false, true, true);
            else if (StartsWith(bytes, new byte[] { 0, 0, 0xfe, 0xff }))
                encoding = new UTF32Encoding(true, true, true);
            else if (StartsWith(bytes, new byte[] { 0xff, 0xfe }))
                encoding = new UnicodeEncoding(false, true, true);
            else if (StartsWith(bytes, new byte[] { 0xfe, 0xff }))
                encoding = new UnicodeEncoding(true, true, true);
            else
                encoding = new UTF8Encoding(StartsWith(bytes, new byte[] { 0xef, 0xbb, 0xbf }), true);
            preamble = encoding.GetPreamble();
            Text = encoding.GetString(bytes, preamble.Length, bytes.Length - preamble.Length);
            if (Text.Contains("\0"))
                throw new InvalidDataException("This resource contains NUL characters or unsupported encoding. Use file upload instead of the text editor.");
        }

        public Guid Id { get; }
        public string Name { get; }
        public int ResourceType { get; }
        public string Text { get; }
        public string EncodingName => encoding.EncodingName;
        public static bool IsTextType(int type) => type == 1 || type == 2 || type == 3 || type == 4 || type == 9 || type == 11 || type == 12;

        public static WebResourceDocument Open(Guid id, string name, int type, string base64)
        {
            if (!IsTextType(type))
                throw new ArgumentException("Only HTML, CSS, JavaScript, XML, XSL, SVG, and RESX resources can be edited as text. Use upload for binary resources.");
            return new WebResourceDocument(id, name, type, Convert.FromBase64String(base64));
        }

        public byte[] Encode(string text) => preamble.Concat(encoding.GetBytes(text)).ToArray();
        internal byte[]? PrepareUpdate(string text, string currentContent)
        {
            var bytes = Encode(text);
            if (DeploymentRules.ContentMatches(bytes, currentContent)) return null;
            if (!DeploymentRules.ContentMatches(original, currentContent))
                throw new InvalidOperationException("The unpublished resource changed after you opened it. Copy your edits, close this editor, reopen the resource, and merge the changes before saving.");
            return bytes;
        }
        private static bool StartsWith(byte[] bytes, byte[] prefix) => bytes.Take(prefix.Length).SequenceEqual(prefix);
    }
}
