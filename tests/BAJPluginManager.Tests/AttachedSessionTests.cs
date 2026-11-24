using BAJPluginManager.Core;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Threading.Tasks;

namespace BAJPluginManager.Tests
{
    [TestClass]
    public class AttachedSessionTests
    {
        [TestMethod]
        public async Task TestOwnedAttachedSessionDisposesOnlyItsOwnService()
        {
            var host = new HostService();
            var independent = new HostService();
            using (var session = CrmSession.AttachOwned(independent, "https://crm.example.test/DevOrg", "Development", "9.1.23.10"))
            {
                await session.GetAssembliesAsync();
                Assert.AreEqual(1, independent.QueryCount);
                Assert.AreEqual(0, host.QueryCount);
                await session.DisconnectAsync();
                Assert.IsTrue(independent.Disposed);
                Assert.IsFalse(host.Disposed);
            }
            Assert.AreEqual(1, independent.DisposeCount);
            var second = new HostService();
            CrmSession.AttachOwned(second, "https://crm.example.test/DevOrg", "Development", "9.1.23.10").Dispose();
            Assert.AreEqual(1, second.DisposeCount);
        }

        [TestMethod]
        public void TestOwnedAttachedSessionDisposesServiceWhenValidationFails()
        {
            var service = new HostService();
            Assert.ThrowsException<NotSupportedException>(() =>
                CrmSession.AttachOwned(service, "https://crm.example.test/DevOrg", "Development", "9.2.0.0"));
            Assert.AreEqual(1, service.DisposeCount);
            Assert.AreEqual(0, service.ExecuteCount);
        }

        [TestMethod]
        public void TestAttachedSessionUsesHostVersionWithoutStartupRequests()
        {
            var service = new HostService();
            using (var session = CrmSession.Attach(service, "https://crm.example.test/DevOrg", "Development", "9.1.23.10"))
            {
                Assert.IsTrue(session.IsConnected);
                Assert.AreEqual("Development", session.OrganizationName);
                Assert.AreEqual("9.1.23.10", session.ServerVersion);
                Assert.AreEqual(0, service.ExecuteCount);
                Assert.AreEqual(0, service.QueryCount);
            }
            Assert.IsFalse(service.Disposed);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("not-a-version")]
        [DataRow("9.2.0.0")]
        [DataRow("8.1.0.0")]
        public void TestAttachedSessionRejectsInvalidHostVersionWithoutRequests(string version)
        {
            var service = new HostService();
            Assert.ThrowsException<NotSupportedException>(() =>
                CrmSession.Attach(service, "https://crm.example.test/DevOrg", "Development", version));
            Assert.AreEqual(0, service.ExecuteCount);
            Assert.IsFalse(service.Disposed);
        }

        [TestMethod]
        public async Task TestAttachedSessionUsesHostServiceWithoutDisposingIt()
        {
            var service = new HostService();
            using (var session = await CrmSession.AttachAsync(service, "https://crm.example.test/DevOrg", "Development"))
            {
                Assert.IsTrue(session.IsConnected);
                Assert.AreEqual("Development", session.OrganizationName);
                Assert.AreEqual("9.1.23.10", session.ServerVersion);
                await session.GetSolutionsAsync();
                Assert.AreEqual(1, service.QueryCount);
                await session.DisconnectAsync();
                Assert.IsFalse(session.IsConnected);
                Assert.IsFalse(service.Disposed);
            }
            Assert.IsFalse(service.Disposed);
            using (var session = await CrmSession.AttachAsync(service, "https://crm.example.test/DevOrg", "Development"))
                await session.GetAssembliesAsync();
            Assert.AreEqual(2, service.QueryCount);
            Assert.IsFalse(service.Disposed);
        }

        [TestMethod]
        public async Task TestAttachedSessionRejectsUnsupportedVersionWithoutOwningHostService()
        {
            var service = new HostService { Version = "9.2.0.0" };
            await Assert.ThrowsExceptionAsync<NotSupportedException>(() =>
                CrmSession.AttachAsync(service, "https://crm.example.test/DevOrg", "Development"));
            Assert.IsFalse(service.Disposed);
            Assert.AreEqual(0, service.QueryCount);
        }

        private sealed class HostService : IOrganizationService, IDisposable
        {
            public string Version { get; set; } = "9.1.23.10";
            public int QueryCount { get; private set; }
            public int ExecuteCount { get; private set; }
            public bool Disposed { get; private set; }
            public int DisposeCount { get; private set; }
            public OrganizationResponse Execute(OrganizationRequest request)
            {
                ExecuteCount++;
                if (request is WhoAmIRequest) return new WhoAmIResponse();
                if (request is RetrieveVersionRequest)
                    return new RetrieveVersionResponse { Results = new ParameterCollection { ["Version"] = Version } };
                throw new NotSupportedException();
            }
            public EntityCollection RetrieveMultiple(QueryBase query)
            {
                QueryCount++;
                return new EntityCollection();
            }
            public void Dispose()
            {
                DisposeCount++;
                Disposed = true;
            }
            public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
            public void Update(Entity entity) => throw new NotSupportedException();
            public Guid Create(Entity entity) => throw new NotSupportedException();
            public void Delete(string entityName, Guid id) => throw new NotSupportedException();
            public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
            public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        }
    }
}
