using NUnit.Framework;
using SsmsSqlFormatter;

namespace SsmsSqlFormatter.Tests
{
    [TestFixture]
    public class FormatOnSaveDocTableEventsTests
    {
        [TestCase(@"C:\scripts\report.sql", true)]
        [TestCase(@"C:\scripts\report.SQL", true)]
        [TestCase(@"C:\scripts\SQLQuery1.sql", true)]
        [TestCase(@"C:\scripts\report.txt", false)]
        [TestCase(@"C:\scripts\report.sql.bak", false)]
        [TestCase(null, false)]
        [TestCase("", false)]
        public void IsSqlFile_MatchesOnExtensionOnly(string moniker, bool expected)
        {
            Assert.AreEqual(expected, FormatOnSaveDocTableEvents.IsSqlFile(moniker));
        }
    }
}
