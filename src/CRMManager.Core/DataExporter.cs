using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CRMManager.Core
{
    /// <summary>
    /// Exports table records as a Configuration Migration (data migration) tool package:
    /// a ZIP containing data_schema.xml, data.xml and [Content_Types].xml.
    /// </summary>
    public static class DataExporter
    {
        private static readonly HashSet<string> ExcludedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "versionnumber", "importsequencenumber", "timezoneruleversionnumber", "utcconversiontimezonecode",
            "overriddencreatedon", "processid", "stageid", "traversedpath"
        };

        /// <summary>Validates FetchXML (for example from Advanced Find) and returns its root table name.</summary>
        public static string ParseFetch(string? fetchXml, out XDocument document)
        {
            if (string.IsNullOrWhiteSpace(fetchXml))
            {
                throw new ArgumentException("Paste the FetchXML to export, for example from Advanced Find > Download Fetch XML.");
            }

            try
            {
                document = XDocument.Parse(fetchXml);
            }
            catch (XmlException exception)
            {
                throw new ArgumentException("The FetchXML is not valid XML: " + exception.Message, exception);
            }

            var entity = document.Root?.Name.LocalName == "fetch" ? document.Root.Element("entity") : null;
            var name = entity?.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("The FetchXML must be a <fetch> element containing an <entity name=\"...\"> element.");
            }

            if (string.Equals(document.Root!.Attribute("aggregate")?.Value, "true", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Aggregate FetchXML returns summaries, not records, and cannot be exported.");
            }

            return name!.Trim().ToLowerInvariant();
        }

        /// <param name="relationships">Schema names of many-to-many relationships to include; null exports none.</param>
        public static byte[] BuildPackage(IOrganizationService service, string fetchXml, IReadOnlyCollection<string>? relationships,
            out int recordCount, out int linkCount)
        {
            recordCount = 0;
            linkCount = 0;
            var name = ParseFetch(fetchXml, out var fetch);
            var metadata = RetrieveMetadata(service, name);
            var fields = SelectFields(metadata);
            var entity = fetch.Root!.Element("entity")!;
            var requested = entity.Elements("attribute").Select(a => a.Attribute("name")?.Value).Where(n => n != null).ToList();
            if (entity.Element("all-attributes") == null && requested.Count > 0)
            {
                fields = fields.Where(f => f.LogicalName == metadata.PrimaryIdAttribute || requested.Contains(f.LogicalName)).ToList();
            }

            if (entity.Element("all-attributes") == null && !requested.Contains(metadata.PrimaryIdAttribute))
            {
                entity.AddFirst(new XElement("attribute", new XAttribute("name", metadata.PrimaryIdAttribute)));
            }

            if (!entity.Elements("order").Any())
            {
                entity.Add(new XElement("order", new XAttribute("attribute", metadata.PrimaryIdAttribute)));
            }

            var schema = new MemoryStream();
            var data = new MemoryStream();
            var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
            using (var schemaWriter = XmlWriter.Create(schema, settings))
            using (var dataWriter = XmlWriter.Create(data, settings))
            {
                schemaWriter.WriteStartDocument();
                schemaWriter.WriteStartElement("entities");
                schemaWriter.WriteAttributeString("version", "9.1.0.0");
                dataWriter.WriteStartDocument();
                dataWriter.WriteStartElement("entities");
                dataWriter.WriteAttributeString("timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

                var relations = relationships != null ? GetRelations(service, metadata, relationships) : new List<M2MRelation>();
                WriteSchema(schemaWriter, metadata, fields, relations);
                recordCount = WriteData(dataWriter, service, metadata, fields, fetch, relations, out linkCount);

                schemaWriter.WriteEndElement();
                schemaWriter.WriteEndDocument();
                dataWriter.WriteEndElement();
                dataWriter.WriteEndDocument();
            }

            using (var stream = new MemoryStream())
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    AddEntry(archive, "[Content_Types].xml", new UTF8Encoding(false).GetBytes(
                        "<?xml version=\"1.0\" encoding=\"utf-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"xml\" ContentType=\"application/octet-stream\" /></Types>"));
                    AddEntry(archive, "data_schema.xml", schema.ToArray());
                    AddEntry(archive, "data.xml", data.ToArray());
                }

                return stream.ToArray();
            }
        }

        public static void WriteFile(byte[] package, string outputPath)
        {
            // Stage beside the destination so a failed write preserves an existing export.
            var temporary = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, package);
                if (File.Exists(outputPath))
                {
                    File.Replace(temporary, outputPath, null);
                }
                else
                {
                    File.Move(temporary, outputPath);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private static EntityMetadata RetrieveMetadata(IOrganizationService service, string name,
            EntityFilters filters = EntityFilters.Entity | EntityFilters.Attributes | EntityFilters.Relationships)
        {
            try
            {
                var response = (RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest
                {
                    LogicalName = name,
                    EntityFilters = filters,
                    RetrieveAsIfPublished = false
                });
                return response.EntityMetadata;
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                throw new InvalidOperationException($"Could not read metadata for table '{name}': {exception.Message}", exception);
            }
        }

        private static List<AttributeMetadata> SelectFields(EntityMetadata metadata) =>
            (metadata.Attributes ?? new AttributeMetadata[0])
                .Where(a => a.AttributeOf == null
                    && !ExcludedFields.Contains(a.LogicalName)
                    && SchemaType(a) != null
                    && (a.LogicalName == metadata.PrimaryIdAttribute
                        || (a.IsValidForRead != false && (a.IsValidForCreate == true || a.IsValidForUpdate == true))))
                .OrderBy(a => a.LogicalName, StringComparer.Ordinal)
                .ToList();

        private static string? SchemaType(AttributeMetadata attribute)
        {
            switch (attribute.AttributeType)
            {
                case AttributeTypeCode.String:
                case AttributeTypeCode.Memo: return "string";
                case AttributeTypeCode.Integer:
                case AttributeTypeCode.BigInt: return "number";
                case AttributeTypeCode.Decimal: return "decimal";
                case AttributeTypeCode.Double: return "float";
                case AttributeTypeCode.Money: return "money";
                case AttributeTypeCode.Boolean: return "bool";
                case AttributeTypeCode.DateTime: return "datetime";
                case AttributeTypeCode.Lookup: return "entityreference";
                case AttributeTypeCode.Customer: return "customer";
                case AttributeTypeCode.Owner: return "owner";
                case AttributeTypeCode.Picklist: return "optionsetvalue";
                case AttributeTypeCode.State: return "state";
                case AttributeTypeCode.Status: return "status";
                case AttributeTypeCode.Uniqueidentifier: return "guid";
                default: return null;
            }
        }

        private static void WriteSchema(XmlWriter writer, EntityMetadata metadata, List<AttributeMetadata> fields, List<M2MRelation> relations)
        {
            writer.WriteStartElement("entity");
            writer.WriteAttributeString("name", metadata.LogicalName);
            writer.WriteAttributeString("displayname", metadata.DisplayName?.UserLocalizedLabel?.Label ?? metadata.LogicalName);
            writer.WriteAttributeString("etc", (metadata.ObjectTypeCode ?? 0).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("primaryidfield", metadata.PrimaryIdAttribute);
            writer.WriteAttributeString("primarynamefield", metadata.PrimaryNameAttribute ?? "");
            writer.WriteAttributeString("disableplugins", "false");
            writer.WriteStartElement("fields");
            foreach (var field in fields)
            {
                writer.WriteStartElement("field");
                writer.WriteAttributeString("displayname", field.DisplayName?.UserLocalizedLabel?.Label ?? field.LogicalName);
                writer.WriteAttributeString("name", field.LogicalName);
                writer.WriteAttributeString("type", SchemaType(field));
                writer.WriteAttributeString("customfield", (field.IsCustomAttribute == true ? "true" : "false"));
                if (field.LogicalName == metadata.PrimaryIdAttribute)
                {
                    writer.WriteAttributeString("primaryKey", "true");
                }

                if (field is LookupAttributeMetadata lookup && lookup.Targets?.Length > 0)
                {
                    writer.WriteAttributeString("lookupType", string.Join("|", lookup.Targets));
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteStartElement("relationships");
            foreach (var relation in relations)
            {
                writer.WriteStartElement("relationship");
                writer.WriteAttributeString("name", relation.SchemaName);
                writer.WriteAttributeString("manyToMany", "true");
                writer.WriteAttributeString("isreflexive", "false");
                writer.WriteAttributeString("relatedEntityName", relation.IntersectEntity);
                writer.WriteAttributeString("m2mTargetEntity", relation.TargetEntity);
                writer.WriteAttributeString("m2mTargetEntityPrimaryKey", relation.TargetPrimaryKey);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        private static int WriteData(XmlWriter writer, IOrganizationService service, EntityMetadata metadata,
            List<AttributeMetadata> fields, XDocument fetch, List<M2MRelation> relations, out int linkCount)
        {
            writer.WriteStartElement("entity");
            writer.WriteAttributeString("name", metadata.LogicalName);
            writer.WriteAttributeString("displayname", metadata.DisplayName?.UserLocalizedLabel?.Label ?? metadata.LogicalName);
            writer.WriteStartElement("records");

            // The export pages through all matching rows itself, so ignore any page/count from Advanced Find
            // unless the query is limited with top.
            var root = fetch.Root!;
            var limited = root.Attribute("top") != null;
            root.Attribute("count")?.Remove();
            root.Attribute("page")?.Remove();
            root.Attribute("paging-cookie")?.Remove();
            var seen = new HashSet<Guid>();
            var pageNumber = 1;
            while (true)
            {
                if (!limited)
                {
                    root.SetAttributeValue("count", 5000);
                    root.SetAttributeValue("page", pageNumber);
                }

                var page = service.RetrieveMultiple(new FetchExpression(root.ToString(SaveOptions.DisableFormatting)));
                foreach (var record in page.Entities)
                {
                    // Joined (link-entity) queries can return the same root record more than once.
                    if (seen.Add(record.Id))
                    {
                        WriteRecord(writer, record, fields, metadata.PrimaryIdAttribute);
                    }
                }

                if (limited || !page.MoreRecords)
                {
                    break;
                }

                root.SetAttributeValue("paging-cookie", page.PagingCookie);
                pageNumber++;
            }

            writer.WriteEndElement();
            writer.WriteStartElement("m2mrelationships");
            linkCount = 0;
            foreach (var relation in relations)
            {
                linkCount += WriteLinks(writer, service, relation, seen);
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            return seen.Count;
        }

        private static int WriteLinks(XmlWriter writer, IOrganizationService service, M2MRelation relation, HashSet<Guid> ids)
        {
            var links = new Dictionary<Guid, List<Guid>>();
            var all = ids.ToList();
            for (var offset = 0; offset < all.Count; offset += 500)
            {
                var query = new QueryExpression(relation.IntersectEntity)
                {
                    ColumnSet = new ColumnSet(relation.SourceAttribute, relation.TargetAttribute),
                    PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
                };
                query.Criteria.AddCondition(relation.SourceAttribute, ConditionOperator.In,
                    all.Skip(offset).Take(500).Cast<object>().ToArray());
                while (true)
                {
                    var page = service.RetrieveMultiple(query);
                    foreach (var row in page.Entities)
                    {
                        var source = row.GetAttributeValue<Guid>(relation.SourceAttribute);
                        if (!links.TryGetValue(source, out var targets))
                        {
                            links[source] = targets = new List<Guid>();
                        }

                        targets.Add(row.GetAttributeValue<Guid>(relation.TargetAttribute));
                    }

                    if (!page.MoreRecords)
                    {
                        break;
                    }

                    query.PageInfo.PageNumber++;
                    query.PageInfo.PagingCookie = page.PagingCookie;
                }
            }

            foreach (var link in links.OrderBy(l => l.Key))
            {
                writer.WriteStartElement("m2mrelationship");
                writer.WriteAttributeString("sourceid", link.Key.ToString());
                writer.WriteAttributeString("targetentityname", relation.TargetEntity);
                writer.WriteAttributeString("targetentitynameidfield", relation.TargetPrimaryKey);
                writer.WriteStartElement("targetids");
                foreach (var target in link.Value.Distinct())
                {
                    writer.WriteElementString("targetid", target.ToString());
                }

                writer.WriteEndElement();
                writer.WriteElementString("relationshipname", relation.SchemaName);
                writer.WriteEndElement();
            }

            return links.Values.Sum(v => v.Distinct().Count());
        }

        private static List<M2MRelation> GetRelations(IOrganizationService service, EntityMetadata metadata, IReadOnlyCollection<string>? names)
        {
            var relations = new List<M2MRelation>();
            foreach (var r in (metadata.ManyToManyRelationships ?? new ManyToManyRelationshipMetadata[0]).OrderBy(r => r.SchemaName, StringComparer.Ordinal))
            {
                // Self-referencing relationships need both sides in the same package and are not exported.
                if (r.Entity1LogicalName == r.Entity2LogicalName || names != null && !names.Contains(r.SchemaName))
                {
                    continue;
                }

                var isFirst = r.Entity1LogicalName == metadata.LogicalName;
                var target = isFirst ? r.Entity2LogicalName : r.Entity1LogicalName;
                relations.Add(new M2MRelation
                {
                    SchemaName = r.SchemaName,
                    IntersectEntity = r.IntersectEntityName,
                    SourceAttribute = isFirst ? r.Entity1IntersectAttribute : r.Entity2IntersectAttribute,
                    TargetAttribute = isFirst ? r.Entity2IntersectAttribute : r.Entity1IntersectAttribute,
                    TargetEntity = target,
                    TargetPrimaryKey = RetrieveMetadata(service, target, EntityFilters.Entity).PrimaryIdAttribute
                });
            }

            return relations;
        }

        public static IReadOnlyList<TableInfo> GetTables(IOrganizationService service)
        {
            var response = (RetrieveAllEntitiesResponse)service.Execute(new RetrieveAllEntitiesRequest
            {
                EntityFilters = EntityFilters.Entity,
                RetrieveAsIfPublished = false
            });
            return response.EntityMetadata
                .Where(e => e.IsIntersect != true && e.IsValidForAdvancedFind == true && !string.IsNullOrEmpty(e.PrimaryIdAttribute))
                .Select(e => new TableInfo { LogicalName = e.LogicalName, DisplayName = e.DisplayName?.UserLocalizedLabel?.Label ?? e.LogicalName })
                .OrderBy(t => t.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public static TableDetails GetTableDetails(IOrganizationService service, string table)
        {
            var metadata = RetrieveMetadata(service, table);
            var details = new TableDetails { PrimaryId = metadata.PrimaryIdAttribute };
            details.Columns.AddRange(SelectFields(metadata)
                .Select(f => new ColumnInfo { LogicalName = f.LogicalName, DisplayName = f.DisplayName?.UserLocalizedLabel?.Label ?? f.LogicalName })
                .OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase));
            details.Relationships.AddRange((metadata.ManyToManyRelationships ?? new ManyToManyRelationshipMetadata[0])
                .Where(r => r.Entity1LogicalName != r.Entity2LogicalName)
                .OrderBy(r => r.SchemaName, StringComparer.Ordinal)
                .Select(r => new RelationshipInfo
                {
                    SchemaName = r.SchemaName,
                    TargetEntity = r.Entity1LogicalName == metadata.LogicalName ? r.Entity2LogicalName : r.Entity1LogicalName
                }));
            return details;
        }

        private static readonly HashSet<string> ValuelessOperators = new HashSet<string> { "null", "not-null" };

        public static readonly IReadOnlyList<string> FilterOperators = new[]
        {
            "eq", "ne", "like", "not-like", "begins-with", "ends-with", "gt", "ge", "lt", "le", "null", "not-null"
        };

        /// <summary>Builds FetchXML for the selected table, columns (none = all) and filter conditions.</summary>
        public static string BuildFetchXml(string table, IEnumerable<string> columns, IEnumerable<FilterCondition> conditions, bool matchAny)
        {
            if (string.IsNullOrWhiteSpace(table))
            {
                throw new ArgumentException("Select a table first.");
            }

            var entity = new XElement("entity", new XAttribute("name", table));
            var selected = columns.ToList();
            if (selected.Count == 0)
            {
                entity.Add(new XElement("all-attributes"));
            }

            foreach (var column in selected)
            {
                entity.Add(new XElement("attribute", new XAttribute("name", column)));
            }

            var filter = new XElement("filter", new XAttribute("type", matchAny ? "or" : "and"));
            foreach (var condition in conditions)
            {
                if (string.IsNullOrWhiteSpace(condition.Column) || !FilterOperators.Contains(condition.Operator))
                {
                    throw new ArgumentException("Each filter needs a column and an operator.");
                }

                var element = new XElement("condition", new XAttribute("attribute", condition.Column), new XAttribute("operator", condition.Operator));
                if (!ValuelessOperators.Contains(condition.Operator))
                {
                    if (string.IsNullOrEmpty(condition.Value))
                    {
                        throw new ArgumentException($"Enter a value for the '{condition.Column}' filter.");
                    }

                    element.Add(new XAttribute("value", condition.Value));
                }

                filter.Add(element);
            }

            if (filter.HasElements)
            {
                entity.Add(filter);
            }

            return new XElement("fetch", entity).ToString();
        }

        private sealed class M2MRelation
        {
            public string SchemaName = "";
            public string IntersectEntity = "";
            public string SourceAttribute = "";
            public string TargetAttribute = "";
            public string TargetEntity = "";
            public string TargetPrimaryKey = "";
        }

        private static void WriteRecord(XmlWriter writer, Entity record, List<AttributeMetadata> fields, string primaryId)
        {
            writer.WriteStartElement("record");
            writer.WriteAttributeString("id", record.Id.ToString());
            foreach (var field in fields)
            {
                if (field.LogicalName == primaryId || !record.Attributes.TryGetValue(field.LogicalName, out var value) || value == null)
                {
                    continue;
                }

                writer.WriteStartElement("field");
                writer.WriteAttributeString("name", field.LogicalName);
                if (value is EntityReference reference)
                {
                    writer.WriteAttributeString("value", reference.Id.ToString());
                    writer.WriteAttributeString("lookupentity", reference.LogicalName);
                    writer.WriteAttributeString("lookupentityname", Clean(reference.Name ?? ""));
                }
                else
                {
                    writer.WriteAttributeString("value", Clean(Format(value)));
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        internal static string Format(object value)
        {
            switch (value)
            {
                case OptionSetValue option: return option.Value.ToString(CultureInfo.InvariantCulture);
                case Money money: return money.Value.ToString(CultureInfo.InvariantCulture);
                case bool flag: return flag ? "True" : "False";
                case DateTime date: return date.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                case Guid id: return id.ToString();
                case IFormattable formattable: return formattable.ToString(null, CultureInfo.InvariantCulture);
                default: return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            }
        }

        private static string Clean(string text) => new string(text.Where(XmlConvert.IsXmlChar).ToArray());

        private static void AddEntry(ZipArchive archive, string name, byte[] content)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            {
                stream.Write(content, 0, content.Length);
            }
        }
    }
}
