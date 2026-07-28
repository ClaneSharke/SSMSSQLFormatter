using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SsmsSqlFormatter.Formatting;

namespace SsmsSqlFormatter.Tests
{
    [TestFixture]
    public class CsvWriterTests
    {
        private string _path;

        [SetUp]
        public void SetUp() => _path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }

        [Test]
        public void Write_SimpleGrid_ProducesCommaSeparatedRows()
        {
            CsvWriter.Write(_path, new List<string> { "a\tb\r\n1\t2" }, ',', false);

            var text = File.ReadAllText(_path);
            StringAssert.Contains("a,b", text);
            StringAssert.Contains("1,2", text);
        }

        [Test]
        public void Write_ValueContainingSeparator_IsQuoted()
        {
            CsvWriter.Write(_path, new List<string> { "a,b\tc" }, ',', false);

            var text = File.ReadAllText(_path);
            StringAssert.Contains("\"a,b\"", text);
        }

        [Test]
        public void Write_ValueContainingQuote_IsEscapedByDoubling()
        {
            CsvWriter.Write(_path, new List<string> { "say \"hi\"\tb" }, ',', false);

            var text = File.ReadAllText(_path);
            StringAssert.Contains("\"say \"\"hi\"\"\"", text);
        }

        [Test]
        public void Write_NullsAsEmpty_ConvertsLiteralNullToEmptyCell()
        {
            CsvWriter.Write(_path, new List<string> { "a\tb\r\nNULL\tx" }, ',', true);

            var lines = File.ReadAllLines(_path);
            Assert.AreEqual(",x", lines[1]);
        }

        [Test]
        public void Write_NullsAsEmptyFalse_KeepsLiteralNullText()
        {
            CsvWriter.Write(_path, new List<string> { "a\tb\r\nNULL\tx" }, ',', false);

            var lines = File.ReadAllLines(_path);
            Assert.AreEqual("NULL,x", lines[1]);
        }

        [Test]
        public void Write_MultipleSheets_SeparatesWithBlankLine()
        {
            CsvWriter.Write(_path, new List<string> { "a\tb\r\n1\t2", "c\td\r\n3\t4" }, ',', false);

            var text = File.ReadAllText(_path);
            var firstSetEnd = text.IndexOf("1,2");
            var secondSetStart = text.IndexOf("c,d");
            Assert.Greater(secondSetStart, firstSetEnd);
        }
    }
}
