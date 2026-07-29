using System;
using System.IO;
using NUnit.Framework;
using SsmsSqlFormatter.Options;

namespace SsmsSqlFormatter.Tests
{
    [TestFixture]
    public class FormatterConfigDiscoveryTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "sqlfmt-discovery-" + Guid.NewGuid());
            Directory.CreateDirectory(Path.Combine(_root, "sub", "deeper"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        private string ConfigPath => Path.Combine(_root, FormatterConfigDiscovery.ConfigFileName);

        [Test]
        public void FindConfigPath_NoConfigAnywhere_ReturnsNull()
        {
            var deep = Path.Combine(_root, "sub", "deeper");
            Assert.IsNull(FormatterConfigDiscovery.FindConfigPath(deep));
        }

        [Test]
        public void FindConfigPath_ConfigInSameFolder_IsFound()
        {
            File.WriteAllText(ConfigPath, "{}");
            Assert.AreEqual(ConfigPath, FormatterConfigDiscovery.FindConfigPath(_root));
        }

        [Test]
        public void FindConfigPath_ConfigInAncestorFolder_IsFoundFromDeeperDirectory()
        {
            File.WriteAllText(ConfigPath, "{}");
            var deep = Path.Combine(_root, "sub", "deeper");
            Assert.AreEqual(ConfigPath, FormatterConfigDiscovery.FindConfigPath(deep));
        }

        [Test]
        public void FindConfigPath_NonexistentStartDirectory_ReturnsNullRatherThanThrowing()
        {
            Assert.IsNull(FormatterConfigDiscovery.FindConfigPath(Path.Combine(_root, "does-not-exist")));
        }

        [Test]
        public void ResolveEffectiveSettings_NoConfigFound_ReturnsBaseSettingsUnchanged()
        {
            var baseSettings = new FormatterSettings { IndentationSize = 4 };
            var filePath = Path.Combine(_root, "sub", "deeper", "script.sql");

            var effective = FormatterConfigDiscovery.ResolveEffectiveSettings(filePath, baseSettings);

            Assert.AreSame(baseSettings, effective);
        }

        [Test]
        public void ResolveEffectiveSettings_ConfigFoundInAncestor_OverridesMatchingProperties()
        {
            File.WriteAllText(ConfigPath, "{\"IndentationSize\": 2, \"KeywordCasing\": \"Lowercase\"}");
            var baseSettings = new FormatterSettings { IndentationSize = 4, KeywordCasing = KeywordCase.Uppercase };
            var filePath = Path.Combine(_root, "sub", "deeper", "script.sql");

            var effective = (FormatterSettings)FormatterConfigDiscovery.ResolveEffectiveSettings(filePath, baseSettings);

            Assert.AreEqual(2, effective.IndentationSize);
            Assert.AreEqual(KeywordCase.Lowercase, effective.KeywordCasing);
        }

        [Test]
        public void ResolveEffectiveSettings_NeverMutatesTheOriginalBaseSettings()
        {
            File.WriteAllText(ConfigPath, "{\"IndentationSize\": 2}");
            var baseSettings = new FormatterSettings { IndentationSize = 4 };
            var filePath = Path.Combine(_root, "sub", "deeper", "script.sql");

            FormatterConfigDiscovery.ResolveEffectiveSettings(filePath, baseSettings);

            Assert.AreEqual(4, baseSettings.IndentationSize, "the original settings object must never be mutated");
        }

        [Test]
        public void ResolveEffectiveSettings_MalformedConfigFile_FallsBackToBaseSettingsRatherThanThrowing()
        {
            File.WriteAllText(ConfigPath, "{ this is not valid json");
            var baseSettings = new FormatterSettings { IndentationSize = 4 };
            var filePath = Path.Combine(_root, "sub", "deeper", "script.sql");

            var effective = FormatterConfigDiscovery.ResolveEffectiveSettings(filePath, baseSettings);

            Assert.AreSame(baseSettings, effective);
        }
    }
}
