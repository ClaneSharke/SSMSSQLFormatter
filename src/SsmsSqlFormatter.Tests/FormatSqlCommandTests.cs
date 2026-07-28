using System.Drawing;
using NUnit.Framework;
using SsmsSqlFormatter;

namespace SsmsSqlFormatter.Tests
{
    [TestFixture]
    public class FormatSqlCommandTests
    {
        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("plain single word", false)]
        [TestCase("a\tb", true)]
        [TestCase("line1\nline2", true)]
        [TestCase("a\tb\r\nc\td", true)]
        public void LooksLikeGridData_DetectsTabOrMultilineShape(string input, bool expected)
        {
            Assert.AreEqual(expected, FormatSqlCommand.LooksLikeGridData(input));
        }

        [Test]
        public void Hex_FormatsColorAsUppercaseRgbHexWithHash()
        {
            Assert.AreEqual("#FF0080", FormatSqlCommand.Hex(Color.FromArgb(0xFF, 0x00, 0x80)));
        }

        [Test]
        public void Hex_IgnoresAlphaChannel()
        {
            Assert.AreEqual("#000000", FormatSqlCommand.Hex(Color.FromArgb(128, 0, 0, 0)));
        }
    }
}
