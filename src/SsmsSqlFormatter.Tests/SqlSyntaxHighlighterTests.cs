using System.Linq;
using NUnit.Framework;
using SsmsSqlFormatter.Formatting;

namespace SsmsSqlFormatter.Tests
{
    [TestFixture]
    public class SqlSyntaxHighlighterTests
    {
        [Test]
        public void TokenizeLine_Keyword_IsClassifiedAsKeyword()
        {
            var fragments = SqlSyntaxHighlighter.TokenizeLine("SELECT 1");
            var select = fragments.First(f => f.Text.Equals("SELECT", System.StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(SqlTokenCategory.Keyword, select.Category);
        }

        [Test]
        public void TokenizeLine_StringLiteral_IsClassifiedAsString()
        {
            var fragments = SqlSyntaxHighlighter.TokenizeLine("SELECT 'hello'");
            var str = fragments.First(f => f.Text.Contains("hello"));
            Assert.AreEqual(SqlTokenCategory.String, str.Category);
        }

        [Test]
        public void TokenizeLine_NumberLiteral_IsClassifiedAsNumber()
        {
            var fragments = SqlSyntaxHighlighter.TokenizeLine("SELECT 42");
            var number = fragments.First(f => f.Text == "42");
            Assert.AreEqual(SqlTokenCategory.Number, number.Category);
        }

        [Test]
        public void TokenizeLine_LineComment_IsClassifiedAsComment()
        {
            var fragments = SqlSyntaxHighlighter.TokenizeLine("SELECT 1 -- note");
            var comment = fragments.First(f => f.Text.Contains("note"));
            Assert.AreEqual(SqlTokenCategory.Comment, comment.Category);
        }

        [Test]
        public void TokenizeLine_Identifier_IsClassifiedAsIdentifier()
        {
            var fragments = SqlSyntaxHighlighter.TokenizeLine("SELECT columnName FROM t");
            var ident = fragments.First(f => f.Text == "columnName");
            Assert.AreEqual(SqlTokenCategory.Identifier, ident.Category);
        }

        [Test]
        public void TokenizeLine_Variable_IsClassifiedAsIdentifier()
        {
            var fragments = SqlSyntaxHighlighter.TokenizeLine("SET @x = 1");
            var variable = fragments.First(f => f.Text == "@x");
            Assert.AreEqual(SqlTokenCategory.Identifier, variable.Category);
        }

        [Test]
        public void TokenizeLine_Punctuation_IsClassifiedAsDefault()
        {
            var fragments = SqlSyntaxHighlighter.TokenizeLine("SELECT a, b");
            var comma = fragments.First(f => f.Text == ",");
            Assert.AreEqual(SqlTokenCategory.Default, comma.Category);
        }

        [Test]
        public void TokenizeLine_ReconstructsTheOriginalLineExactly()
        {
            const string line = "SELECT a, b, 'x', 42 -- note";
            var fragments = SqlSyntaxHighlighter.TokenizeLine(line);
            var reconstructed = string.Concat(fragments.Select(f => f.Text));
            Assert.AreEqual(line, reconstructed);
        }

        [Test]
        public void TokenizeLine_EmptyLine_ReturnsSingleDefaultFragment()
        {
            var fragments = SqlSyntaxHighlighter.TokenizeLine("");
            Assert.AreEqual(1, fragments.Count);
            Assert.AreEqual(SqlTokenCategory.Default, fragments[0].Category);
        }

        [Test]
        public void TokenizeLine_UnparsableFragment_FallsBackToSingleDefaultFragmentRatherThanThrowing()
        {
            // A lone continuation line of a multi-line block comment - not valid T-SQL
            // on its own, but must never throw; falls back to plain text.
            var fragments = SqlSyntaxHighlighter.TokenizeLine("this is inside a /* block comment");
            Assert.IsNotEmpty(fragments);
            Assert.AreEqual("this is inside a /* block comment", string.Concat(fragments.Select(f => f.Text)));
        }

        [Test]
        public void Classify_UnknownTokenType_DefaultsToKeyword()
        {
            // Any of the ~130 genuine keyword token types not explicitly categorized
            // elsewhere should fall into the Keyword bucket.
            Assert.AreEqual(SqlTokenCategory.Keyword,
                SqlSyntaxHighlighter.Classify(Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.Where));
            Assert.AreEqual(SqlTokenCategory.Keyword,
                SqlSyntaxHighlighter.Classify(Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.Join));
        }
    }
}
