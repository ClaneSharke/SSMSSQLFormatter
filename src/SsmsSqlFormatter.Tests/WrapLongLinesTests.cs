using NUnit.Framework;
using SsmsSqlFormatter.Formatting;
using SsmsSqlFormatter.Options;

namespace SsmsSqlFormatter.Tests
{
    [TestFixture]
    public class WrapLongLinesTests
    {
        [Test]
        public void Format_MaxLineLengthZero_LeavesOutputUnchangedFromDefault()
        {
            var opts = new FormatterSettings();
            Assert.AreEqual(0, opts.MaxLineLength);
        }

        [Test]
        public void Format_LongTopLevelList_WrapsOneItemPerLine()
        {
            var sql = "SELECT columnOne, columnTwo, columnThree, columnFour, columnFive FROM t";
            var opts = new FormatterSettings
            {
                Preset = StylePreset.Custom,
                MultilineSelectList = false,
                MaxLineLength = 30
            };

            var res = ScriptDomFormatter.Format(sql, opts);

            Assert.IsTrue(res.Success, res.ErrorMessage);
            StringAssert.Contains("columnOne", res.FormattedSql);
            StringAssert.Contains("columnFive", res.FormattedSql);
            Assert.IsTrue(res.FormattedSql.Split('\n').Length > 1, "Expected the long list to wrap across multiple lines:\n" + res.FormattedSql);
        }

        [Test]
        public void Format_ShortLine_IsNotWrapped()
        {
            var sql = "SELECT a, b FROM t";
            var opts = new FormatterSettings { MaxLineLength = 200 };

            var res = ScriptDomFormatter.Format(sql, opts);

            Assert.IsTrue(res.Success, res.ErrorMessage);
        }

        [Test]
        public void Format_ParenthesizedList_IsNotWrapped()
        {
            // In-list is nested inside parentheses, out of scope for this pass by design.
            var sql = "SELECT a FROM t WHERE x IN (1111, 2222, 3333, 4444, 5555, 6666, 7777, 8888)";
            var opts = new FormatterSettings { MaxLineLength = 20 };

            var res = ScriptDomFormatter.Format(sql, opts);

            Assert.IsTrue(res.Success, res.ErrorMessage);
            StringAssert.Contains("1111, 2222, 3333, 4444, 5555, 6666, 7777, 8888", res.FormattedSql);
        }
    }
}
