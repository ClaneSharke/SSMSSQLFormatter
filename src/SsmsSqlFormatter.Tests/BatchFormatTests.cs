using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SsmsSqlFormatter;
using SsmsSqlFormatter.Options;

namespace SsmsSqlFormatter.Tests
{
    [TestFixture]
    public class BatchFormatTests
    {
        private List<string> _tempFiles;

        [SetUp]
        public void SetUp() => _tempFiles = new List<string>();

        [TearDown]
        public void TearDown()
        {
            foreach (var f in _tempFiles)
                if (File.Exists(f)) File.Delete(f);
        }

        private string WriteTempSql(string contents)
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sql");
            File.WriteAllText(path, contents);
            _tempFiles.Add(path);
            return path;
        }

        [Test]
        public void FormatFiles_ValidScript_IsFormattedAndCountedAsFormatted()
        {
            var path = WriteTempSql("select 1");
            var opts = new FormatterSettings();

            var result = Formatting.BatchFormatter.FormatFiles(new[] { path }, opts);

            Assert.AreEqual(1, result.FormattedCount);
            Assert.AreEqual(0, result.UnchangedCount);
            Assert.AreEqual(0, result.Failures.Count);
            StringAssert.Contains("SELECT", File.ReadAllText(path).ToUpperInvariant());
        }

        [Test]
        public void FormatFiles_AlreadyFormattedScript_IsCountedAsUnchangedAndFileNotTouched()
        {
            var opts = new FormatterSettings();
            var first = Formatting.BatchFormatter.FormatFiles(new[] { WriteTempSql("select 1") }, opts);
            var path = _tempFiles[0];
            var writeTimeBefore = File.GetLastWriteTimeUtc(path);

            System.Threading.Thread.Sleep(20);
            var second = Formatting.BatchFormatter.FormatFiles(new[] { path }, opts);

            Assert.AreEqual(1, second.UnchangedCount);
            Assert.AreEqual(0, second.FormattedCount);
            Assert.AreEqual(writeTimeBefore, File.GetLastWriteTimeUtc(path));
        }

        [Test]
        public void FormatFiles_UnparsableScript_IsReportedAsFailureAndFileLeftUntouched()
        {
            var path = WriteTempSql("select a, from t");
            var original = File.ReadAllText(path);
            var opts = new FormatterSettings();

            var result = Formatting.BatchFormatter.FormatFiles(new[] { path }, opts);

            Assert.AreEqual(0, result.FormattedCount);
            Assert.AreEqual(1, result.Failures.Count);
            Assert.AreEqual(original, File.ReadAllText(path));
        }

        [Test]
        public void FormatFiles_MissingFile_IsReportedAsFailureRatherThanThrowing()
        {
            var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sql");
            var opts = new FormatterSettings();

            var result = Formatting.BatchFormatter.FormatFiles(new[] { missingPath }, opts);

            Assert.AreEqual(1, result.Failures.Count);
        }

        [Test]
        public void FormatFiles_DryRun_ReportsWouldFormatButNeverWritesTheFile()
        {
            var path = WriteTempSql("select 1");
            var original = File.ReadAllText(path);
            var opts = new FormatterSettings();

            var result = Formatting.BatchFormatter.FormatFiles(new[] { path }, opts, dryRun: true);

            Assert.AreEqual(1, result.FormattedCount, "dry run should still count what WOULD change");
            Assert.AreEqual(original, File.ReadAllText(path), "dry run must never write to disk");
        }

        [Test]
        public void FormatFiles_DryRun_IsIdempotentAcrossRepeatedCalls()
        {
            var path = WriteTempSql("select 1");
            var opts = new FormatterSettings();

            var first = Formatting.BatchFormatter.FormatFiles(new[] { path }, opts, dryRun: true);
            var second = Formatting.BatchFormatter.FormatFiles(new[] { path }, opts, dryRun: true);

            Assert.AreEqual(1, first.FormattedCount);
            Assert.AreEqual(1, second.FormattedCount, "since dry run never writes, the file should still need formatting the second time too");
        }

        [Test]
        public void FormatFiles_MultipleFiles_ProcessesEachIndependently()
        {
            var good = WriteTempSql("select 1");
            var bad = WriteTempSql("select a, from t");
            var opts = new FormatterSettings();

            var result = Formatting.BatchFormatter.FormatFiles(new[] { good, bad }, opts);

            Assert.AreEqual(1, result.FormattedCount);
            Assert.AreEqual(1, result.Failures.Count);
        }
    }
}
