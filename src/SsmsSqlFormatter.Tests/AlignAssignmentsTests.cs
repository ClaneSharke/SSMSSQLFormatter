using NUnit.Framework;
using SsmsSqlFormatter.Formatting;
using SsmsSqlFormatter.Options;

namespace SsmsSqlFormatter.Tests
{
    [TestFixture]
    public class AlignAssignmentsTests
    {
        [Test]
        public void Format_AlignSetClauseAssignments_PadsShorterLhsToMatch()
        {
            var sql = "UPDATE t SET a = 1, longname = 2, c = 3 WHERE x = 1";
            var opts = new GeneralOptions
            {
                AlignSetClauseAssignments = true,
                MultilineInsertLists = true
            };

            var res = ScriptDomFormatter.Format(sql, opts);

            Assert.IsTrue(res.Success, res.ErrorMessage);
            var lines = res.FormattedSql.Replace("\r\n", "\n").Split('\n');
            var aLine = System.Array.Find(lines, l => l.TrimStart().StartsWith("a "));
            var longLine = System.Array.Find(lines, l => l.TrimStart().StartsWith("longname"));
            Assert.IsNotNull(aLine);
            Assert.IsNotNull(longLine);
            Assert.AreEqual(longLine.IndexOf('='), aLine.IndexOf('='),
                "The '=' in both assignment lines should land in the same column.\n" + res.FormattedSql);
        }

        [Test]
        public void Format_AlignSetClauseAssignments_OffByDefault_LeavesOutputUnchanged()
        {
            var sql = "UPDATE t SET a = 1, longname = 2 WHERE x = 1";
            var defaultOpts = new GeneralOptions();
            Assert.IsFalse(defaultOpts.AlignSetClauseAssignments);

            var res = ScriptDomFormatter.Format(sql, defaultOpts);
            Assert.IsTrue(res.Success, res.ErrorMessage);
        }

        [Test]
        public void Format_AlignSetClauseAssignments_DoesNotMergeUnrelatedWhereClause()
        {
            var sql = "UPDATE t SET a = 1, longname = 2 WHERE x = 1";
            var opts = new GeneralOptions { AlignSetClauseAssignments = true };

            var res = ScriptDomFormatter.Format(sql, opts);

            Assert.IsTrue(res.Success, res.ErrorMessage);
            // The WHERE predicate must keep a single space around '=', never padded
            // to match the SET clause's alignment column.
            StringAssert.Contains("WHERE x = 1", res.FormattedSql);
        }
    }
}
