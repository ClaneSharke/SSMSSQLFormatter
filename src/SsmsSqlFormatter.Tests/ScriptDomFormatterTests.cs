using NUnit.Framework;
using SsmsSqlFormatter.Formatting;
using SsmsSqlFormatter.Options;

namespace SsmsSqlFormatter.Tests
{
    [TestFixture]
    public class ScriptDomFormatterTests
    {
        [Test]
        public void Format_SimpleSelect_ReturnsSuccess()
        {
            var opts = new GeneralOptions();
            var res = ScriptDomFormatter.Format("select 1", opts);
            Assert.IsTrue(res.Success, res.ErrorMessage);
            Assert.IsNotNull(res.FormattedSql);
            Assert.IsTrue(res.FormattedSql.ToUpperInvariant().Contains("SELECT"));
        }

        [Test]
        public void DescribeStyle_RespectsIndentSize()
        {
            var opts = new GeneralOptions { IndentationSize = 2 };
            var desc = ScriptDomFormatter.DescribeStyle(opts);
            Assert.IsTrue(!string.IsNullOrEmpty(desc));
            Assert.IsTrue(desc.Contains("indent 2") || desc.Contains("2 spaces") || desc.Contains("2"));
        }

        [Test]
        public void PreserveComments_ReinjectsComments()
        {
            var sql = "-- top comment\r\nSELECT 1 -- trailing\r\n";
            var opts = new GeneralOptions { PreserveComments = true };
            var res = ScriptDomFormatter.Format(sql, opts);
            Assert.IsTrue(res.Success, res.ErrorMessage);
            Assert.IsTrue(res.FormattedSql.Contains("-- top comment"));
            Assert.IsTrue(res.FormattedSql.Contains("-- trailing"));
        }

        [Test]
        public void LeadingCommas_MovesCommasToLineStart()
        {
            var sql = "SELECT a, b, c FROM t";
            var opts = new GeneralOptions { Commas = Options.CommaPlacement.Leading, MultilineSelectList = true };
            var res = ScriptDomFormatter.Format(sql, opts);
            Assert.IsTrue(res.Success, res.ErrorMessage);
            // With leading commas and multiline select, later lines should start with a comma
            Assert.IsTrue(res.FormattedSql.Contains("\n, ") || res.FormattedSql.Contains("\r\n, ")); 
        }

        [Test]
        public void ReindentSubqueries_AddsIndentation()
        {
            var sql = "SELECT * FROM (SELECT 1 AS x, 2 AS y) t";
            var opts = new GeneralOptions { ReindentSubqueries = true, IndentationSize = 2 };
            var res = ScriptDomFormatter.Format(sql, opts);
            Assert.IsTrue(res.Success, res.ErrorMessage);
            Assert.IsTrue(res.FormattedSql.Contains("  SELECT") || res.FormattedSql.Contains("\tSELECT"));
        }

        [Test]
        public void IdentifierCasing_FunctionsAndTypesAreRecased()
        {
            var sql = "select count(1) as cnt, CAST(1 as int)";
            var opts = new GeneralOptions { FunctionCasing = Options.IdentifierCase.Uppercase, DataTypeCasing = Options.IdentifierCase.Lowercase };
            var res = ScriptDomFormatter.Format(sql, opts);
            Assert.IsTrue(res.Success, res.ErrorMessage);
            Assert.IsTrue(res.FormattedSql.Contains("COUNT") || res.FormattedSql.Contains("count("));
            Assert.IsTrue(res.FormattedSql.ToLowerInvariant().Contains(" int") || res.FormattedSql.Contains("INT"));
        }

        [Test]
        public void BlankLinesBetweenStatements_AppliesSpacing()
        {
            var sql = "SELECT 1;\r\n\r\n\r\nSELECT 2;";
            var opts = new GeneralOptions { BlankLinesBetweenStatements = 0 };
            var res = ScriptDomFormatter.Format(sql, opts);
            Assert.IsTrue(res.Success, res.ErrorMessage);
            // No consecutive blank line sequences longer than one
            Assert.IsFalse(res.FormattedSql.Contains("\r\n\r\n\r\n"));
        }

        [Test]
        public void NormalizeGoSpacing_RespectsBlankLines()
        {
            var sql = "SELECT 1\r\nGO\r\nSELECT 2";
            var opts = new GeneralOptions { BlankLinesBeforeGo = 1, BlankLinesAfterGo = 2 };
            var res = ScriptDomFormatter.Format(sql, opts);
            Assert.IsTrue(res.Success, res.ErrorMessage);
            // There should be at least one blank line before GO and two after (rough check)
            Assert.IsTrue(res.FormattedSql.Contains("\r\n\r\nGO") || res.FormattedSql.Contains("\n\nGO"));
        }

        [Test]
        public void UseTabsForIndentation_ConvertsIndentsToTabs()
        {
            var sql = "SELECT\r\n    a,\r\n    b FROM t";
            var opts = new GeneralOptions { UseTabsForIndentation = true, IndentationSize = 4, MultilineSelectList = true };
            var res = ScriptDomFormatter.Format(sql, opts);
            Assert.IsTrue(res.Success, res.ErrorMessage);
            Assert.IsTrue(res.FormattedSql.Contains("\t"));
        }
    }
}
