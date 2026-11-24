using BAJPluginManager.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace BAJPluginManager.App
{
    [DataContract]
    public sealed class SavedConnection
    {
        private static readonly byte[] PasswordEntropy = Encoding.UTF8.GetBytes("CRMManager.Password.v1");

        [DataMember(IsRequired = true)] public Guid Id { get; private set; }
        [DataMember(IsRequired = true)] public string Name { get; private set; } = "";
        [DataMember(IsRequired = true)] public string Address { get; private set; } = "";
        [DataMember(IsRequired = true)] public string Domain { get; private set; } = "";
        [DataMember(IsRequired = true)] public string Username { get; private set; } = "";
        [DataMember(IsRequired = true)] private byte[] password = Array.Empty<byte>();

        public static SavedConnection Create(Guid id, string name, string address, string domain, string username, SecureString secret)
        {
            var connection = new SavedConnection
            {
                Id = id, Name = name.Trim(), Address = address.Trim(), Domain = domain.Trim(), Username = username.Trim()
            };
            if (id == Guid.Empty || connection.Name.Length == 0)
                throw new ArgumentException("Enter a name for the saved connection.");
            DeploymentRules.GetOrganizationUniqueName(DeploymentRules.GetServiceUri(connection.Address));
            WindowsAuthentication.CreateCredential(false, connection.Domain, connection.Username, secret);
            var pointer = Marshal.SecureStringToBSTR(secret);
            var bytes = new byte[checked(secret.Length * 2)];
            try
            {
                Marshal.Copy(pointer, bytes, 0, bytes.Length);
                connection.password = ProtectedData.Protect(bytes, PasswordEntropy, DataProtectionScope.CurrentUser);
                return connection;
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
                Marshal.ZeroFreeBSTR(pointer);
            }
        }

        public SecureString OpenPassword()
        {
            var bytes = ProtectedData.Unprotect(password, PasswordEntropy, DataProtectionScope.CurrentUser);
            var result = new SecureString();
            try
            {
                if (bytes.Length == 0 || bytes.Length % 2 != 0)
                    throw new InvalidDataException("The saved password is invalid. Save this connection again.");
                for (var i = 0; i < bytes.Length; i += 2)
                    result.AppendChar((char)(bytes[i] | bytes[i + 1] << 8));
                result.MakeReadOnly();
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }

        internal void Validate()
        {
            if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Address) || Domain == null
                || string.IsNullOrWhiteSpace(Username) || password == null || password.Length == 0)
                throw new InvalidDataException("A saved connection is incomplete.");
            DeploymentRules.GetOrganizationUniqueName(DeploymentRules.GetServiceUri(Address));
        }
    }

    [DataContract]
    public sealed class SavedConnections
    {
        [DataMember(IsRequired = true)] public int FormatVersion { get; set; } = 1;
        [DataMember(IsRequired = true)] public List<SavedConnection> Connections { get; set; } = new List<SavedConnection>();
        [DataMember(IsRequired = true)] public Guid SelectedId { get; set; }

        public SavedConnections Copy() => new SavedConnections
        {
            FormatVersion = FormatVersion, Connections = Connections.ToList(), SelectedId = SelectedId
        };

        internal void Validate()
        {
            if (FormatVersion != 1 || Connections == null || Connections.Any(connection => connection == null))
                throw new InvalidDataException("The saved connection file has an unsupported or invalid format.");
            foreach (var connection in Connections) connection.Validate();
            if (Connections.Select(connection => connection.Id).Distinct().Count() != Connections.Count
                || Connections.Select(connection => connection.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Connections.Count)
                throw new InvalidDataException("Saved connections must have unique names and identities.");
            if (SelectedId != Guid.Empty && !Connections.Any(connection => connection.Id == SelectedId))
                throw new InvalidDataException("The last selected saved connection is missing.");
        }
    }

    public sealed class ConnectionProfileStore
    {
        private static readonly byte[] FileEntropy = Encoding.UTF8.GetBytes("CRMManager.Connections.v1");
        private readonly string path;

        public ConnectionProfileStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A saved connection file path is required.", nameof(path));
            this.path = Path.GetFullPath(path);
        }

        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CRMManager", "connections.dat");

        public SavedConnections Load()
        {
            byte[] encrypted;
            try { encrypted = File.ReadAllBytes(path); }
            catch (FileNotFoundException) { return new SavedConnections(); }
            catch (DirectoryNotFoundException) { return new SavedConnections(); }
            var bytes = ProtectedData.Unprotect(encrypted, FileEntropy, DataProtectionScope.CurrentUser);
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                {
                    var state = new DataContractSerializer(typeof(SavedConnections)).ReadObject(stream) as SavedConnections
                        ?? throw new InvalidDataException("The saved connection file is empty.");
                    state.Validate();
                    return state;
                }
            }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }

        public void Save(SavedConnections state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            state.Validate();
            byte[] bytes;
            using (var stream = new MemoryStream())
            {
                new DataContractSerializer(typeof(SavedConnections)).WriteObject(stream, state);
                bytes = stream.ToArray();
                Array.Clear(stream.GetBuffer(), 0, checked((int)stream.Length));
            }
            byte[] encrypted;
            try { encrypted = ProtectedData.Protect(bytes, FileEntropy, DataProtectionScope.CurrentUser); }
            finally { Array.Clear(bytes, 0, bytes.Length); }
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(encrypted, 0, encrypted.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public void ForgetAll() => File.Delete(path);
    }
}
