using BAJPluginManager.App;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Net;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace BAJPluginManager.Tests
{
    [TestClass]
    public class ConnectionProfileTests
    {
        private string directory = "";
        private string path = "";
        private ConnectionProfileStore store = null!;

        [TestInitialize]
        public void Initialize()
        {
            directory = Path.Combine(Path.GetTempPath(), "CRMManager-Profiles-" + Guid.NewGuid().ToString("N"));
            path = Path.Combine(directory, "connections.dat");
            store = new ConnectionProfileStore(path);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [TestMethod]
        public void TestSavedProfilesRoundTripSelectionAndEncryptedCredentials()
        {
            const string password = "Offline-only-\u03a9-\ud83d\udd10";
            var first = Profile("First development", password);
            var second = Profile("Second development", "other-offline-secret");
            var state = new SavedConnections { Connections = { first, second }, SelectedId = second.Id };
            store.Save(state);
            var bytes = File.ReadAllBytes(path);
            Assert.IsFalse(Encoding.UTF8.GetString(bytes).Contains(first.Username));
            Assert.IsFalse(Encoding.Unicode.GetString(bytes).Contains(password));
            var loaded = store.Load();
            Assert.AreEqual(2, loaded.Connections.Count);
            Assert.AreEqual(second.Id, loaded.SelectedId);
            Assert.AreEqual(first.Address, loaded.Connections[0].Address);
            Assert.AreEqual(first.Domain, loaded.Connections[0].Domain);
            Assert.AreEqual(first.Username, loaded.Connections[0].Username);
            using (var secret = loaded.Connections[0].OpenPassword())
                Assert.AreEqual(password, new NetworkCredential("", secret).Password);
            using (var secret = Secret("replacement-offline-secret"))
                loaded.Connections[0] = SavedConnection.Create(first.Id, "Renamed development", first.Address,
                    first.Domain, first.Username, secret);
            loaded.SelectedId = first.Id;
            store.Save(loaded);
            var replaced = store.Load();
            Assert.AreEqual("Renamed development", replaced.Connections[0].Name);
            using (var secret = replaced.Connections[0].OpenPassword())
                Assert.AreEqual("replacement-offline-secret", new NetworkCredential("", secret).Password);
            Assert.AreEqual(second.Id, replaced.Connections[1].Id);
            Assert.AreEqual(first.Id, replaced.SelectedId);
            replaced.Connections.RemoveAt(0);
            replaced.SelectedId = second.Id;
            store.Save(replaced);
            Assert.AreEqual(second.Id, store.Load().Connections[0].Id);
            store.ForgetAll();
            Assert.AreEqual(0, store.Load().Connections.Count);
        }

        [TestMethod]
        public void TestDuplicateProfileNamesCannotReplaceSavedFile()
        {
            var first = Profile("Development", "offline-secret");
            store.Save(new SavedConnections { Connections = { first }, SelectedId = first.Id });
            var original = File.ReadAllBytes(path);
            var duplicate = new SavedConnections { Connections = { first, Profile("DEVELOPMENT", "other-secret") } };
            Assert.ThrowsException<InvalidDataException>(() => store.Save(duplicate));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        }

        [TestMethod]
        public void TestFailedProfileWritePreservesExistingFileAndRemovesStagingFile()
        {
            var first = Profile("Development", "offline-secret");
            var state = new SavedConnections { Connections = { first }, SelectedId = first.Id };
            store.Save(state);
            var original = File.ReadAllBytes(path);
            state.Connections.Add(Profile("Another development", "other-secret"));
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.ThrowsException<IOException>(() => store.Save(state));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.AreEqual(1, Directory.GetFiles(directory).Length);
        }

        [TestMethod]
        public void TestTamperedProfileFileReportsDecryptionFailure()
        {
            var first = Profile("Development", "offline-secret");
            store.Save(new SavedConnections { Connections = { first }, SelectedId = first.Id });
            var bytes = File.ReadAllBytes(path);
            bytes[bytes.Length / 2] ^= 0xff;
            File.WriteAllBytes(path, bytes);
            Assert.ThrowsException<CryptographicException>(() => store.Load());
        }

        private static SavedConnection Profile(string name, string password)
        {
            using (var secret = Secret(password))
                return SavedConnection.Create(Guid.NewGuid(), name, "https://crm.example.test/Development",
                    "", "offline-developer@example.test", secret);
        }

        internal static SecureString Secret(string value)
        {
            var password = new SecureString();
            foreach (var character in value) password.AppendChar(character);
            password.MakeReadOnly();
            return password;
        }
    }
}
