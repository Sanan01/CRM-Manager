using BAJPluginManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace BAJPluginManager.Tests
{
    [TestClass]
    public class WebResourceDocumentTests
    {
        [TestMethod]
        public void TestTextEditingPreservesOriginalEncodingAndBom()
        {
            foreach (var encoding in new Encoding[]
            {
                new UTF8Encoding(false, true), new UTF8Encoding(true, true),
                new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true),
                new UTF32Encoding(false, true, true), new UTF32Encoding(true, true, true)
            })
            {
                var original = Bytes(encoding, "var value = '\u0627';\r\n");
                var document = WebResourceDocument.Open(Guid.NewGuid(), "sample.js", 3, Convert.ToBase64String(original));
                Assert.AreEqual("var value = '\u0627';\r\n", document.Text);
                CollectionAssert.AreEqual(original, document.Encode(document.Text));
                CollectionAssert.AreEqual(Bytes(encoding, "var value = '\u0628';\n"), document.Encode("var value = '\u0628';\n"));
            }
        }

        [TestMethod]
        public void TestStaleEditorCannotOverwriteChangedUnpublishedContent()
        {
            var original = Convert.ToBase64String(Encoding.UTF8.GetBytes("original"));
            var document = WebResourceDocument.Open(Guid.NewGuid(), "sample.js", 3, original);
            Assert.ThrowsException<InvalidOperationException>(() =>
                document.PrepareUpdate("local edit", Convert.ToBase64String(Encoding.UTF8.GetBytes("another developer's edit"))));
            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("local edit"), document.PrepareUpdate("local edit", original));
        }

        [TestMethod]
        public void TestUnchangedOrAlreadyMatchingTextDoesNotRequireAnUpdate()
        {
            var original = Convert.ToBase64String(Encoding.UTF8.GetBytes("original"));
            var document = WebResourceDocument.Open(Guid.NewGuid(), "sample.js", 3, original);
            Assert.IsNull(document.PrepareUpdate("original", original));
            Assert.IsNull(document.PrepareUpdate("same edit", Convert.ToBase64String(Encoding.UTF8.GetBytes("same edit"))));
        }

        [TestMethod]
        public void TestBinaryAndUnsupportedTextEncodingAreRejected()
        {
            Assert.ThrowsException<ArgumentException>(() => WebResourceDocument.Open(Guid.NewGuid(), "image.png", 5, ""));
            Assert.ThrowsException<DecoderFallbackException>(() =>
                WebResourceDocument.Open(Guid.NewGuid(), "sample.js", 3, Convert.ToBase64String(new byte[] { 0xff })));
            Assert.ThrowsException<InvalidDataException>(() =>
                WebResourceDocument.Open(Guid.NewGuid(), "sample.js", 3, Convert.ToBase64String(new byte[] { 65, 0 })));
        }

        private static byte[] Bytes(Encoding encoding, string text) => encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
    }
}
