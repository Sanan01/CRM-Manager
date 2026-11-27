using CRMManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

namespace CRMManager.Tests
{
    [TestClass]
    public class DataExportTests
    {
        private const string Fetch = "<fetch><entity name=\"new_widget\"><attribute name=\"new_name\"/><attribute name=\"new_parent\"/>"
            + "<attribute name=\"createdby\"/><filter><condition attribute=\"statecode\" operator=\"eq\" value=\"0\"/></filter></entity></fetch>";

        [TestMethod]
        public void TestFetchXmlIsValidated()
        {
            Assert.AreEqual("new_widget", DataExporter.ParseFetch(Fetch, out _));
            Assert.ThrowsException<ArgumentException>(() => DataExporter.ParseFetch("  ", out _));
            Assert.ThrowsException<ArgumentException>(() => DataExporter.ParseFetch("<fetch>", out _));
            Assert.ThrowsException<ArgumentException>(() => DataExporter.ParseFetch("<fetch><entity/></fetch>", out _));
            Assert.ThrowsException<ArgumentException>(() => DataExporter.ParseFetch("<fetch aggregate=\"true\"><entity name=\"account\"/></fetch>", out _));
        }

        [TestMethod]
        public void TestPackageContainsMigrationSchemaAndRecords()
        {
            var id = Guid.NewGuid();
            var parent = Guid.NewGuid();
            var record = new Entity("new_widget", id)
            {
                ["new_name"] = "A & B\u0001",
                ["new_parent"] = new EntityReference("account", parent) { Name = "Contoso" },
                ["statecode"] = new OptionSetValue(0),
                ["new_amount"] = new Money(12.5m)
            };
            var service = new FakeService(record);
            var package = DataExporter.BuildPackage(service, Fetch, null, out var count, out _);
            Assert.AreEqual(1, count);
            StringAssert.Contains(service.Fetch!, "name=\"new_widgetid\"");

            using (var archive = new ZipArchive(new MemoryStream(package)))
            {
                CollectionAssert.AreEquivalent(new[] { "[Content_Types].xml", "data.xml", "data_schema.xml" },
                    archive.Entries.Select(e => e.FullName).ToArray());
                var schema = Read(archive, "data_schema.xml");
                StringAssert.Contains(schema, "primaryidfield=\"new_widgetid\"");
                StringAssert.Contains(schema, "lookupType=\"account\"");
                Assert.IsFalse(schema.Contains("createdby"));
                Assert.IsFalse(schema.Contains("new_amount"), "Columns outside the FetchXML must not be exported.");
                var data = Read(archive, "data.xml");
                StringAssert.Contains(data, $"id=\"{id}\"");
                StringAssert.Contains(data, "value=\"A &amp; B\"");
                StringAssert.Contains(data, $"value=\"{parent}\" lookupentity=\"account\" lookupentityname=\"Contoso\"");
                Assert.IsFalse(data.Contains("new_amount"));
            }
        }

        [TestMethod]
        public void TestManyToManyLinksAreExportedOnlyWhenRequested()
        {
            var id = Guid.NewGuid();
            var role = Guid.NewGuid();
            var service = new FakeService(new Entity("new_widget", id) { ["new_name"] = "W" })
            {
                Links = new[] { new Entity("new_widget_role") { ["new_widgetid"] = id, ["roleid"] = role } }
            };

            DataExporter.BuildPackage(service, Fetch, null, out _, out var none);
            Assert.AreEqual(0, none);

            DataExporter.BuildPackage(service, Fetch, new string[0], out _, out var unselected);
            Assert.AreEqual(0, unselected);

            var package = DataExporter.BuildPackage(service, Fetch, new[] { "new_widget_role" }, out var records, out var links);
            Assert.AreEqual(1, records);
            Assert.AreEqual(1, links);
            using (var archive = new ZipArchive(new MemoryStream(package)))
            {
                StringAssert.Contains(Read(archive, "data_schema.xml"),
                    "name=\"new_widget_role\" manyToMany=\"true\" isreflexive=\"false\" relatedEntityName=\"new_widget_role\" m2mTargetEntity=\"role\" m2mTargetEntityPrimaryKey=\"roleid\"");
                var data = Read(archive, "data.xml");
                StringAssert.Contains(data, $"sourceid=\"{id}\" targetentityname=\"role\" targetentitynameidfield=\"roleid\"");
                StringAssert.Contains(data, $"<targetid>{role}</targetid>");
                StringAssert.Contains(data, "<relationshipname>new_widget_role</relationshipname>");
            }
        }

