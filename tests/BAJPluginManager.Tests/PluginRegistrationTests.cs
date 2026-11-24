using BAJPluginManager.Core;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Messages;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BAJPluginManager.Tests
{
    [TestClass]
    public class PluginRegistrationTests
    {
        private readonly Guid typeId = Guid.NewGuid();
        private readonly Guid messageId = Guid.NewGuid();
        private readonly Guid filterId = Guid.NewGuid();
        private readonly Guid stepId = Guid.NewGuid();
        private readonly Guid assemblyId = Guid.NewGuid();

        [TestMethod]
        public void TestNewStepUsesSelectedTypeMessageEntityAndExecutionSettings()
        {
            var fake = CreateService();
            var model = NewStep();
            var id = RegistrationService(fake).SaveStep(model);
            Assert.AreEqual(fake.CreatedId, id);
            var created = fake.Created!;
            Assert.AreEqual("sdkmessageprocessingstep", created.LogicalName);
            Assert.AreEqual(typeId, created.GetAttributeValue<EntityReference>("eventhandler").Id);
            Assert.AreEqual(messageId, created.GetAttributeValue<EntityReference>("sdkmessageid").Id);
            Assert.AreEqual(filterId, created.GetAttributeValue<EntityReference>("sdkmessagefilterid").Id);
            Assert.AreEqual(20, created.GetAttributeValue<OptionSetValue>("stage").Value);
            Assert.AreEqual(0, created.GetAttributeValue<OptionSetValue>("mode").Value);
            Assert.AreEqual(0, created.GetAttributeValue<OptionSetValue>("supporteddeployment").Value);
            Assert.AreEqual("name,address1_city", created.GetAttributeValue<string>("filteringattributes"));
        }

        [TestMethod]
        public void TestStepUpdatePreservesIdentitySecureConfigurationRunAsDeploymentAndState()
        {
            var fake = CreateService();
            var step = NewStep();
            step.Id = stepId;
            step.Rank = 3;
            step.UnsecureConfiguration = "<configuration />";
            RegistrationService(fake).SaveStep(step);
            var update = fake.Updated!;
            Assert.AreEqual(stepId, update.Id);
            Assert.AreEqual(3, update.GetAttributeValue<int>("rank"));
            Assert.AreEqual("<configuration />", update.GetAttributeValue<string>("configuration"));
            foreach (var preserved in new[] { "eventhandler", "sdkmessageid", "sdkmessagefilterid", "sdkmessageprocessingstepsecureconfigid",
                "impersonatinguserid", "supporteddeployment", "statecode", "statuscode" })
                Assert.IsFalse(update.Contains(preserved), preserved + " must not be overwritten.");
        }

        [TestMethod]
        public void TestExistingStepCannotBeMovedToAnotherTypeMessageOrEntity()
        {
            foreach (var change in new Action<StepRegistration>[]
            {
                step => step.PluginTypeId = Guid.NewGuid(),
                step => step.MessageId = Guid.NewGuid(),
                step => step.FilterId = null
            })
            {
                var fake = CreateService();
                var model = NewStep();
                model.Id = stepId;
                change(model);
                Assert.ThrowsException<InvalidOperationException>(() => RegistrationService(fake).SaveStep(model));
                Assert.IsNull(fake.Updated);
            }
        }

        [TestMethod]
        public void TestEntityFilterMustBelongToSelectedMessage()
        {
            var fake = CreateService();
            fake.Records[("sdkmessagefilter", filterId)]["sdkmessageid"] = new EntityReference("sdkmessage", Guid.NewGuid());
            Assert.ThrowsException<InvalidOperationException>(() => RegistrationService(fake).SaveStep(NewStep()));
            Assert.IsNull(fake.Created);
        }

        [TestMethod]
        public void TestManagedAndNonCustomizableStepsRejectChanges()
        {
            foreach (var managed in new[] { true, false })
            {
                var fake = CreateService();
                var existing = fake.Records[("sdkmessageprocessingstep", stepId)];
                existing["ismanaged"] = managed;
                existing["iscustomizable"] = new BooleanManagedProperty(managed);
                var step = NewStep();
                step.Id = stepId;
                Assert.ThrowsException<InvalidOperationException>(() => RegistrationService(fake).SaveStep(step));
                Assert.ThrowsException<InvalidOperationException>(() => RegistrationService(fake).SetStepEnabled(stepId, false));
                Assert.ThrowsException<InvalidOperationException>(() => RegistrationService(fake).UnregisterStep(
                    RegistrationService(fake).GetStep(stepId, false)));
                Assert.IsNull(fake.Updated);
                Assert.IsNull(fake.Executed);
                Assert.IsNull(fake.Deleted);
            }
        }

        [TestMethod]
        public void TestUnregisterDeletesOnlySelectedStepAndPropagatesServerFailure()
        {
            var fake = CreateService();
            var service = RegistrationService(fake);
            var selected = service.GetStep(stepId, false);
            service.UnregisterStep(selected);
            Assert.AreEqual(("sdkmessageprocessingstep", stepId), fake.Deleted!.Value);
            Assert.IsNull(fake.Created);
            Assert.IsNull(fake.Updated);
            Assert.IsNull(fake.Executed);
            var failure = new InvalidOperationException("CRM dependency prevents deletion.");
            fake.DeleteFailure = failure;
            Assert.AreSame(failure, Assert.ThrowsException<InvalidOperationException>(() => service.UnregisterStep(selected)));
        }

        [TestMethod]
        public void TestUnregisterRejectsMissingAndChangedIdentityBeforeDeleting()
        {
            var fake = CreateService();
            var service = RegistrationService(fake);
            Assert.ThrowsException<ArgumentException>(() => service.UnregisterStep(new StepRegistration()));
            Assert.AreEqual(0, fake.RetrieveCount);
            var selected = service.GetStep(stepId, false);
            fake.Records[("sdkmessageprocessingstep", stepId)]["eventhandler"] = new EntityReference("plugintype", Guid.NewGuid());
            Assert.ThrowsException<InvalidOperationException>(() => service.UnregisterStep(selected));
            Assert.IsNull(fake.Deleted);
        }

        [TestMethod]
        public void TestUnregisterPluginTypeDeletesItsStepsAndTypeInOneTransaction()
        {
            var fake = CreateService();
            fake.QueryResults.Add(fake.Records[("sdkmessageprocessingstep", stepId)]);
            var selected = new PluginTypeInfo { Id = typeId, Name = "Example.Plugin" };
            RegistrationService(fake).UnregisterPluginType(selected, assemblyId);
            var request = fake.Executed as ExecuteTransactionRequest;
            Assert.IsNotNull(request);
            Assert.AreEqual(2, request.Requests.Count);
            var targets = request.Requests.Cast<DeleteRequest>().Select(item => item.Target).ToArray();
            Assert.AreEqual("sdkmessageprocessingstep", targets[0].LogicalName);
            Assert.AreEqual(stepId, targets[0].Id);
            Assert.AreEqual("plugintype", targets[1].LogicalName);
            Assert.AreEqual(typeId, targets[1].Id);
            Assert.AreEqual(typeId, fake.LastQuery!.Criteria.Conditions.Single().Values.Single());
            Assert.IsNull(fake.Deleted, "Deletes must not be submitted individually.");
            Assert.IsNull(fake.Updated);
            var failure = new InvalidOperationException("CRM dependency blocks the transaction.");
            fake.ExecuteFailure = failure;
            Assert.AreSame(failure, Assert.ThrowsException<InvalidOperationException>(() =>
                RegistrationService(fake).UnregisterPluginType(selected, assemblyId)));
        }

        [TestMethod]
        public void TestUnregisterPluginTypeWithoutStepsDeletesOnlyTheType()
        {
            var fake = CreateService();
            RegistrationService(fake).UnregisterPluginType(new PluginTypeInfo { Id = typeId, Name = "Example.Plugin" }, assemblyId);
            var request = (ExecuteTransactionRequest)fake.Executed!;
            Assert.AreEqual(1, request.Requests.Count);
            Assert.AreEqual(typeId, ((DeleteRequest)request.Requests[0]).Target.Id);
        }

        [TestMethod]
        public void TestUnregisterPluginTypeRejectsProtectedOrChangedRegistrations()
        {
            foreach (var change in new Action<FakeOrganizationService>[]
            {
                fake => fake.Records[("plugintype", typeId)]["ismanaged"] = true,
                fake => fake.Records[("plugintype", typeId)].Attributes.Remove("ismanaged"),
                fake => fake.Records[("plugintype", typeId)]["isworkflowactivity"] = true,
                fake => fake.Records[("plugintype", typeId)].Attributes.Remove("isworkflowactivity"),
                fake => fake.Records[("plugintype", typeId)]["pluginassemblyid"] = new EntityReference("pluginassembly", Guid.NewGuid()),
                fake => fake.Records[("plugintype", typeId)]["typename"] = "Changed.Plugin",
                fake => fake.Records[("pluginassembly", assemblyId)]["ismanaged"] = true,
                fake => fake.Records[("pluginassembly", assemblyId)].Attributes.Remove("ismanaged"),
                fake => fake.Records[("sdkmessageprocessingstep", stepId)]["ismanaged"] = true,
                fake => fake.Records[("sdkmessageprocessingstep", stepId)]["iscustomizable"] = new BooleanManagedProperty(false)
            })
            {
                var fake = CreateService();
                fake.QueryResults.Add(fake.Records[("sdkmessageprocessingstep", stepId)]);
                change(fake);
                Assert.ThrowsException<InvalidOperationException>(() => RegistrationService(fake).UnregisterPluginType(
                    new PluginTypeInfo { Id = typeId, Name = "Example.Plugin" }, assemblyId));
                Assert.IsNull(fake.Executed);
                Assert.IsNull(fake.Deleted);
            }
            var invalid = CreateService();
            Assert.ThrowsException<ArgumentException>(() => RegistrationService(invalid).UnregisterPluginType(new PluginTypeInfo(), assemblyId));
            Assert.ThrowsException<ArgumentException>(() => RegistrationService(invalid).UnregisterPluginType(
                new PluginTypeInfo { Id = typeId, IsWorkflowActivity = true }, assemblyId));
            Assert.AreEqual(0, invalid.RetrieveCount);
        }

        [DataTestMethod]
        [DataRow(true, 0, 1)]
        [DataRow(false, 1, 2)]
        public void TestStepStateUsesExplicitEnabledDisabledStatus(bool enabled, int state, int status)
        {
            var fake = CreateService();
            RegistrationService(fake).SetStepEnabled(stepId, enabled);
            var request = fake.Executed as SetStateRequest;
            Assert.IsNotNull(request);
            Assert.AreEqual(stepId, request.EntityMoniker.Id);
            Assert.AreEqual("sdkmessageprocessingstep", request.EntityMoniker.LogicalName);
            Assert.AreEqual(state, request.State.Value);
            Assert.AreEqual(status, request.Status.Value);
        }

        [TestMethod]
        public void TestChangingStageCannotInvalidateRegisteredImages()
        {
            var fake = CreateService();
            fake.Records[("sdkmessageprocessingstep", stepId)]["stage"] = new OptionSetValue(40);
            fake.QueryResults.Add(ImageEntity(Guid.NewGuid(), 1));
            var step = NewStep();
            step.Id = stepId;
            Assert.ThrowsException<ArgumentException>(() => RegistrationService(fake).SaveStep(step));
            Assert.IsNull(fake.Updated);
        }

        [TestMethod]
        public void TestImageCreationUsesSelectedStepAliasAndExplicitAttributes()
        {
            var fake = CreateService();
            var image = NewImage();
            RegistrationService(fake).SaveImage(image);
            Assert.AreEqual(stepId, fake.Created!.GetAttributeValue<EntityReference>("sdkmessageprocessingstepid").Id);
            Assert.AreEqual("Before", fake.Created.GetAttributeValue<string>("entityalias"));
            Assert.AreEqual("Target", fake.Created.GetAttributeValue<string>("messagepropertyname"));
            Assert.AreEqual("name,address1_city", fake.Created.GetAttributeValue<string>("attributes"));
        }

        [TestMethod]
        public void TestAllImageAttributesRequireExplicitOptIn()
        {
            var fake = CreateService();
            var image = NewImage();
            image.Attributes = "";
            Assert.ThrowsException<ArgumentException>(() => RegistrationService(fake).SaveImage(image));
            Assert.IsNull(fake.Created);
            image.AllAttributes = true;
            RegistrationService(fake).SaveImage(image);
            Assert.IsTrue(fake.Created!.Contains("attributes"));
            Assert.IsNull(fake.Created["attributes"]);
        }

        [TestMethod]
        public void TestImageUpdatePreservesItsParentAndRejectsMoves()
        {
            var fake = CreateService();
            var image = NewImage();
            image.Id = Guid.NewGuid();
            fake.Records[("sdkmessageprocessingstepimage", image.Id)] = ImageEntity(image.Id, 0);
            RegistrationService(fake).SaveImage(image);
            Assert.IsFalse(fake.Updated!.Contains("sdkmessageprocessingstepid"));
            fake.Updated = null;
            fake.Records[("sdkmessageprocessingstepimage", image.Id)]["sdkmessageprocessingstepid"] =
                new EntityReference("sdkmessageprocessingstep", Guid.NewGuid());
            Assert.ThrowsException<InvalidOperationException>(() => RegistrationService(fake).SaveImage(image));
            Assert.IsNull(fake.Updated);
        }

        [TestMethod]
        public void TestHierarchyQueriesAreScopedAndDoNotLoadConfiguration()
        {
            var fake = CreateService();
            var registrations = RegistrationService(fake);
            registrations.GetTypes(typeId);
            Assert.AreEqual("plugintype", fake.LastQuery!.EntityName);
            Assert.AreEqual(typeId, fake.LastQuery.Criteria.Conditions.Single().Values.Single());
            registrations.GetSteps(typeId);
            Assert.AreEqual("eventhandler", fake.LastQuery!.Criteria.Conditions.Single().AttributeName);
            Assert.AreEqual(typeId, fake.LastQuery.Criteria.Conditions.Single().Values.Single());
            Assert.IsFalse(fake.LastQuery.ColumnSet.Columns.Contains("configuration"));
            registrations.GetImages(stepId);
            Assert.AreEqual("sdkmessageprocessingstepid", fake.LastQuery!.Criteria.Conditions.Single().AttributeName);
            Assert.AreEqual(stepId, fake.LastQuery.Criteria.Conditions.Single().Values.Single());
            Assert.IsFalse(fake.LastQuery.ColumnSet.AllColumns);
        }

        [DataTestMethod]
        [DataRow(20, 1, 1)]
        [DataRow(10, 0, 0)]
        [DataRow(40, 2, 1)]
        public void TestInvalidExecutionSettingsFailBeforeSdkAccess(int stage, int mode, int rank)
        {
            var fake = new FakeOrganizationService();
            var step = NewStep();
            step.Stage = stage; step.Mode = mode; step.Rank = rank;
            Assert.ThrowsException<ArgumentException>(() => RegistrationService(fake).SaveStep(step));
            Assert.AreEqual(0, fake.RetrieveCount);
        }

        [DataTestMethod]
        [DataRow("Create", 40, 0)]
        [DataRow("Create", 40, 2)]
        [DataRow("Delete", 40, 1)]
        [DataRow("Update", 20, 1)]
        [DataRow("Update", 10, 0)]
        public void TestUnsupportedImageStageMessageCombinationsAreRejected(string message, int stage, int imageType)
        {
            var step = NewStep(); step.Id = stepId; step.MessageName = message; step.Stage = stage;
            var image = NewImage(); image.ImageType = imageType;
            Assert.ThrowsException<ArgumentException>(() => RegistrationRules.ValidateImage(image, step));
        }

        [DataTestMethod]
        [DataRow("Create", 40, 1)]
        [DataRow("Delete", 20, 0)]
        [DataRow("Update", 40, 2)]
        public void TestSupportedImageCombinationsRemainAvailable(string message, int stage, int imageType)
        {
            var step = NewStep(); step.Id = stepId; step.MessageName = message; step.Stage = stage;
            var image = NewImage(); image.ImageType = imageType;
            RegistrationRules.ValidateImage(image, step);
        }

        [TestMethod]
        public void TestAsynchronousPostOperationStepKeepsAsyncJobSetting()
        {
            var fake = CreateService();
            var step = NewStep();
            step.Stage = 40; step.Mode = 1; step.AsyncAutoDelete = true;
            RegistrationService(fake).SaveStep(step);
            Assert.AreEqual(40, fake.Created!.GetAttributeValue<OptionSetValue>("stage").Value);
            Assert.AreEqual(1, fake.Created.GetAttributeValue<OptionSetValue>("mode").Value);
            Assert.IsTrue(fake.Created.GetAttributeValue<bool>("asyncautodelete"));
        }

        private StepRegistration NewStep() => new StepRegistration
        {
            PluginTypeId = typeId, MessageId = messageId, MessageName = "Update", FilterId = filterId,
            Name = "Update account", FilteringAttributes = "Name, address1_city, name"
        };
        private ImageRegistration NewImage() => new ImageRegistration
        {
            StepId = stepId, Name = "Before", Alias = "Before", Attributes = "Name, address1_city, name"
        };
        private PluginRegistrationService RegistrationService(FakeOrganizationService fake) =>
            new PluginRegistrationService(fake, query => fake.RetrieveMultiple(query).Entities.ToList());

        private FakeOrganizationService CreateService()
        {
            var fake = new FakeOrganizationService();
            fake.Records[("plugintype", typeId)] = new Entity("plugintype", typeId)
            {
                ["ismanaged"] = false, ["isworkflowactivity"] = false, ["typename"] = "Example.Plugin",
                ["pluginassemblyid"] = new EntityReference("pluginassembly", assemblyId)
            };
            fake.Records[("pluginassembly", assemblyId)] = new Entity("pluginassembly", assemblyId) { ["ismanaged"] = false };
            fake.Records[("sdkmessage", messageId)] = new Entity("sdkmessage", messageId) { ["name"] = "Update", ["isprivate"] = false };
            fake.Records[("sdkmessagefilter", filterId)] = new Entity("sdkmessagefilter", filterId)
            {
                ["sdkmessageid"] = new EntityReference("sdkmessage", messageId), ["iscustomprocessingstepallowed"] = true
            };
            fake.Records[("sdkmessageprocessingstep", stepId)] = new Entity("sdkmessageprocessingstep", stepId)
            {
                ["name"] = "Update account", ["eventhandler"] = new EntityReference("plugintype", typeId),
                ["sdkmessageid"] = new EntityReference("sdkmessage", messageId) { Name = "Update" },
                ["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter", filterId),
                ["stage"] = new OptionSetValue(20), ["mode"] = new OptionSetValue(0), ["rank"] = 1,
                ["statecode"] = new OptionSetValue(0), ["ismanaged"] = false, ["iscustomizable"] = new BooleanManagedProperty(true)
            };
            return fake;
        }
        private Entity ImageEntity(Guid id, int imageType) => new Entity("sdkmessageprocessingstepimage", id)
        {
            ["name"] = "Before", ["sdkmessageprocessingstepid"] = new EntityReference("sdkmessageprocessingstep", stepId),
            ["entityalias"] = "Before", ["imagetype"] = new OptionSetValue(imageType), ["messagepropertyname"] = "Target",
            ["attributes"] = "name", ["ismanaged"] = false, ["iscustomizable"] = new BooleanManagedProperty(true)
        };

        private sealed class FakeOrganizationService : IOrganizationService
        {
            internal readonly Dictionary<(string, Guid), Entity> Records = new Dictionary<(string, Guid), Entity>();
            internal readonly List<Entity> QueryResults = new List<Entity>();
            internal readonly Guid CreatedId = Guid.NewGuid();
            internal Entity? Created;
            internal Entity? Updated;
            internal OrganizationRequest? Executed;
            internal QueryExpression? LastQuery;
            internal int RetrieveCount;
            internal (string, Guid)? Deleted;
            internal Exception? DeleteFailure;
            internal Exception? ExecuteFailure;
            public Guid Create(Entity entity) { Created = entity; return CreatedId; }
            public void Update(Entity entity) => Updated = entity;
            public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) { RetrieveCount++; return Records[(entityName, id)]; }
            public EntityCollection RetrieveMultiple(QueryBase query)
            {
                LastQuery = (QueryExpression)query;
                return new EntityCollection(QueryResults.ToList());
            }
            public OrganizationResponse Execute(OrganizationRequest request)
            {
                if (ExecuteFailure != null) throw ExecuteFailure;
                Executed = request;
                return new OrganizationResponse();
            }
            public void Delete(string entityName, Guid id)
            {
                if (DeleteFailure != null) throw DeleteFailure;
                Deleted = (entityName, id);
            }
            public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
            public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        }
    }
}
