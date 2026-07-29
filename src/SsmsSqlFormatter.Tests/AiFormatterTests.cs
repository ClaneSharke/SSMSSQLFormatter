using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SsmsSqlFormatter.Formatting;
using SsmsSqlFormatter.Options;

namespace SsmsSqlFormatter.Tests
{
    [TestFixture]
    public class AiFormatterTests
    {
        [Test]
        public void StripCodeFences_RemovesTripleBacktickFenceWithLanguageTag()
        {
            var text = "```sql\nSELECT 1\n```";
            Assert.AreEqual("SELECT 1", AiFormatter.StripCodeFences(text));
        }

        [Test]
        public void StripCodeFences_RemovesPlainTripleBacktickFence()
        {
            var text = "```\nSELECT 1\n```";
            Assert.AreEqual("SELECT 1", AiFormatter.StripCodeFences(text));
        }

        [Test]
        public void StripCodeFences_LeavesPlainTextUnchanged()
        {
            var text = "SELECT 1\nFROM t";
            Assert.AreEqual(text, AiFormatter.StripCodeFences(text));
        }

        [Test]
        public void StripCodeFences_RemovesSingleBacktickWrapping()
        {
            Assert.AreEqual("SELECT 1", AiFormatter.StripCodeFences("`SELECT 1`"));
        }

        [Test]
        public void ParseModelOutput_ExtractsTextFromContentArray()
        {
            var json = JObject.Parse(@"{""content"":[{""type"":""text"",""text"":""SELECT 1""}]}");
            Assert.AreEqual("SELECT 1", AiFormatter.ParseModelOutput(json));
        }

        [Test]
        public void ParseModelOutput_ConcatenatesMultipleTextBlocks()
        {
            var json = JObject.Parse(@"{""content"":[{""type"":""text"",""text"":""SELECT 1""},{""type"":""text"",""text"":"" FROM t""}]}");
            Assert.AreEqual("SELECT 1 FROM t", AiFormatter.ParseModelOutput(json));
        }

        [Test]
        public void ParseModelOutput_FallsBackToCompletionField()
        {
            var json = JObject.Parse(@"{""completion"":""SELECT 2""}");
            Assert.AreEqual("SELECT 2", AiFormatter.ParseModelOutput(json));
        }

        [Test]
        public void ParseModelOutput_ReturnsEmptyForUnrecognizedShape()
        {
            var json = JObject.Parse(@"{""foo"":""bar""}");
            Assert.AreEqual(string.Empty, AiFormatter.ParseModelOutput(json));
        }

        [Test]
        public void TryGetApiError_ExtractsMessageFromErrorObject()
        {
            var body = @"{""error"":{""type"":""invalid_request_error"",""message"":""bad key""}}";
            Assert.AreEqual("bad key", AiFormatter.TryGetApiError(body));
        }

        [Test]
        public void TryGetApiError_ReturnsRawBodyWhenNotJson()
        {
            var body = "Internal Server Error";
            Assert.AreEqual(body, AiFormatter.TryGetApiError(body));
        }

        [Test]
        public void BuildSystemPrompt_IncludesCustomInstructionsWhenPresent()
        {
            var general = new FormatterSettings();
            var ai = new AiOptions { CustomInstructions = "use leading commas" };

            var prompt = AiFormatter.BuildSystemPrompt(general, ai);

            StringAssert.Contains("use leading commas", prompt);
            StringAssert.Contains("PRESERVE all comments", prompt);
        }

        [Test]
        public void BuildSystemPrompt_OmitsStyleGuideWhenDisabled()
        {
            var general = new FormatterSettings();
            var ai = new AiOptions { UseGeneralOptionsAsStyleGuide = false, CustomInstructions = "" };

            var prompt = AiFormatter.BuildSystemPrompt(general, ai);

            StringAssert.DoesNotContain("Style guide:", prompt);
        }
    }
}
