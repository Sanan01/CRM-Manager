using System;
using System.Net;
using System.Security;

namespace BAJPluginManager.Core
{
    public static class WindowsAuthentication
    {
        public static NetworkCredential CreateCredential(bool useCurrentAccount, string domain, string username, SecureString? password)
        {
            if (useCurrentAccount)
            {
                return CredentialCache.DefaultNetworkCredentials;
            }

            domain = domain.Trim();
            username = username.Trim();
            if (string.IsNullOrWhiteSpace(username) || password == null || password.Length == 0)
            {
                throw new ArgumentException("Enter your CRM Windows username and password.");
            }

            var separator = username.IndexOf('\\');
            if (separator >= 0)
            {
                if (separator == 0 || separator == username.Length - 1
                    || username.IndexOf('\\', separator + 1) >= 0
                    || username.Contains("@"))
                {
                    throw new ArgumentException("Use DOMAIN\\username or user@domain, not a combination of the two.");
                }

                var usernameDomain = username.Substring(0, separator);
                if (domain.Length > 0 && !string.Equals(domain, usernameDomain, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("The Domain field must match the domain in DOMAIN\\username, or be left empty.");
                }

                domain = usernameDomain;
                username = username.Substring(separator + 1);
            }
            else if (username.Contains("@"))
            {
                var at = username.IndexOf('@');
                if (domain.Length > 0 || at == 0 || at == username.Length - 1 || username.IndexOf('@', at + 1) >= 0)
                {
                    throw new ArgumentException("For user@domain login, enter a complete username and leave the Domain field empty.");
                }
            }
            else if (domain.Length == 0)
            {
                throw new ArgumentException("Enter your Windows domain, or use DOMAIN\\username or user@domain.");
            }

            return new NetworkCredential(username, password, domain);
        }
    }
}
