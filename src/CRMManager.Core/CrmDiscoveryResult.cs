using Microsoft.Xrm.Sdk.Discovery;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

namespace CRMManager.Core
{
    public sealed class CrmOrganization
    {
        internal CrmOrganization(OrganizationDetail detail)
        {
            if (detail.OrganizationId == Guid.Empty || string.IsNullOrWhiteSpace(detail.UniqueName)
                || !detail.Endpoints.Contains(EndpointType.OrganizationService))
            {
                throw new InvalidDataException("Discovery returned an organization without its ID, unique name, or organization service endpoint.");
            }

            Detail = detail;
            ServiceUri = DeploymentRules.GetServiceUri(detail.Endpoints[EndpointType.OrganizationService]);
        }

        internal OrganizationDetail Detail { get; }
        public Guid Id => Detail.OrganizationId;
        public string UniqueName => Detail.UniqueName;
        public string DisplayName => $"{Detail.FriendlyName} ({Detail.UniqueName})";
        public Uri ServiceUri { get; }
    }

    public sealed class CrmDiscoveryResult
    {
        internal CrmDiscoveryResult(Uri server, NetworkCredential credential, IEnumerable<OrganizationDetail> organizations)
        {
            Server = server;
            Credential = credential;
            Organizations = Array.AsReadOnly(organizations.Select(detail => new CrmOrganization(detail))
                .OrderBy(organization => organization.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray());
            if (Organizations.Count == 0)
            {
                throw new InvalidOperationException("Discovery returned no organizations accessible to this Windows account. Verify the discovery server and account access.");
            }
        }

        internal Uri Server { get; }
        internal NetworkCredential Credential { get; }
        public IReadOnlyList<CrmOrganization> Organizations { get; }
    }
}
