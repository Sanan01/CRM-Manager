using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace CRMManager.Core
{
    public static class DeploymentRules
    {
        private const string ServicePath = "/XRMServices/2011/Organization.svc";

        public static string NormalizeSolutionVersion(string version)
        {
            var parts = version.Trim().Split('.');
            if (parts.Length != 4 || parts.Any(part => !int.TryParse(part, NumberStyles.None,
                CultureInfo.InvariantCulture, out var number) || number < 0))
                throw new ArgumentException("Enter a solution version with four non-negative whole numbers, such as 1.0.0.50.");
            return string.Join(".", parts.Select(part => int.Parse(part, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)));
        }

        public static Uri GetServerUri(string server, string port, bool useSsl)
        {
            var scheme = useSsl ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
            var address = server.Trim();
            if (!address.Contains("://"))
            {
                address = scheme + "://" + address;
            }

            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
                || uri.Scheme != scheme || string.IsNullOrEmpty(uri.Host) || uri.Port < 1
                || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/")
            {
                throw new ArgumentException("Enter the CRM server hostname only (no organization path), and select Use SSL to match the server protocol. Use Organization URL mode for a full organization URL.");
            }

            var builder = new UriBuilder(uri);
            if (!string.IsNullOrWhiteSpace(port))
            {
                if (!int.TryParse(port.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                    || parsed < 1 || parsed > 65535)
                {
                    throw new ArgumentException("Port must be a number from 1 to 65535, or blank for the server default.");
                }

                if (!uri.IsDefaultPort && uri.Port != parsed)
                {
                    throw new ArgumentException("The port in Server conflicts with the Port field. Specify the same port or remove it from Server.");
                }

                builder.Port = parsed;
            }

            return builder.Uri;
        }

        public static string GetOrganizationUniqueName(Uri serviceUri)
        {
            var path = GetServiceUri(serviceUri.AbsoluteUri).AbsolutePath;
            var name = Uri.UnescapeDataString(path.Substring(0, path.Length - ServicePath.Length).Trim('/'));
            if (string.IsNullOrWhiteSpace(name) || name.Contains("/") || name.Contains("\\"))
            {
                throw new ArgumentException("Organization URL mode requires an organization path, for example https://crm-server/DevelopmentOrg. For a server hostname only, use server discovery and select an organization.");
            }

            return name;
        }

        public static Uri GetServiceUri(string address)
        {
            if (!Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                || !string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new ArgumentException("Enter an HTTP(S) CRM organization URL without credentials, query parameters, or a fragment.");
            }

            var path = uri.AbsolutePath.TrimEnd('/');
            if (!path.EndsWith(ServicePath, StringComparison.OrdinalIgnoreCase))
            {
                path += ServicePath;
            }

            return new UriBuilder(uri) { Path = path }.Uri;
        }

        public static void ValidateAssemblyUpdate(AssemblyName local, AssemblyName registered)
        {
            if (!string.Equals(local.Name, registered.Name, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(NormalizeCulture(local.CultureName), NormalizeCulture(registered.CultureName), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(GetPublicKeyToken(local), GetPublicKeyToken(registered), StringComparison.OrdinalIgnoreCase)
                || local.Version == null || registered.Version == null
                || local.Version.Major != registered.Version.Major
                || local.Version.Minor != registered.Version.Minor)
            {
                throw new InvalidOperationException("The DLL must match the registered assembly name, culture, public key token, and major/minor version. A different identity requires an explicit upgrade, which this version does not support.");
            }
        }

        public static string GetPublicKeyToken(AssemblyName assembly)
        {
            var token = assembly.GetPublicKeyToken();
            if (token == null || token.Length == 0)
            {
                throw new InvalidOperationException("CRM plugin assemblies must be strong-name signed.");
            }

            return BitConverter.ToString(token).Replace("-", "").ToLowerInvariant();
        }

        public static string[] ParseTypeNames(string input)
        {
            var names = input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(name => name.Trim()).Where(name => name.Length > 0).ToArray();
            if (names.Length == 0 || names.Any(name => name.Any(char.IsWhiteSpace))
                || names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            {
                throw new ArgumentException("Enter unique, fully qualified plugin type names, one per line, without whitespace inside names.");
            }

            return names;
        }

        public static void ValidateWebResourceFile(string path, int resourceType)
        {
            var extensions = new Dictionary<int, string[]>
            {
                [1] = new[] { ".html", ".htm" },
                [2] = new[] { ".css" },
                [3] = new[] { ".js" },
                [4] = new[] { ".xml" },
                [5] = new[] { ".png" },
                [6] = new[] { ".jpg", ".jpeg" },
                [7] = new[] { ".gif" },
                [8] = new[] { ".xap" },
                [9] = new[] { ".xsl", ".xslt" },
                [10] = new[] { ".ico" },
                [11] = new[] { ".svg" },
                [12] = new[] { ".resx" }
            };
            if (!extensions.TryGetValue(resourceType, out var allowed)
                || !allowed.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The selected file extension does not match the CRM web resource type. Upload compiled JavaScript, not TypeScript.");
            }
        }

        public static bool ContentMatches(byte[] local, string remoteBase64)
        {
            using (var hash = SHA256.Create())
            {
                var localHash = hash.ComputeHash(local);
                var remoteHash = hash.ComputeHash(Convert.FromBase64String(remoteBase64));
                return localHash.SequenceEqual(remoteHash);
            }
        }

        private static string NormalizeCulture(string? culture) =>
            string.IsNullOrWhiteSpace(culture) || string.Equals(culture, "neutral", StringComparison.OrdinalIgnoreCase)
                ? "neutral" : culture!;
    }
}
