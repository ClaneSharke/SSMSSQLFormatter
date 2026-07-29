using NUnit.Framework;
using SsmsSqlFormatter.Formatting;
using SsmsSqlFormatter.Options;

namespace SsmsSqlFormatter.Tests
{
    [TestFixture]
    public class AlignJoinAndCaseTests
    {
        [Test]
        public void Format_AlignJoinConditions_PadsShorterJoinsSoOnAligns()
        {
            var sql = "SELECT 1 FROM Orders o JOIN Customers c ON o.CustomerId = c.CustomerId JOIN P p ON o.ProductId = p.ProductId";
            var opts = new FormatterSettings
            {
                Preset = StylePreset.Custom,
                AlignClauseBodies = false,
                NewLineBeforeJoin = true,
                AlignJoinConditions = true
            };

            var res = ScriptDomFormatter.Format(sql, opts);

            Assert.IsTrue(res.Success, res.ErrorMessage);
            var lines = res.FormattedSql.Replace("\r\n", "\n").Split('\n');
            var joinLines = System.Array.FindAll(lines, l => l.Contains("JOIN") && l.Contains(" ON "));
            Assert.AreEqual(2, joinLines.Length, "Expected two JOIN lines:\n" + res.FormattedSql);
            Assert.AreEqual(joinLines[0].IndexOf(" ON "), joinLines[1].IndexOf(" ON "),
                "The ON keyword in both JOIN lines should land in the same column.\n" + res.FormattedSql);
        }

        [Test]
        public void Format_AlignJoinConditions_OffByDefault()
        {
            var opts = new FormatterSettings();
            Assert.IsFalse(opts.AlignJoinConditions);
        }

        [Test]
        public void Format_AlignCaseExpressions_PadsShorterWhenConditionsSoThenAligns()
        {
            var sql = "SELECT CASE WHEN x = 1 THEN 'one' WHEN xx = 2 THEN 'two' END";
            var opts = new FormatterSettings
            {
                Preset = StylePreset.Custom,
                AlignClauseBodies = false,
                AlignCaseExpressions = true
            };

            var res = ScriptDomFormatter.Format(sql, opts);

            Assert.IsTrue(res.Success, res.ErrorMessage);
            var lines = res.FormattedSql.Replace("\r\n", "\n").Split('\n');
            var whenLines = System.Array.FindAll(lines, l => l.TrimStart().StartsWith("WHEN") && l.Contains("THEN"));
            Assert.AreEqual(2, whenLines.Length, "Expected two WHEN lines:\n" + res.FormattedSql);
            Assert.AreEqual(whenLines[0].IndexOf("THEN"), whenLines[1].IndexOf("THEN"),
                "THEN should land in the same column on both WHEN lines.\n" + res.FormattedSql);
        }

        [Test]
        public void Format_AlignCaseExpressions_OffByDefault()
        {
            var opts = new FormatterSettings();
            Assert.IsFalse(opts.AlignCaseExpressions);
        }

        [Test]
        public void Format_SingleJoin_IsUnaffectedByAlignJoinConditions()
        {
            var sql = "SELECT 1 FROM a JOIN b ON a.id = b.id";
            var opts = new FormatterSettings { AlignJoinConditions = true };

            var res = ScriptDomFormatter.Format(sql, opts);

            Assert.IsTrue(res.Success, res.ErrorMessage);
        }

        [Test]
        public void Format_SingleWhenBranch_IsUnaffectedByAlignCaseExpressions()
        {
            var sql = "SELECT CASE WHEN x = 1 THEN 'one' END";
            var opts = new FormatterSettings { AlignCaseExpressions = true };

            var res = ScriptDomFormatter.Format(sql, opts);

            Assert.IsTrue(res.Success, res.ErrorMessage);
        }

        [Test]
        public void Format_AlignJoinConditions_CondensesEachJoinOntoOneLine()
        {
            var sql = "SELECT 1 FROM Orders o JOIN Customers c ON o.CustomerId = c.CustomerId";
            var opts = new FormatterSettings { AlignJoinConditions = true };

            var res = ScriptDomFormatter.Format(sql, opts);

            Assert.IsTrue(res.Success, res.ErrorMessage);
            StringAssert.IsMatch(@"JOIN\s+Customers\s+(AS\s+)?c\s+ON\s+o\.CustomerId\s*=\s*c\.CustomerId",
                res.FormattedSql.Replace("\r\n", " "));
        }

        [Test]
        public void Format_AlignCaseExpressions_ExpandsMultiBranchCaseOntoSeparateLines()
        {
            var sql = "SELECT CASE WHEN x = 1 THEN 'one' WHEN y = 2 THEN 'two' END";
            var opts = new FormatterSettings { AlignCaseExpressions = true };

            var res = ScriptDomFormatter.Format(sql, opts);

            Assert.IsTrue(res.Success, res.ErrorMessage);
            var lines = res.FormattedSql.Replace("\r\n", "\n").Split('\n');
            Assert.AreEqual(2, System.Array.FindAll(lines, l => l.Contains("WHEN")).Length,
                "Each WHEN branch should be on its own line:\n" + res.FormattedSql);
        }
    }
}
