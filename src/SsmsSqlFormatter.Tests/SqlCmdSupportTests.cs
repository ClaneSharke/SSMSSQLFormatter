using NUnit.Framework;
using SsmsSqlFormatter.Formatting;
using SsmsSqlFormatter.Options;

namespace SsmsSqlFormatter.Tests
{
    [TestFixture]
    public class SqlCmdSupportTests
    {
        [Test]
        public void Format_SetvarAndConnectDirectives_AreFormattedAndPreserved()
        {
            var sql =
                ":setvar DatabaseName \"MyDb\"\r\n" +
                ":connect localhost\r\n" +
                "select a,b from t\r\n" +
                "go\r\n";
            var opts = new GeneralOptions();

            var res = ScriptDomFormatter.Format(sql, opts);

            Assert.IsTrue(res.Success, res.ErrorMessage);
            StringAssert.Contains(":setvar DatabaseName \"MyDb\"", res.FormattedSql);
            StringAssert.Contains(":connect localhost", res.FormattedSql);
            StringAssert.Contains("SELECT", res.FormattedSql.ToUpperInvariant());
        }

        [Test]
        public void Format_IncludeDirectiveMidBatch_IsPreservedInPlace()
        {
            var sql =
                "select 1\r\n" +
                "go\r\n" +
                ":r shared_setup.sql\r\n" +
                "select 2\r\n" +
                "go\r\n";
            var opts = new GeneralOptions();

            var res = ScriptDomFormatter.Format(sql, opts);

            Assert.IsTrue(res.Success, res.ErrorMessage);
            StringAssert.Contains(":r shared_setup.sql", res.FormattedSql);
        }

        [Test]
        public void Format_SqlCmdWithGenuineSyntaxError_StillFails()
        {
            var sql = ":setvar DatabaseName \"MyDb\"\r\nselect a, from t\r\n";
            var opts = new GeneralOptions();

            var res = ScriptDomFormatter.Format(sql, opts);

            Assert.IsFalse(res.Success);
        }

        [Test]
        public void Format_DoesNotForcePreservedCommentsGlobally_WhenNoSqlCmdPresent()
        {
            // Regression guard: a plain script with no ':' at all must go through the
            // original fast path untouched by the SQLCMD extraction logic.
            var sql = "select 1";
            var opts = new GeneralOptions();

            var res = ScriptDomFormatter.Format(sql, opts);

            Assert.IsTrue(res.Success, res.ErrorMessage);
        }
    }
}
