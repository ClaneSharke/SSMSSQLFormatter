using NUnit.Framework;
using SsmsSqlFormatter.Options;

namespace SsmsSqlFormatter.Tests
{
    [TestFixture]
    public class CredentialVaultTests
    {
        private const string TestTarget = "SsmsSqlFormatter.Tests:CredentialVaultTests";

        [TearDown]
        public void Cleanup() => CredentialVault.TryDelete(TestTarget);

        [Test]
        public void SaveThenLoad_RoundTripsTheSecret()
        {
            Assert.IsTrue(CredentialVault.TrySave(TestTarget, "sk-ant-test-12345"));
            Assert.AreEqual("sk-ant-test-12345", CredentialVault.TryLoad(TestTarget));
        }

        [Test]
        public void Load_WithNoStoredValue_ReturnsNull()
        {
            CredentialVault.TryDelete(TestTarget);
            Assert.IsNull(CredentialVault.TryLoad(TestTarget));
        }

        [Test]
        public void Save_OverwritesPreviousValue()
        {
            CredentialVault.TrySave(TestTarget, "first-value");
            CredentialVault.TrySave(TestTarget, "second-value");
            Assert.AreEqual("second-value", CredentialVault.TryLoad(TestTarget));
        }

        [Test]
        public void Save_WithEmptySecret_DeletesAnyExistingValue()
        {
            CredentialVault.TrySave(TestTarget, "will-be-deleted");
            Assert.IsTrue(CredentialVault.TrySave(TestTarget, string.Empty));
            Assert.IsNull(CredentialVault.TryLoad(TestTarget));
        }

        [Test]
        public void SaveThenLoad_RoundTripsUnicodeAndSpecialCharacters()
        {
            const string secret = "sk-ant-áéíóú-日本語-!@#$%^&*()";
            Assert.IsTrue(CredentialVault.TrySave(TestTarget, secret));
            Assert.AreEqual(secret, CredentialVault.TryLoad(TestTarget));
        }
    }
}
