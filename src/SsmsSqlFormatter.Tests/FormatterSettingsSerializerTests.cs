using System;
using System.IO;
using NUnit.Framework;
using SsmsSqlFormatter.Options;

namespace SsmsSqlFormatter.Tests
{
    [TestFixture]
    public class FormatterSettingsSerializerTests
    {
        [Test]
        public void ToJson_ThenApplyFromJson_RoundTripsValues()
        {
            var source = new FormatterSettings
            {
                Preset = StylePreset.Custom,
                KeywordCasing = KeywordCase.Lowercase,
                IndentationSize = 2,
                Commas = CommaPlacement.Leading,
                MaxLineLength = 100
            };

            var json = FormatterSettingsSerializer.ToJson(source);
            var target = new FormatterSettings();
            var (applied, skipped) = FormatterSettingsSerializer.ApplyFromJson(target, json);

            Assert.Greater(applied, 0);
            Assert.AreEqual(StylePreset.Custom, target.Preset);
            Assert.AreEqual(KeywordCase.Lowercase, target.KeywordCasing);
            Assert.AreEqual(2, target.IndentationSize);
            Assert.AreEqual(CommaPlacement.Leading, target.Commas);
            Assert.AreEqual(100, target.MaxLineLength);
        }

        [Test]
        public void ApplyFromJson_UnknownProperty_IsIgnoredNotThrown()
        {
            var target = new FormatterSettings();
            var json = "{\"ThisPropertyDoesNotExist\": 123, \"IndentationSize\": 8}";

            var (applied, skipped) = FormatterSettingsSerializer.ApplyFromJson(target, json);

            Assert.AreEqual(8, target.IndentationSize);
            Assert.AreEqual(1, applied);
        }

        [Test]
        public void ApplyFromJson_MalformedEnumValue_IsSkippedNotThrown()
        {
            var target = new FormatterSettings();
            var json = "{\"Preset\": \"NotARealPreset\", \"IndentationSize\": 6}";

            var (applied, skipped) = FormatterSettingsSerializer.ApplyFromJson(target, json);

            Assert.AreEqual(StylePreset.Modern, target.Preset, "malformed value should leave the default untouched");
            Assert.AreEqual(6, target.IndentationSize);
            Assert.AreEqual(1, skipped);
        }

        [Test]
        public void Clone_ProducesEqualButIndependentInstance()
        {
            var source = new FormatterSettings { IndentationSize = 2, KeywordCasing = KeywordCase.Lowercase };

            var clone = (FormatterSettings)FormatterSettingsSerializer.Clone(source);
            clone.IndentationSize = 99;

            Assert.AreNotSame(source, clone);
            Assert.AreEqual(KeywordCase.Lowercase, clone.KeywordCasing);
            Assert.AreEqual(2, source.IndentationSize, "mutating the clone must never affect the source");
        }

        [Test]
        public void CloneAndApplyJson_OverlaysJsonOntoACloneWithoutMutatingTheSource()
        {
            var source = new FormatterSettings { IndentationSize = 4, KeywordCasing = KeywordCase.Uppercase };
            var json = "{\"IndentationSize\": 2}";

            var result = (FormatterSettings)FormatterSettingsSerializer.CloneAndApplyJson(source, json);

            Assert.AreEqual(2, result.IndentationSize, "the JSON overlay should apply to the clone");
            Assert.AreEqual(KeywordCase.Uppercase, result.KeywordCasing, "properties not present in the JSON keep the source's value");
            Assert.AreEqual(4, source.IndentationSize, "the source must never be mutated");
        }

        [Test]
        public void ToJson_OnGeneralOptions_NeverThrowsOnInheritedDialogPageProperties()
        {
            // Regression test: GeneralOptions inherits DialogPage, whose own base-class
            // properties (AutomationObject, Site, Container, ...) are COM/design-time
            // objects. Reflecting over them (instead of just GeneralOptions' own
            // DeclaredOnly properties) crashes Newtonsoft.Json with "Self referencing
            // loop detected for property 'AutomationObject' ... Path 'Component.Inner'"
            // the moment a real GeneralOptions instance is exported.
            var general = new GeneralOptions();

            string json = null;
            Assert.DoesNotThrow(() => json = FormatterSettingsSerializer.ToJson(general));
            StringAssert.DoesNotContain("AutomationObject", json);
            StringAssert.Contains("IndentationSize", json);
        }

        [Test]
        public void Clone_OnGeneralOptions_NeverThrowsOnInheritedDialogPageProperties()
        {
            var general = new GeneralOptions { IndentationSize = 7 };

            IFormatterOptions clone = null;
            Assert.DoesNotThrow(() => clone = FormatterSettingsSerializer.Clone(general));
            Assert.AreEqual(7, clone.IndentationSize);
        }

        [Test]
        public void LoadFromJsonFile_ReadsSettingsFromDisk()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            try
            {
                File.WriteAllText(path, "{\"IndentationSize\": 3, \"KeywordCasing\": \"Lowercase\"}");

                var loaded = FormatterSettingsSerializer.LoadFromJsonFile<FormatterSettings>(path);

                Assert.AreEqual(3, loaded.IndentationSize);
                Assert.AreEqual(KeywordCase.Lowercase, loaded.KeywordCasing);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
