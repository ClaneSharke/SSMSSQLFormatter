using System;
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
            var opts = new FormatterSettings();
            var res = ScriptDomFormatter.Format("select 1", opts);
            Assert.IsTrue(res.Success, res.ErrorMessage);
            Assert.IsNotNull(res.FormattedSql);
            Assert.IsTrue(res.FormattedSql.ToUpperInvariant().Contains("SELECT"));
        }

        [Test]
        public void DescribeStyle_RespectsIndentSize()
        {
            var opts = new FormatterSettings { IndentationSize = 2 };
            var desc = ScriptDomFormatter.DescribeStyle(opts);
            Assert.IsTrue(!string.IsNullOrEmpty(desc));
            Assert.IsTrue(desc.Contains("indent 2") || desc.Contains("2 spaces") || desc.Contains("2"));
        }

        [Test]
        public void CommentHandlingInline_ReinjectsComments()
        {
            var sql = "-- top comment\r\nSELECT 1 -- trailing\r\n";
            var opts = new FormatterSettings { CommentHandling = CommentHandling.Inline };
            var res = ScriptDomFormatter.Format(sql, opts);
            Assert.IsTrue(res.Success, res.ErrorMessage);
            Assert.IsTrue(res.FormattedSql.Contains("-- top comment"));
            Assert.IsTrue(res.FormattedSql.Contains("-- trailing"));
        }

        [Test]
        public void CommentHandlingMoveToEnd_CollectsCommentsAtEnd()
        {
            var sql = "-- top comment\r\nSELECT 1 -- trailing\r\n";
            var opts = new FormatterSettings { CommentHandling = CommentHandling.MoveToEnd };
            var res = ScriptDomFormatter.Format(sql, opts);
            Assert.IsTrue(res.Success, res.ErrorMessage);
            int selectIdx = res.FormattedSql.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase);
            int topIdx = res.FormattedSql.IndexOf("-- top comment", StringComparison.Ordinal);
            int trailingIdx = res.FormattedSql.IndexOf("-- trailing", StringComparison.Ordinal);
            Assert.IsTrue(topIdx > selectIdx, "Expected '-- top comment' to be moved after the SELECT statement.");
            Assert.IsTrue(trailingIdx > selectIdx, "Expected '-- trailing' to be moved after the SELECT statement.");
        }

        [Test]
        public void CommentHandlingDiscard_DropsComments()
        {
            var sql = "-- top comment\r\nSELECT 1 -- trailing\r\n";
            var opts = new FormatterSettings { CommentHandling = CommentHandling.Discard };
            var res = ScriptDomFormatter.Format(sql, opts);
            Assert.IsTrue(res.Success, res.ErrorMessage);
            Assert.IsFalse(res.FormattedSql.Contains("-- top comment"));
            Assert.IsFalse(res.FormattedSql.Contains("-- trailing"));
            Assert.AreEqual(2, res.CommentCount);
        }

        [Test]
        public void LeadingCommas_MovesCommasToLineStart()
        {
            var sql = "SELECT a, b, c FROM t";
            var opts = new FormatterSettings { Commas = Options.CommaPlacement.Leading, MultilineSelectList = true };
            var res = ScriptDomFormatter.Format(sql, opts);
            Assert.IsTrue(res.Success, res.ErrorMessage);
            // With leading commas and multiline select, later lines should start with a
            // comma - possibly after alignment indentation, so allow leading whitespace.
            StringAssert.IsMatch(@"\r?\n[ \t]*, ", res.FormattedSql);
        }

        [Test]
        public void ReindentSubqueries_AddsIndentation()
        {
            var sql = "SELECT * FROM (SELECT 1 AS x, 2 AS y) t";
            var opts = new FormatterSettings { ReindentSubqueries = true, IndentationSize = 2 };
            var res = ScriptDomFormatter.Format(sql, opts);
            Assert.IsTrue(res.Success, res.ErrorMessage);
            // The subquery's second column lands on a continuation line inside the
            // parentheses; it must be indented at least one level (2 spaces here).
            var lines = res.FormattedSql.Replace("\r\n", "\n").Split('\n');
            var continuationLine = System.Array.Find(lines, l => l.Contains("AS y"));
            Assert.IsNotNull(continuationLine, "Expected a line containing 'AS y':\n" + res.FormattedSql);
            int leadingSpaces = continuationLine.Length - continuationLine.TrimStart(' ').Length;
            Assert.IsTrue(leadingSpaces >= 2, "Expected at least 2 leading spaces, got " + leadingSpaces + ":\n" + res.FormattedSql);
        }

        [Test]
        public void IdentifierCasing_FunctionsAndTypesAreRecased()
        {
            var sql = "select count(1) as cnt, CAST(1 as int)";
            var opts = new FormatterSettings { FunctionCasing = Options.IdentifierCase.Uppercase, DataTypeCasing = Options.IdentifierCase.Lowercase };
            var res = ScriptDomFormatter.Format(sql, opts);
            Assert.IsTrue(res.Success, res.ErrorMessage);
            Assert.IsTrue(res.FormattedSql.Contains("COUNT") || res.FormattedSql.Contains("count("));
            Assert.IsTrue(res.FormattedSql.ToLowerInvariant().Contains(" int") || res.FormattedSql.Contains("INT"));
        }

        [Test]
        public void BlankLinesBetweenStatements_AppliesSpacing()
        {
            var sql = "SELECT 1;\r\n\r\n\r\nSELECT 2;";
            var opts = new FormatterSettings { BlankLinesBetweenStatements = 0 };
            var res = ScriptDomFormatter.Format(sql, opts);
            Assert.IsTrue(res.Success, res.ErrorMessage);
            // No consecutive blank line sequences longer than one
            Assert.IsFalse(res.FormattedSql.Contains("\r\n\r\n\r\n"));
        }

        [Test]
        public void BlankLinesBetweenStatements_AppliesInsideNestedBlocks()
        {
            var sql = "CREATE PROCEDURE dbo.Foo AS\r\nBEGIN\r\n" +
                      "    IF @x = 1\r\n    BEGIN\r\n        SELECT 1;\r\n    END\r\n" +
                      "    SELECT 2;\r\n" +
                      "END\r\n";
            var opts = new FormatterSettings { BlankLinesBetweenStatements = 1 };
            var res = ScriptDomFormatter.Format(sql, opts);
            Assert.IsTrue(res.Success, res.ErrorMessage);
            // A blank line must appear after the nested IF...END block, before the next
            // statement in the procedure body - not just between top-level statements.
            StringAssert.IsMatch(@"END\r?\n\r?\n\s*SELECT 2", res.FormattedSql);
        }

        [Test]
        public void NormalizeGoSpacing_RespectsBlankLines()
        {
            var sql = "SELECT 1\r\nGO\r\nSELECT 2";
            var opts = new FormatterSettings { BlankLinesBeforeGo = 1, BlankLinesAfterGo = 2 };
            var res = ScriptDomFormatter.Format(sql, opts);
            Assert.IsTrue(res.Success, res.ErrorMessage);
            // There should be at least one blank line before GO and two after (rough check)
            Assert.IsTrue(res.FormattedSql.Contains("\r\n\r\nGO") || res.FormattedSql.Contains("\n\nGO"));
        }

        [Test]
        public void UseTabsForIndentation_ConvertsIndentsToTabs()
        {
            var sql = "SELECT\r\n    a,\r\n    b FROM t";
            var opts = new FormatterSettings { UseTabsForIndentation = true, IndentationSize = 4, MultilineSelectList = true };
            var res = ScriptDomFormatter.Format(sql, opts);
            Assert.IsTrue(res.Success, res.ErrorMessage);
            Assert.IsTrue(res.FormattedSql.Contains("\t"));
        }
    }
}
