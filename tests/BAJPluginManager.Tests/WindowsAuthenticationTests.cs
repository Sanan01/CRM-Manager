using BAJPluginManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;
using System.Security;
using System.Threading.Tasks;

namespace BAJPluginManager.Tests
{
    [TestClass]
    public class WindowsAuthenticationTests
    {
        [TestMethod]
        public void TestCurrentWindowsLoginDoesNotRequireExplicitCredentials()
        {
            Assert.AreSame(CredentialCache.DefaultNetworkCredentials,
                WindowsAuthentication.CreateCredential(true, "", "", null));
        }

        [DataTestMethod]
        [DataRow(" EXAMPLE ", " developer ", "EXAMPLE", "developer")]
        [DataRow("", @"EXAMPLE\developer", "EXAMPLE", "developer")]
        [DataRow("example", @"EXAMPLE\developer", "EXAMPLE", "developer")]
        [DataRow("", "developer@example.test", "", "developer@example.test")]
        public void TestExplicitWindowsLoginNormalizesSupportedAccountFormats(string domain, string username, string expectedDomain, string expectedUser)
        {
            using (var password = CreateTestPassword())
            {
                var credential = WindowsAuthentication.CreateCredential(false, domain, username, password);
                Assert.AreEqual(expectedDomain, credential.Domain);
                Assert.AreEqual(expectedUser, credential.UserName);
                using (var snapshot = credential.SecurePassword)
                {
                    Assert.AreEqual(password.Length, snapshot.Length);
                }
            }
        }

        [DataTestMethod]
        [DataRow("EXAMPLE", "")]
        [DataRow("", "developer")]
        [DataRow("OTHER", @"EXAMPLE\developer")]
        [DataRow("", @"EXAMPLE\")]
        [DataRow("", @"\developer")]
        [DataRow("", @"EXAMPLE\developer\extra")]
        [DataRow("EXAMPLE", "developer@example.test")]
        [DataRow("", "@example.test")]
        [DataRow("", "developer@")]
        [DataRow("", "developer@@example.test")]
        [DataRow("", @"EXAMPLE\developer@example.test")]
        public void TestExplicitWindowsLoginRejectsMissingOrAmbiguousAccountDetails(string domain, string username)
        {
            using (var password = CreateTestPassword())
            {
                Assert.ThrowsException<ArgumentException>(() =>
                    WindowsAuthentication.CreateCredential(false, domain, username, password));
            }
        }

        [TestMethod]
        public void TestExplicitWindowsLoginRequiresNonEmptyPassword()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                WindowsAuthentication.CreateCredential(false, "EXAMPLE", "developer", null));
            using (var empty = new SecureString())
            {
                Assert.ThrowsException<ArgumentException>(() =>
                    WindowsAuthentication.CreateCredential(false, "EXAMPLE", "developer", empty));
            }
        }

        [TestMethod]
        public async Task TestMissingExplicitCredentialsFailBeforeNetworkAccess()
        {
            using (var session = new CrmSession())
            {
                await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                    session.ConnectAsync("https://crm.example.test/DevOrg", "", "", null));
                Assert.IsFalse(session.IsConnected);
            }
        }

        private static SecureString CreateTestPassword()
        {
            var password = new SecureString();
            password.AppendChar('x');
            password.MakeReadOnly();
            return password;
        }
    }
}