        [TestMethod]
        public void TestFetchXmlBuilderUsesColumnsAndFilters()
        {
            var fetch = DataExporter.BuildFetchXml("new_widget", new[] { "new_name" }, new[]
            {
                new FilterCondition { Column = "new_name", Operator = "like", Value = "A&B%" },
                new FilterCondition { Column = "new_parent", Operator = "null" }
            }, true);
            Assert.AreEqual("new_widget", DataExporter.ParseFetch(fetch, out var document));
            var entity = document.Root!.Element("entity")!;
            Assert.AreEqual("new_name", entity.Element("attribute")!.Attribute("name")!.Value);
            Assert.AreEqual("or", entity.Element("filter")!.Attribute("type")!.Value);
            var conditions = entity.Element("filter")!.Elements("condition").ToList();
            Assert.AreEqual("A&B%", conditions[0].Attribute("value")!.Value);
            Assert.IsNull(conditions[1].Attribute("value"));

            StringAssert.Contains(DataExporter.BuildFetchXml("new_widget", new string[0], new FilterCondition[0], false), "all-attributes");
            Assert.ThrowsException<ArgumentException>(() => DataExporter.BuildFetchXml("new_widget", new string[0],
                new[] { new FilterCondition { Column = "new_name", Operator = "eq", Value = "" } }, false));
        }

        [TestMethod]
        public void TestTableDetailsListExportableColumnsAndRelationships()
        {
            var details = DataExporter.GetTableDetails(new FakeService(new Entity("new_widget")), "new_widget");
            CollectionAssert.AreEqual(new[] { "new_widgetid", "new_amount", "new_name", "new_parent", "statecode" },
                details.Columns.Select(c => c.LogicalName).OrderBy(n => n == "new_widgetid" ? "" : n, StringComparer.Ordinal).ToArray());
            Assert.AreEqual("new_widget_role", details.Relationships.Single().SchemaName);
            Assert.AreEqual("role", details.Relationships.Single().TargetEntity);
        }

        private static string Read(ZipArchive archive, string name)
        {
            using (var reader = new StreamReader(archive.GetEntry(name)!.Open()))
            {
                return reader.ReadToEnd();
            }
        }

        private static AttributeMetadata Attribute<T>(string name, AttributeTypeCode type, bool create = true) where T : AttributeMetadata, new()
        {
            var attribute = new T { LogicalName = name };
            Set(attribute, "AttributeType", type);
            Set(attribute, "IsValidForRead", true);
            Set(attribute, "IsValidForCreate", create);
            Set(attribute, "IsValidForUpdate", create);
            return attribute;
        }

        private static void Set(object target, string property, object value) =>
            target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.SetValue(target, value);

        private sealed class FakeService : IOrganizationService
        {
            private readonly Entity record;
            public FakeService(Entity record) => this.record = record;

            public Entity[] Links { get; set; } = new Entity[0];

            public OrganizationResponse Execute(OrganizationRequest request)
            {
                var response = new RetrieveEntityResponse();
                if (((RetrieveEntityRequest)request).LogicalName == "role")
                {
                    var role = new EntityMetadata { LogicalName = "role" };
                    Set(role, "PrimaryIdAttribute", "roleid");
                    response.Results["EntityMetadata"] = role;
                    return response;
                }

                var metadata = new EntityMetadata { LogicalName = "new_widget" };
                Set(metadata, "PrimaryIdAttribute", "new_widgetid");
                Set(metadata, "PrimaryNameAttribute", "new_name");
                Set(metadata, "ManyToManyRelationships", new[]
                {
                    new ManyToManyRelationshipMetadata
                    {
                        SchemaName = "new_widget_role", IntersectEntityName = "new_widget_role",
                        Entity1LogicalName = "new_widget", Entity1IntersectAttribute = "new_widgetid",
                        Entity2LogicalName = "role", Entity2IntersectAttribute = "roleid"
                    }
                });
                var lookup = (LookupAttributeMetadata)Attribute<LookupAttributeMetadata>("new_parent", AttributeTypeCode.Lookup);
                lookup.Targets = new[] { "account" };
                Set(metadata, "Attributes", new[]
                {
                    Attribute<UniqueIdentifierAttributeMetadata>("new_widgetid", AttributeTypeCode.Uniqueidentifier, false),
                    Attribute<StringAttributeMetadata>("new_name", AttributeTypeCode.String),
                    lookup,
                    Attribute<StateAttributeMetadata>("statecode", AttributeTypeCode.State),
                    Attribute<MoneyAttributeMetadata>("new_amount", AttributeTypeCode.Money),
                    Attribute<LookupAttributeMetadata>("createdby", AttributeTypeCode.Lookup, false)
                });
                response = new RetrieveEntityResponse();
                response.Results["EntityMetadata"] = metadata;
                return response;
            }

            public string? Fetch { get; private set; }
            public EntityCollection RetrieveMultiple(QueryBase query)
            {
                if (query is QueryExpression)
                {
                    return new EntityCollection(Links);
                }

                Fetch = ((FetchExpression)query).Query;
                return new EntityCollection(new[] { record });
            }
            public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
            public Guid Create(Entity entity) => throw new NotSupportedException();
            public void Update(Entity entity) => throw new NotSupportedException();
            public void Delete(string entityName, Guid id) => throw new NotSupportedException();
            public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
            public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        }
    }
}
