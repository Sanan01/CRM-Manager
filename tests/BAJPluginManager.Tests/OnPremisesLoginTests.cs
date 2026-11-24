using BAJPluginManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xrm.Sdk.Discovery;
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace BAJPluginManager.Tests
{
    [TestClass]
    public class OnPremisesLoginTests
    {
        [DataTestMethod]
        [DataRow("crm.example.test", "", true, "https://crm.example.test/")]
        [DataRow("crm.example.test", "", false, "http://crm.example.test/")]
        [DataRow("crm.example.test", "5555", true, "https://crm.example.test:5555/")]
        [DataRow("https://crm.example.test:5555", "", true, "https://crm.example.test:5555/")]
        [DataRow("https://crm.example.test:5555", "5555", true, "https://crm.example.test:5555/")]
        public void TestServerLoginBuildsDiscoveryAuthority(string host, string port, bool ssl, string expected)
        {
            Assert.AreEqual(expected, DeploymentRules.GetServerUri(host, port, ssl).AbsoluteUri);
        }

        [DataTestMethod]
        [DataRow("", "", true)]
        [DataRow("crm.example.test/DevOrg", "", true)]
        [DataRow("http://crm.example.test", "", true)]
        [DataRow("https://crm.example.test", "", false)]
        [DataRow("https://user:example@crm.example.test", "", true)]
        [DataRow("https://crm.example.test?query=example", "", true)]
        [DataRow("https://crm.example.test#fragment", "", true)]
        [DataRow("crm.example.test", "0", true)]
        [DataRow("crm.example.test", "65536", true)]
        [DataRow("crm.example.test", "invalid", true)]
        [DataRow("crm.example.test", "-1", true)]
        [DataRow("crm.example.test:5555", "5556", true)]
        public void TestServerLoginRejectsInvalidOrConflictingSettings(string host, string port, bool ssl)
        {
            Assert.ThrowsException<ArgumentException>(() => DeploymentRules.GetServerUri(host, port, ssl));
        }

        [TestMethod]
        public void TestDirectOrganizationLoginDoesNotGuessOrganizationFromBareHost()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                DeploymentRules.GetOrganizationUniqueName(new Uri("https://crm.example.test/XRMServices/2011/Organization.svc")));
            Assert.AreEqual("DevOrg", DeploymentRules.GetOrganizationUniqueName(
                new Uri("https://crm.example.test/DevOrg/XRMServices/2011/Organization.svc")));
        }

        [TestMethod]
        public void TestDiscoveryPreservesExactOrganizationIdentityAndEndpoint()
        {
            var detail = Organization("DevOrg");
            var result = new CrmDiscoveryResult(new Uri("https://crm.example.test"),
                CredentialCache.DefaultNetworkCredentials, new[] { detail });
            Assert.AreEqual(1, result.Organizations.Count);
            Assert.AreEqual(detail.OrganizationId, result.Organizations[0].Id);
            Assert.AreEqual("DevOrg", result.Organizations[0].UniqueName);
            Assert.AreEqual("https://crm.example.test/DevOrg/XRMServices/2011/Organization.svc", result.Organizations[0].ServiceUri.AbsoluteUri);
        }

        [TestMethod]
        public void TestDiscoveryFailsExplicitlyWhenEmptyOrMissingEndpoint()
        {
            Assert.ThrowsException<InvalidOperationException>(() => new CrmDiscoveryResult(new Uri("https://crm.example.test"),
                CredentialCache.DefaultNetworkCredentials, Array.Empty<OrganizationDetail>()));
            var detail = Organization("DevOrg");
            detail.Endpoints.Clear();
            Assert.ThrowsException<InvalidDataException>(() => new CrmDiscoveryResult(new Uri("https://crm.example.test"),
                CredentialCache.DefaultNetworkCredentials, new[] { detail }));
        }

        [TestMethod]
        public async Task TestDiscoveryRequiresDevelopmentConfirmationAndExplicitCredentialsBeforeNetworkAccess()
        {
            using (var session = new CrmSession())
            {
                await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
                    await session.DiscoverAsync("crm.example.test", "", true, false));
                await Assert.ThrowsExceptionAsync<ArgumentException>(async () =>
                    await session.DiscoverAsync("crm.example.test", "", true, true, false));
            }
        }

        [TestMethod]
        public async Task TestConnectionRejectsOrganizationFromDifferentDiscoveryResultBeforeNetworkAccess()
        {
            var server = new Uri("https://crm.example.test");
            var result = new CrmDiscoveryResult(server, CredentialCache.DefaultNetworkCredentials, new[] { Organization("DevOrg") });
            var other = new CrmDiscoveryResult(server, CredentialCache.DefaultNetworkCredentials, new[] { Organization("OtherOrg") });
            using (var session = new CrmSession())
            {
                await Assert.ThrowsExceptionAsync<ArgumentException>(async () =>
                    await session.ConnectAsync(result, other.Organizations[0], true));
                Assert.IsFalse(session.IsConnected);
            }
        }

        private static OrganizationDetail Organization(string uniqueName)
        {
            var detail = new OrganizationDetail
            {
                OrganizationId = Guid.NewGuid(),
                UniqueName = uniqueName,
                FriendlyName = "Development"
            };
            detail.Endpoints.Add(EndpointType.OrganizationService, $"https://crm.example.test/{uniqueName}/XRMServices/2011/Organization.svc");
            return detail;
        }
    }
}
