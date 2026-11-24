using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Messages;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CRMManager.Core
{
    internal sealed class PluginRegistrationService
    {
        private readonly IOrganizationService service;
        private readonly Func<QueryExpression, List<Entity>> retrieveAll;
        private static readonly ColumnSet StepColumns = new ColumnSet("name", "eventhandler", "sdkmessageid",
            "sdkmessagefilterid", "stage", "mode", "rank", "statecode", "filteringattributes", "description",
            "asyncautodelete", "ismanaged", "iscustomizable");
        private static readonly ColumnSet ImageColumns = new ColumnSet("name", "sdkmessageprocessingstepid",
            "entityalias", "imagetype", "messagepropertyname", "attributes", "ismanaged", "iscustomizable");

        internal PluginRegistrationService(IOrganizationService service, Func<QueryExpression, List<Entity>> retrieveAll)
        {
            this.service = service;
            this.retrieveAll = retrieveAll;
        }

        internal IReadOnlyList<PluginTypeInfo> GetTypes(Guid assemblyId)
        {
            var query = new QueryExpression("plugintype") { ColumnSet = new ColumnSet("typename", "isworkflowactivity") };
            query.Criteria.AddCondition("pluginassemblyid", ConditionOperator.Equal, assemblyId);
            query.AddOrder("typename", OrderType.Ascending);
            query.AddOrder("plugintypeid", OrderType.Ascending);
            return retrieveAll(query).Select(entity => new PluginTypeInfo
            {
                Id = entity.Id, Name = entity.GetAttributeValue<string>("typename"),
                IsWorkflowActivity = entity.GetAttributeValue<bool>("isworkflowactivity")
            }).ToArray();
        }

        internal IReadOnlyList<StepRegistration> GetSteps(Guid typeId)
        {
            var query = new QueryExpression("sdkmessageprocessingstep") { ColumnSet = StepColumns };
            query.Criteria.AddCondition("eventhandler", ConditionOperator.Equal, typeId);
            query.AddOrder("rank", OrderType.Ascending);
            query.AddOrder("sdkmessageprocessingstepid", OrderType.Ascending);
            var message = query.AddLink("sdkmessage", "sdkmessageid", "sdkmessageid");
            message.EntityAlias = "message";
            message.Columns = new ColumnSet("name");
            var filter = query.AddLink("sdkmessagefilter", "sdkmessagefilterid", "sdkmessagefilterid", JoinOperator.LeftOuter);
            filter.EntityAlias = "filter";
            filter.Columns = new ColumnSet("primaryobjecttypecode");
            return retrieveAll(query).Select(ReadStep).ToArray();
        }

        internal StepRegistration GetStep(Guid id, bool includeConfiguration = true)
        {
            var columns = includeConfiguration ? new ColumnSet(StepColumns.Columns.Concat(new[] { "configuration" }).ToArray()) : StepColumns;
            var step = ReadStep(service.Retrieve("sdkmessageprocessingstep", id, columns));
            if (string.IsNullOrEmpty(step.MessageName))
                step.MessageName = service.Retrieve("sdkmessage", step.MessageId, new ColumnSet("name")).GetAttributeValue<string>("name");
            return step;
        }

        internal IReadOnlyList<ImageRegistration> GetImages(Guid stepId)
        {
            var query = new QueryExpression("sdkmessageprocessingstepimage") { ColumnSet = ImageColumns };
            query.Criteria.AddCondition("sdkmessageprocessingstepid", ConditionOperator.Equal, stepId);
            query.AddOrder("name", OrderType.Ascending);
            query.AddOrder("sdkmessageprocessingstepimageid", OrderType.Ascending);
            return retrieveAll(query).Select(ReadImage).ToArray();
        }

        internal ImageRegistration GetImage(Guid id) =>
            ReadImage(service.Retrieve("sdkmessageprocessingstepimage", id, ImageColumns));

        internal IReadOnlyList<RegistrationOption> GetMessages()
        {
            var query = new QueryExpression("sdkmessage") { ColumnSet = new ColumnSet("name", "sdkmessageid"), Distinct = true };
            query.Criteria.AddCondition("isprivate", ConditionOperator.Equal, false);
            var filter = query.AddLink("sdkmessagefilter", "sdkmessageid", "sdkmessageid");
            filter.LinkCriteria.AddCondition("iscustomprocessingstepallowed", ConditionOperator.Equal, true);
            query.AddOrder("name", OrderType.Ascending);
            query.AddOrder("sdkmessageid", OrderType.Ascending);
            return retrieveAll(query).Select(entity => new RegistrationOption
            {
                Id = entity.Id, Name = entity.GetAttributeValue<string>("name")
            }).ToArray();
        }

        internal IReadOnlyList<RegistrationOption> GetFilters(Guid messageId)
        {
            var query = new QueryExpression("sdkmessagefilter")
            {
                ColumnSet = new ColumnSet("primaryobjecttypecode", "secondaryobjecttypecode")
            };
            query.Criteria.AddCondition("sdkmessageid", ConditionOperator.Equal, messageId);
            query.Criteria.AddCondition("iscustomprocessingstepallowed", ConditionOperator.Equal, true);
            query.AddOrder("primaryobjecttypecode", OrderType.Ascending);
            query.AddOrder("sdkmessagefilterid", OrderType.Ascending);
            var options = new List<RegistrationOption>
            {
                new RegistrationOption { Id = Guid.Empty, Name = "(none / global - all entities supported by this message)" }
            };
            options.AddRange(retrieveAll(query).Select(entity => new RegistrationOption
            {
                Id = entity.Id,
                Name = (entity.GetAttributeValue<string>("primaryobjecttypecode") ?? "(no primary entity)")
                    + (string.IsNullOrEmpty(entity.GetAttributeValue<string>("secondaryobjecttypecode"))
                        ? "" : " / " + entity.GetAttributeValue<string>("secondaryobjecttypecode"))
            }));
            return options;
        }

        internal Guid SaveStep(StepRegistration step)
        {
            RegistrationRules.ValidateStep(step);
            StepRegistration? existing = null;
            if (step.Id != Guid.Empty)
            {
                existing = GetStep(step.Id, false);
                EnsureEditable(existing.CanEdit);
                if (existing.PluginTypeId != step.PluginTypeId || existing.MessageId != step.MessageId || existing.FilterId != step.FilterId)
                    throw new InvalidOperationException("An existing step cannot be moved to another plugin type, message, or entity here. Register a new step instead.");
            }
            var type = service.Retrieve("plugintype", step.PluginTypeId, new ColumnSet("isworkflowactivity", "ismanaged"));
            if (!type.Contains("ismanaged") || type.GetAttributeValue<bool>("ismanaged") || type.GetAttributeValue<bool>("isworkflowactivity"))
                throw new InvalidOperationException("Step registration requires an unmanaged IPlugin type, not a workflow activity.");
            var message = service.Retrieve("sdkmessage", step.MessageId, new ColumnSet("name", "isprivate"));
            step.MessageName = message.GetAttributeValue<string>("name");
            if (message.GetAttributeValue<bool>("isprivate"))
                throw new InvalidOperationException("Private SDK messages cannot be registered here.");
            RegistrationRules.ValidateStep(step);
            if (step.FilterId.HasValue)
            {
                var filter = service.Retrieve("sdkmessagefilter", step.FilterId.Value, new ColumnSet("sdkmessageid", "iscustomprocessingstepallowed"));
                if (filter.GetAttributeValue<EntityReference>("sdkmessageid")?.Id != step.MessageId
                    || !filter.GetAttributeValue<bool>("iscustomprocessingstepallowed"))
                    throw new InvalidOperationException("Select an allowed entity filter belonging to the selected message.");
            }
            if (existing != null && existing.Stage != step.Stage)
                foreach (var image in GetImages(step.Id)) RegistrationRules.ValidateImage(image, step);

            var entity = new Entity("sdkmessageprocessingstep", step.Id)
            {
                ["name"] = step.Name.Trim(), ["stage"] = new OptionSetValue(step.Stage),
                ["mode"] = new OptionSetValue(step.Mode), ["rank"] = step.Rank,
                ["filteringattributes"] = EmptyToNull(RegistrationRules.NormalizeAttributes(step.FilteringAttributes)),
                ["description"] = EmptyToNull(step.Description),
                ["configuration"] = EmptyToNull(step.UnsecureConfiguration),
                ["asyncautodelete"] = step.AsyncAutoDelete
            };
            if (step.Id == Guid.Empty)
            {
                entity["eventhandler"] = new EntityReference("plugintype", step.PluginTypeId);
                entity["sdkmessageid"] = new EntityReference("sdkmessage", step.MessageId);
                if (step.FilterId.HasValue) entity["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter", step.FilterId.Value);
                entity["supporteddeployment"] = new OptionSetValue(0);
                return service.Create(entity);
            }
            service.Update(entity);
            return step.Id;
        }

        internal Guid SaveImage(ImageRegistration image)
        {
            if (image.StepId == Guid.Empty) throw new ArgumentException("Select the parent plugin step before saving an image.");
            var step = GetStep(image.StepId, false);
            EnsureEditable(step.CanEdit);
            RegistrationRules.ValidateImage(image, step);
            if (image.Id != Guid.Empty)
            {
                var existing = GetImage(image.Id);
                EnsureEditable(existing.CanEdit);
                if (existing.StepId != image.StepId)
                    throw new InvalidOperationException("An existing image cannot be moved to another step.");
            }
            var entity = new Entity("sdkmessageprocessingstepimage", image.Id)
            {
                ["name"] = image.Name.Trim(), ["entityalias"] = image.Alias.Trim(),
                ["imagetype"] = new OptionSetValue(image.ImageType),
                ["messagepropertyname"] = image.MessagePropertyName.Trim(),
                ["attributes"] = image.AllAttributes ? null : RegistrationRules.NormalizeAttributes(image.Attributes)
            };
            if (image.Id == Guid.Empty)
            {
                entity["sdkmessageprocessingstepid"] = new EntityReference("sdkmessageprocessingstep", image.StepId);
                return service.Create(entity);
            }
            service.Update(entity);
            return image.Id;
        }

        internal void SetStepEnabled(Guid id, bool enabled)
        {
            EnsureEditable(GetStep(id, false).CanEdit);
            service.Execute(new SetStateRequest
            {
                EntityMoniker = new EntityReference("sdkmessageprocessingstep", id),
                State = new OptionSetValue(enabled ? 0 : 1), Status = new OptionSetValue(enabled ? 1 : 2)
            });
        }

        internal void UnregisterStep(StepRegistration selected)
        {
            if (selected == null || selected.Id == Guid.Empty || selected.PluginTypeId == Guid.Empty)
                throw new ArgumentException("Select an existing plugin step to unregister.");
            var current = GetStep(selected.Id, false);
            EnsureEditable(current.CanEdit);
            if (current.PluginTypeId != selected.PluginTypeId || current.MessageId != selected.MessageId
                || current.FilterId != selected.FilterId)
                throw new InvalidOperationException("The step registration changed. Refresh and review it before unregistering.");
            service.Delete("sdkmessageprocessingstep", current.Id);
        }

        internal void UnregisterPluginType(PluginTypeInfo selected, Guid assemblyId)
        {
            if (selected == null || selected.Id == Guid.Empty || assemblyId == Guid.Empty || selected.IsWorkflowActivity)
                throw new ArgumentException("Select an existing IPlugin type, not a workflow activity.");
            var type = service.Retrieve("plugintype", selected.Id,
                new ColumnSet("typename", "pluginassemblyid", "ismanaged", "isworkflowactivity"));
            if (!type.Contains("ismanaged") || type.GetAttributeValue<bool>("ismanaged")
                || !type.Contains("isworkflowactivity") || type.GetAttributeValue<bool>("isworkflowactivity"))
                throw new InvalidOperationException("Only unmanaged IPlugin types can be unregistered.");
            if (type.GetAttributeValue<EntityReference>("pluginassemblyid")?.Id != assemblyId
                || type.GetAttributeValue<string>("typename") != selected.Name)
                throw new InvalidOperationException("The plugin type registration changed. Refresh and review it before unregistering.");
            var assembly = service.Retrieve("pluginassembly", assemblyId, new ColumnSet("ismanaged"));
            if (!assembly.Contains("ismanaged") || assembly.GetAttributeValue<bool>("ismanaged"))
                throw new InvalidOperationException("Plugin types in managed assemblies cannot be unregistered.");
            var steps = GetSteps(selected.Id);
            foreach (var step in steps) EnsureEditable(step.CanEdit);
            var requests = new OrganizationRequestCollection();
            foreach (var step in steps)
                requests.Add(new DeleteRequest { Target = new EntityReference("sdkmessageprocessingstep", step.Id) });
            requests.Add(new DeleteRequest { Target = new EntityReference("plugintype", selected.Id) });
            service.Execute(new ExecuteTransactionRequest { Requests = requests, ReturnResponses = false });
        }

        private static StepRegistration ReadStep(Entity entity)
        {
            var handler = entity.GetAttributeValue<EntityReference>("eventhandler");
            if (handler == null || handler.LogicalName != "plugintype")
                throw new InvalidOperationException("This registration is not a plugin-type step.");
            return new StepRegistration
            {
                Id = entity.Id, Name = entity.GetAttributeValue<string>("name"), PluginTypeId = handler.Id,
                MessageId = entity.GetAttributeValue<EntityReference>("sdkmessageid").Id,
                MessageName = entity.GetAttributeValue<AliasedValue>("message.name")?.Value as string
                    ?? entity.GetAttributeValue<EntityReference>("sdkmessageid").Name ?? "",
                PrimaryEntity = entity.GetAttributeValue<AliasedValue>("filter.primaryobjecttypecode")?.Value as string ?? "",
                FilterId = entity.GetAttributeValue<EntityReference>("sdkmessagefilterid")?.Id,
                Stage = entity.GetAttributeValue<OptionSetValue>("stage").Value,
                Mode = entity.GetAttributeValue<OptionSetValue>("mode").Value, Rank = entity.GetAttributeValue<int>("rank"),
                Enabled = entity.GetAttributeValue<OptionSetValue>("statecode").Value == 0,
                FilteringAttributes = entity.GetAttributeValue<string>("filteringattributes") ?? "",
                Description = entity.GetAttributeValue<string>("description") ?? "",
                UnsecureConfiguration = entity.GetAttributeValue<string>("configuration") ?? "",
                AsyncAutoDelete = entity.GetAttributeValue<bool>("asyncautodelete"), CanEdit = IsEditable(entity)
            };
        }

        private static ImageRegistration ReadImage(Entity entity) => new ImageRegistration
        {
            Id = entity.Id, StepId = entity.GetAttributeValue<EntityReference>("sdkmessageprocessingstepid").Id,
            Name = entity.GetAttributeValue<string>("name"), Alias = entity.GetAttributeValue<string>("entityalias"),
            ImageType = entity.GetAttributeValue<OptionSetValue>("imagetype").Value,
            MessagePropertyName = entity.GetAttributeValue<string>("messagepropertyname"),
            Attributes = entity.GetAttributeValue<string>("attributes") ?? "",
            AllAttributes = string.IsNullOrWhiteSpace(entity.GetAttributeValue<string>("attributes")),
            CanEdit = IsEditable(entity)
        };

        private static bool IsEditable(Entity entity) => entity.Contains("ismanaged")
            && !entity.GetAttributeValue<bool>("ismanaged")
            && entity.GetAttributeValue<BooleanManagedProperty>("iscustomizable")?.Value == true;

        private static void EnsureEditable(bool editable)
        {
            if (!editable) throw new InvalidOperationException("Only unmanaged, customizable registrations can be changed.");
        }

        private static string? EmptyToNull(string value) => string.IsNullOrEmpty(value) ? null : value;
    }
}
