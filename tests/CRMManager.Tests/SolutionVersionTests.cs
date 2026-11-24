using CRMManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;

namespace CRMManager.Tests
{
    [TestClass]
    public class SolutionVersionTests
    {
        [DataTestMethod]
        [DataRow("1.0.0")]
        [DataRow("1.0.0.0.1")]
        [DataRow("1.-1.0.0")]
        [DataRow("1.0..0")]
        [DataRow("1.0.0.x")]
        [DataRow("1.0.0.2147483648")]
        public void TestSolutionVersionRejectsInvalidInput(string version) =>
            Assert.ThrowsException<ArgumentException>(() => DeploymentRules.NormalizeSolutionVersion(version));

        [TestMethod]
        public void TestSolutionVersionUpdatesOnlyVersionAndSkipsUnchanged()
        {
            var solution = new CrmItem { Id = Guid.NewGuid(), Name = "CustomSolution", Version = "1.0.0.49" };
            var fake = new SolutionService(solution);
            Assert.IsTrue(CrmSession.UpdateSolutionVersion(fake, solution, " 01.0.0.50 "));
            Assert.AreEqual(solution.Id, fake.Updated!.Id);
            Assert.AreEqual("solution", fake.Updated.LogicalName);
            Assert.AreEqual(1, fake.Updated.Attributes.Count);
            Assert.AreEqual("1.0.0.50", fake.Updated["version"]);
            fake.Updated = null;
            Assert.IsFalse(CrmSession.UpdateSolutionVersion(fake, solution, "1.0.0.49"));
            Assert.IsNull(fake.Updated);
        }

        [TestMethod]
        public void TestSolutionVersionRejectsStaleManagedAndSystemSolutions()
        {
            var solution = new CrmItem { Id = Guid.NewGuid(), Name = "CustomSolution", Version = "1.0.0.49" };
            var fake = new SolutionService(solution);
            fake.Current["version"] = "1.0.0.51";
            Assert.ThrowsException<InvalidOperationException>(() => CrmSession.UpdateSolutionVersion(fake, solution, "1.0.0.50"));
            fake.Current["version"] = solution.Version;
            fake.Current["ismanaged"] = true;
            Assert.ThrowsException<InvalidOperationException>(() => CrmSession.UpdateSolutionVersion(fake, solution, "1.0.0.50"));
            fake.Current["ismanaged"] = false;
            solution.Name = "Default";
            fake.Current["uniquename"] = "Default";
            Assert.ThrowsException<InvalidOperationException>(() => CrmSession.UpdateSolutionVersion(fake, solution, "1.0.0.50"));
            Assert.IsNull(fake.Updated);
        }

        private sealed class SolutionService : IOrganizationService
        {
            public SolutionService(CrmItem solution)
            {
                Current = new Entity("solution", solution.Id)
                {
                    ["uniquename"] = solution.Name, ["version"] = solution.Version, ["ismanaged"] = false
                };
            }

            public Entity Current { get; }
            public Entity? Updated { get; set; }
            public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => Current;
            public void Update(Entity entity) => Updated = entity;
            public Guid Create(Entity entity) => throw new NotSupportedException();
            public void Delete(string entityName, Guid id) => throw new NotSupportedException();
            public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();
            public EntityCollection RetrieveMultiple(QueryBase query) => throw new NotSupportedException();
            public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
            public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        }
    }
}
