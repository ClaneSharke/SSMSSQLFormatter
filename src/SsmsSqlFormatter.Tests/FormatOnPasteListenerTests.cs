using NUnit.Framework;
using SsmsSqlFormatter;

namespace SsmsSqlFormatter.Tests
{
    [TestFixture]
    public class FormatOnPasteListenerTests
    {
        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("x", false, Description = "single character - ordinary typing")]
        [TestCase("xy", false, Description = "multi-character but single-line - typing a short word, not a paste")]
        [TestCase("SELECT 1\nFROM t", true, Description = "multi-line - the shape of a paste")]
        [TestCase("a\r\nb", true)]
        public void LooksLikePasteText_DetectsMultiLineMultiCharacterInsertions(string newText, bool expected)
        {
            Assert.AreEqual(expected, FormatOnPasteHandler.LooksLikePasteText(newText));
        }
    }
}
