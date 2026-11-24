using CRMManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Reflection;

namespace CRMManager.Tests
{
    [TestClass]
    public class DeploymentRulesTests
    {
        [DataTestMethod]
        [DataRow("https://crm.example.test/DevOrg", "https://crm.example.test/DevOrg/XRMServices/2011/Organization.svc")]
        [DataRow("http://crm.example.test:5555/DevOrg/", "http://crm.example.test:5555/DevOrg/XRMServices/2011/Organization.svc")]
        [DataRow("https://crm.example.test/DevOrg/XRMServices/2011/Organization.svc", "https://crm.example.test/DevOrg/XRMServices/2011/Organization.svc")]
        public void TestOrganizationUrlPreservesOrganizationAndServicePath(string input, string expected)
        {
            Assert.AreEqual(expected, DeploymentRules.GetServiceUri(input).AbsoluteUri);
        }

        [DataTestMethod]
        [DataRow("file:///C:/test")]
        [DataRow("https://user:password@crm.example.test/DevOrg")]
        [DataRow("https://crm.example.test/DevOrg?token=example")]
        [DataRow("https://crm.example.test/DevOrg#fragment")]
        [DataRow("")]
        public void TestOrganizationUrlRejectsUnsafeInput(string input)
        {
            Assert.ThrowsException<ArgumentException>(() => DeploymentRules.GetServiceUri(input));
        }

        [TestMethod]
        public void TestAssemblyUpdateAllowsBuildAndRevisionChanges()
        {
            DeploymentRules.ValidateAssemblyUpdate(Identity("Plugins", "1.2.4.5"), Identity("Plugins", "1.2.0.0"));
        }

        [DataTestMethod]
        [DataRow("OtherPlugins", "1.2.0.0", "neutral", "0123456789abcdef")]
        [DataRow("Plugins", "2.2.0.0", "neutral", "0123456789abcdef")]
        [DataRow("Plugins", "1.3.0.0", "neutral", "0123456789abcdef")]
        [DataRow("Plugins", "1.2.0.0", "en-US", "0123456789abcdef")]
        [DataRow("Plugins", "1.2.0.0", "neutral", "fedcba9876543210")]
        public void TestAssemblyUpdateRejectsDifferentIdentity(string name, string version, string culture, string token)
        {
            Assert.ThrowsException<InvalidOperationException>(() =>
                DeploymentRules.ValidateAssemblyUpdate(Identity(name, version, culture, token), Identity("Plugins", "1.2.0.0")));
        }

        [TestMethod]
        public void TestPluginTypeNamesRejectDuplicatesAndWhitespace()
        {
            Assert.ThrowsException<ArgumentException>(() => DeploymentRules.ParseTypeNames("Example.Plugin\nExample.Plugin"));
            Assert.ThrowsException<ArgumentException>(() => DeploymentRules.ParseTypeNames("Example. Plugin"));
            Assert.ThrowsException<ArgumentException>(() => DeploymentRules.ParseTypeNames("\r\n "));
            CollectionAssert.AreEqual(new[] { "Example.First", "Example.Second" },
                DeploymentRules.ParseTypeNames(" Example.First\r\nExample.Second\n"));
        }

        [TestMethod]
        public void TestJavascriptUploadRejectsTypescriptAndWrongResourceType()
        {
            DeploymentRules.ValidateWebResourceFile(@"C:\build\plugin.JS", 3);
            Assert.ThrowsException<ArgumentException>(() => DeploymentRules.ValidateWebResourceFile(@"C:\src\plugin.ts", 3));
            Assert.ThrowsException<ArgumentException>(() => DeploymentRules.ValidateWebResourceFile(@"C:\build\plugin.js", 1));
            Assert.ThrowsException<ArgumentException>(() => DeploymentRules.ValidateWebResourceFile(@"C:\build\plugin.js", 999));
        }

        [TestMethod]
        public void TestContentComparisonDetectsChangedAndUnchangedUploads()
        {
            var local = new byte[] { 1, 2, 3 };
            Assert.IsTrue(DeploymentRules.ContentMatches(local, Convert.ToBase64String(local)));
            Assert.IsFalse(DeploymentRules.ContentMatches(local, Convert.ToBase64String(new byte[] { 1, 2, 4 })));
            Assert.ThrowsException<FormatException>(() => DeploymentRules.ContentMatches(local, "invalid!"));
        }

        [TestMethod]
        public async System.Threading.Tasks.Task TestConnectionRequiresOrganizationPathBeforeNetworkAccess()
        {
            using (var session = new CrmSession())
            {
                await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                    session.ConnectAsync("https://crm.example.test", "", "", null));
                Assert.IsFalse(session.IsConnected);
            }
        }

        private static AssemblyName Identity(string name, string version, string culture = "neutral", string token = "0123456789abcdef") =>
            new AssemblyName($"{name}, Version={version}, Culture={culture}, PublicKeyToken={token}");
    }
}
