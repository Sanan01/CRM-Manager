using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Tooling.Connector;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace CRMManager.Core
{
    public sealed class CrmSession : IDisposable
    {
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private CrmServiceClient? connection;
        private OrganizationServiceProxy? proxy;
        private IOrganizationService? attachedService;
        private IDisposable? ownedAttachedService;
        private bool disposed;

        public string OrganizationName { get; private set; } = "";
        public string ServerVersion { get; private set; } = "";
        public string Endpoint { get; private set; } = "";
        public bool IsConnected => proxy != null || attachedService != null;

        public static CrmSession Attach(IOrganizationService service, string address, string organizationName, string serverVersion)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            var endpoint = DeploymentRules.GetServiceUri(address);
            if (!System.Version.TryParse(serverVersion, out var parsed) || parsed.Major != 9 || parsed.Minor != 1)
                throw new NotSupportedException("CRM Manager requires a CRM 9.1 server version from the connected host. Reported version: " + serverVersion);
            return new CrmSession
            {
                attachedService = service,
                OrganizationName = string.IsNullOrWhiteSpace(organizationName)
                    ? DeploymentRules.GetOrganizationUniqueName(endpoint) : organizationName,
                Endpoint = endpoint.AbsoluteUri,
                ServerVersion = serverVersion
            };
        }

        public static CrmSession AttachOwned(IOrganizationService service, string address, string organizationName, string serverVersion)
        {
            if (!(service is IDisposable owned))
                throw new ArgumentException("An owned organization service must implement IDisposable.", nameof(service));
            try
            {
                var session = Attach(service, address, organizationName, serverVersion);
                session.ownedAttachedService = owned;
                return session;
            }
            catch
            {
                owned.Dispose();
                throw;
            }
        }

        public static async Task<CrmSession> AttachAsync(IOrganizationService service, string address, string organizationName)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            var endpoint = DeploymentRules.GetServiceUri(address);
            return await Task.Run(() =>
            {
                service.Execute(new WhoAmIRequest());
                var version = ((RetrieveVersionResponse)service.Execute(new RetrieveVersionRequest())).Version;
                return Attach(service, endpoint.AbsoluteUri, organizationName, version);
            }).ConfigureAwait(false);
        }

        public async Task ConnectAsync(string address, string domain, string username, SecureString? password,
            IProgress<string>? progress = null)
        {
            var endpoint = DeploymentRules.GetServiceUri(address);
            var uniqueName = DeploymentRules.GetOrganizationUniqueName(endpoint);
            var server = new Uri(endpoint.GetLeftPart(UriPartial.Authority));
            var windowsCredential = WindowsAuthentication.CreateCredential(false, domain, username, password);
            await RunAsync(() =>
            {
                ConnectCore(server, windowsCredential, uniqueName, null, progress);
                return true;
            });
        }

        public Task<CrmDiscoveryResult> DiscoverAsync(string server, string port, bool useSsl, bool developmentConfirmed,
            bool useCurrentWindowsAccount = true, string domain = "", string username = "", SecureString? password = null)
        {
            EnsureDevelopment(developmentConfirmed);
            var serverUri = DeploymentRules.GetServerUri(server, port, useSsl);
            var credential = WindowsAuthentication.CreateCredential(useCurrentWindowsAccount, domain, username, password);
            return RunAsync(() =>
            {
                EnsureDisconnected();
                var discoveryUri = new Uri(serverUri, "/XRMServices/2011/Discovery.svc");
                CrmServiceClient.MaxConnectionTimeout = TimeSpan.FromMinutes(10);
                var organizations = CrmServiceClient.DiscoverOrganizations(discoveryUri, null, credential);
                if (organizations == null)
                {
                    throw new InvalidOperationException("XRM Tooling discovery did not return a result. Verify the server, SSL/port, and Windows credentials.");
                }

                return new CrmDiscoveryResult(serverUri, credential, organizations);
            });
        }

        public Task ConnectAsync(CrmDiscoveryResult discovery, CrmOrganization organization, bool developmentConfirmed,
            IProgress<string>? progress = null)
        {
            EnsureDevelopment(developmentConfirmed);
            if (!discovery.Organizations.Contains(organization))
            {
                throw new ArgumentException("Select an organization from the current discovery result. Organizations are never chosen automatically.");
            }

            return RunAsync(() =>
            {
                ConnectCore(discovery.Server, discovery.Credential, organization.UniqueName, organization.Detail, progress);
                return true;
            });
        }

        private void ConnectCore(Uri server, NetworkCredential credential, string uniqueName, OrganizationDetail? detail,
            IProgress<string>? progress)
        {
            EnsureDisconnected();
            CrmServiceClient? candidate = null;
            var stage = "XRM Tooling Active Directory authentication";
            try
            {
                progress?.Report(stage);
                CrmServiceClient.MaxConnectionTimeout = TimeSpan.FromMinutes(10);
                candidate = new CrmServiceClient(credential, Microsoft.Xrm.Tooling.Connector.AuthenticationType.AD, server.Host,
                    server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), uniqueName,
                    useUniqueInstance: true, useSsl: server.Scheme == Uri.UriSchemeHttps, orgDetail: detail);
                if (!candidate.IsReady)
                {
                    throw new InvalidOperationException("XRM Tooling could not establish the connection. " + candidate.LastCrmError,
                        candidate.LastCrmException);
                }

                var activeProxy = candidate.OrganizationServiceProxy
                    ?? throw new InvalidOperationException("XRM Tooling did not provide an Active Directory organization service proxy.");
                activeProxy.Timeout = TimeSpan.FromMinutes(10);
                stage = "Verifying organization access (WhoAmI)";
                progress?.Report(stage);
                var identity = (WhoAmIResponse)activeProxy.Execute(new WhoAmIRequest());
                if ((detail != null && identity.OrganizationId != detail.OrganizationId)
                    || !string.Equals(candidate.ConnectedOrgUniqueName, uniqueName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("The connected organization does not match the requested organization. The connection was rejected.");
                }

                stage = "Checking CRM server version";
                progress?.Report(stage);
                var version = ((RetrieveVersionResponse)activeProxy.Execute(new RetrieveVersionRequest())).Version;
                if (!System.Version.TryParse(version, out var parsed) || parsed.Major != 9 || parsed.Minor != 1)
                {
                    throw new NotSupportedException("This application targets on-premises CRM 9.1. The server reported: " + version);
                }

                var actualUri = candidate.CrmConnectOrgUriActual
                    ?? throw new InvalidOperationException("XRM Tooling did not report the connected organization URL.");
                var organizationName = string.IsNullOrWhiteSpace(candidate.ConnectedOrgFriendlyName)
                    ? candidate.ConnectedOrgUniqueName : candidate.ConnectedOrgFriendlyName;
                if (string.IsNullOrWhiteSpace(organizationName))
                {
                    throw new InvalidOperationException("XRM Tooling did not report the connected organization name.");
                }

                var actualEndpoint = DeploymentRules.GetServiceUri(actualUri.AbsoluteUri).AbsoluteUri;
                OrganizationName = organizationName;
                ServerVersion = version;
                Endpoint = actualEndpoint;
                connection = candidate;
                proxy = activeProxy;
            }
            catch (Exception exception)
            {
                candidate?.Dispose();
                throw new InvalidOperationException(stage + " failed: " + exception.Message, exception);
            }
        }

        public Task DisconnectAsync() => RunAsync(() =>
        {
            connection?.Dispose();
            ownedAttachedService?.Dispose();
            ownedAttachedService = null;
            connection = null;
            proxy = null;
            attachedService = null;
            OrganizationName = "";
            ServerVersion = "";
            Endpoint = "";
            return true;
        });

        public Task<IReadOnlyList<CrmItem>> GetAssembliesAsync() => RunAsync<IReadOnlyList<CrmItem>>(() =>
        {
            var query = new QueryExpression("pluginassembly")
            {
                ColumnSet = new ColumnSet("name", "version", "culture", "publickeytoken", "isolationmode", "sourcetype", "ismanaged")
            };
            query.Criteria.AddCondition("ismanaged", ConditionOperator.Equal, false);
            query.Criteria.AddCondition("sourcetype", ConditionOperator.Equal, 0);
            query.AddOrder("name", OrderType.Ascending);
            return RetrieveAll(query).Select(entity => new CrmItem
            {
                Id = entity.Id,
                Name = entity.GetAttributeValue<string>("name"),
                Version = entity.GetAttributeValue<string>("version"),
                Culture = entity.GetAttributeValue<string>("culture") ?? "neutral",
                PublicKeyToken = entity.GetAttributeValue<string>("publickeytoken"),
                Details = entity.GetAttributeValue<OptionSetValue>("isolationmode")?.Value == 2 ? "Sandbox" : "None"
            }).ToArray();
        });

        public Task<bool> UpdateAssemblyAsync(CrmItem assembly, string file) => RunAsync(() =>
        {
            var localName = AssemblyName.GetAssemblyName(file);
            var current = Service.Retrieve("pluginassembly", assembly.Id,
                new ColumnSet("name", "version", "culture", "publickeytoken", "content", "ismanaged", "sourcetype"));
            EnsureUnmanaged(current);
            if (current.GetAttributeValue<OptionSetValue>("sourcetype")?.Value != 0)
            {
                throw new InvalidOperationException("Only database-stored assemblies can be updated.");
            }

            var registeredName = new AssemblyName(
                $"{current.GetAttributeValue<string>("name")}, Version={current.GetAttributeValue<string>("version")}, Culture={current.GetAttributeValue<string>("culture") ?? "neutral"}, PublicKeyToken={current.GetAttributeValue<string>("publickeytoken")}");
            DeploymentRules.ValidateAssemblyUpdate(localName, registeredName);
            var content = File.ReadAllBytes(file);
            if (DeploymentRules.ContentMatches(content, current.GetAttributeValue<string>("content")))
            {
                return false;
            }

            var update = new Entity("pluginassembly", assembly.Id);
            update["content"] = Convert.ToBase64String(content);
            Service.Update(update);
            return true;
        });

        public Task RegisterAssemblyAsync(string file, string typeNames, bool sandbox) => RunAsync(() =>
        {
            var name = AssemblyName.GetAssemblyName(file);
            var token = DeploymentRules.GetPublicKeyToken(name);
            var types = DeploymentRules.ParseTypeNames(typeNames);
            var assemblyId = Guid.NewGuid();
            var assembly = new Entity("pluginassembly", assemblyId)
            {
                ["name"] = name.Name,
                ["version"] = name.Version.ToString(),
                ["culture"] = string.IsNullOrEmpty(name.CultureName) ? "neutral" : name.CultureName,
                ["publickeytoken"] = token,
                ["sourcetype"] = new OptionSetValue(0),
                ["isolationmode"] = new OptionSetValue(sandbox ? 2 : 1),
                ["content"] = Convert.ToBase64String(File.ReadAllBytes(file))
            };
            var requests = new OrganizationRequestCollection { new CreateRequest { Target = assembly } };
            foreach (var type in types)
            {
                requests.Add(new CreateRequest
                {
                    Target = new Entity("plugintype")
                    {
                        ["name"] = type,
                        ["typename"] = type,
                        ["friendlyname"] = type,
                        ["pluginassemblyid"] = new EntityReference("pluginassembly", assemblyId)
                    }
                });
            }

            Service.Execute(new ExecuteTransactionRequest { Requests = requests, ReturnResponses = false });
            return true;
        });

        public Task<IReadOnlyList<CrmItem>> GetWebResourcesAsync() => RunAsync<IReadOnlyList<CrmItem>>(() =>
        {
            var query = new QueryExpression("webresource")
            {
                ColumnSet = new ColumnSet("name", "displayname", "webresourcetype", "ismanaged", "iscustomizable")
            };
            query.Criteria.AddCondition("ismanaged", ConditionOperator.Equal, false);
            query.AddOrder("name", OrderType.Ascending);
            return RetrieveAll(query).Where(IsCustomizable).Select(entity => new CrmItem
            {
                Id = entity.Id,
                Name = entity.GetAttributeValue<string>("name"),
                DisplayName = entity.GetAttributeValue<string>("displayname"),
                Type = entity.GetAttributeValue<OptionSetValue>("webresourcetype").Value,
                Details = entity.FormattedValues.Contains("webresourcetype") ? entity.FormattedValues["webresourcetype"] : ""
            }).ToArray();
        });

        public Task<bool> UpdateWebResourceAsync(CrmItem resource, string file) => RunAsync(() =>
        {
            var current = RetrieveEditableWebResource(resource.Id);
            DeploymentRules.ValidateWebResourceFile(file, current.GetAttributeValue<OptionSetValue>("webresourcetype").Value);
            var content = File.ReadAllBytes(file);
            if (DeploymentRules.ContentMatches(content, current.GetAttributeValue<string>("content")))
            {
                return false;
            }

            Service.Update(new Entity("webresource", resource.Id) { ["content"] = Convert.ToBase64String(content) });
            return true;
        });

        public Task<WebResourceDocument> GetWebResourceTextAsync(Guid id) => RunAsync(() =>
        {
            var resource = RetrieveEditableWebResource(id);
            return WebResourceDocument.Open(id, resource.GetAttributeValue<string>("name"),
                resource.GetAttributeValue<OptionSetValue>("webresourcetype").Value, resource.GetAttributeValue<string>("content"));
        });

        public Task<bool> SaveWebResourceTextAsync(WebResourceDocument document, string text) => RunAsync(() =>
        {
            var resource = RetrieveEditableWebResource(document.Id);
            if (resource.GetAttributeValue<OptionSetValue>("webresourcetype").Value != document.ResourceType)
                throw new InvalidOperationException("The resource type changed since opening this editor. Close and reopen it.");
            var bytes = document.PrepareUpdate(text, resource.GetAttributeValue<string>("content"));
            if (bytes == null) return false;
            Service.Update(new Entity("webresource", document.Id) { ["content"] = Convert.ToBase64String(bytes) });
            return true;
        });

        private Entity RetrieveEditableWebResource(Guid id)
        {
            // Compare and edit the unpublished layer, never the older published content.
            var resource = ((RetrieveUnpublishedResponse)Service.Execute(new RetrieveUnpublishedRequest
            {
                Target = new EntityReference("webresource", id),
                ColumnSet = new ColumnSet("name", "content", "webresourcetype", "ismanaged", "iscustomizable")
            })).Entity;
            EnsureUnmanaged(resource);
            if (!IsCustomizable(resource)) throw new InvalidOperationException("This web resource is not customizable.");
            return resource;
        }

        public Task PublishWebResourceAsync(Guid id) => RunAsync(() =>
        {
            Service.Execute(new PublishXmlRequest
            {
                ParameterXml = $"<importexportxml><webresources><webresource>{id:D}</webresource></webresources></importexportxml>"
            });
            return true;
        });

        public Task<IReadOnlyList<CrmItem>> GetSolutionsAsync() => RunAsync<IReadOnlyList<CrmItem>>(() =>
        {
            var query = new QueryExpression("solution")
            {
                ColumnSet = new ColumnSet("uniquename", "friendlyname", "version")
            };
            query.Criteria.AddCondition("ismanaged", ConditionOperator.Equal, false);
            query.Criteria.AddCondition("isvisible", ConditionOperator.Equal, true);
            query.Criteria.AddCondition("uniquename", ConditionOperator.NotIn, "Active", "Default");
            query.AddOrder("friendlyname", OrderType.Ascending);
            return RetrieveAll(query).Select(entity => new CrmItem
            {
                Id = entity.Id,
                Name = entity.GetAttributeValue<string>("uniquename"),
                DisplayName = entity.GetAttributeValue<string>("friendlyname"),
                Version = entity.GetAttributeValue<string>("version")
            }).ToArray();
        });

        public Task<bool> UpdateSolutionVersionAsync(CrmItem solution, string version) =>
            RunAsync(() => UpdateSolutionVersion(Service, solution, version));

        internal static bool UpdateSolutionVersion(IOrganizationService service, CrmItem solution, string version)
        {
            var normalized = DeploymentRules.NormalizeSolutionVersion(version);
            var current = service.Retrieve("solution", solution.Id, new ColumnSet("uniquename", "version", "ismanaged"));
            EnsureUnmanaged(current);
            var name = current.GetAttributeValue<string>("uniquename");
            if (!string.Equals(name, solution.Name, StringComparison.Ordinal)
                || name == "Active" || name == "Default")
                throw new InvalidOperationException("Select an unmanaged custom solution from a refreshed solution list.");
            var currentVersion = current.GetAttributeValue<string>("version");
            if (!string.Equals(currentVersion, solution.Version, StringComparison.Ordinal))
                throw new InvalidOperationException("The solution version changed since the list was loaded. Refresh solutions before updating or exporting.");
            if (currentVersion == normalized) return false;
            var update = new Entity("solution", solution.Id);
            update["version"] = normalized;
            service.Update(update);
            return true;
        }

        public Task ExportSolutionAsync(CrmItem solution, bool managed, string outputPath) => RunAsync(() =>
        {
            var response = (ExportSolutionResponse)Service.Execute(new ExportSolutionRequest
            {
                SolutionName = solution.Name,
                Managed = managed
            });
            var bytes = response.ExportSolutionFile;
            if (bytes == null || bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4b)
            {
                throw new InvalidDataException("CRM did not return a valid solution ZIP.");
            }

            // Stage beside the destination so a failed write preserves an existing export.
            var temporary = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
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

            return true;
        });

        public Task<IReadOnlyList<PluginTypeInfo>> GetPluginTypesAsync(Guid assemblyId) => RunAsync(() => Registrations.GetTypes(assemblyId));
        public Task<IReadOnlyList<StepRegistration>> GetStepsAsync(Guid typeId) => RunAsync(() => Registrations.GetSteps(typeId));
        public Task<StepRegistration> GetStepAsync(Guid id, bool includeConfiguration = true) => RunAsync(() => Registrations.GetStep(id, includeConfiguration));
        public Task<IReadOnlyList<ImageRegistration>> GetImagesAsync(Guid stepId) => RunAsync(() => Registrations.GetImages(stepId));
        public Task<ImageRegistration> GetImageAsync(Guid id) => RunAsync(() => Registrations.GetImage(id));
        public Task<IReadOnlyList<RegistrationOption>> GetRegistrationMessagesAsync() => RunAsync(() => Registrations.GetMessages());
        public Task<IReadOnlyList<RegistrationOption>> GetMessageFiltersAsync(Guid messageId) => RunAsync(() => Registrations.GetFilters(messageId));
        public Task<Guid> SaveStepAsync(StepRegistration step) => RunAsync(() => Registrations.SaveStep(step));
        public Task<Guid> SaveImageAsync(ImageRegistration image) => RunAsync(() => Registrations.SaveImage(image));
        public Task SetStepEnabledAsync(Guid id, bool enabled) => RunAsync(() => { Registrations.SetStepEnabled(id, enabled); return true; });
        public Task UnregisterStepAsync(StepRegistration step) => RunAsync(() => { Registrations.UnregisterStep(step); return true; });
        public Task UnregisterPluginTypeAsync(PluginTypeInfo type, Guid assemblyId) =>
            RunAsync(() => { Registrations.UnregisterPluginType(type, assemblyId); return true; });

        private PluginRegistrationService Registrations => new PluginRegistrationService(Service, RetrieveAll);
        private IOrganizationService Service => attachedService ?? (IOrganizationService?)proxy
            ?? throw new InvalidOperationException("Connect to your development CRM organization first.");

        private static void EnsureDevelopment(bool confirmed)
        {
            if (!confirmed)
            {
                throw new InvalidOperationException("Confirm that the target is a development organization before discovering or connecting.");
            }
        }

        private void EnsureDisconnected()
        {
            if (IsConnected)
            {
                throw new InvalidOperationException("Disconnect before changing environments.");
            }
        }

        private List<Entity> RetrieveAll(QueryExpression query)
        {
            query.PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 };
            var records = new List<Entity>();
            while (true)
            {
                var page = Service.RetrieveMultiple(query);
                records.AddRange(page.Entities);
                if (!page.MoreRecords)
                {
                    return records;
                }

                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
        }

        private static bool IsCustomizable(Entity entity) =>
            entity.GetAttributeValue<BooleanManagedProperty>("iscustomizable")?.Value == true;

        private static void EnsureUnmanaged(Entity entity)
        {
            if (!entity.Contains("ismanaged") || entity.GetAttributeValue<bool>("ismanaged"))
            {
                throw new InvalidOperationException("Only unmanaged development components can be changed.");
            }
        }

        private async Task<T> RunAsync<T>(Func<T> operation)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(CrmSession));
                }

                return await Task.Run(operation).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        public void Dispose()
        {
            gate.Wait();
            try
            {
                if (!disposed)
                {
                    connection?.Dispose();
                    ownedAttachedService?.Dispose();
                    ownedAttachedService = null;
                    connection = null;
                    proxy = null;
                    attachedService = null;
                    disposed = true;
                }
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
