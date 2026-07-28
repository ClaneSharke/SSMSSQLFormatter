using System;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using SsmsSqlFormatter.Formatting;

namespace SsmsSqlFormatter.Tests
{
    [TestFixture]
    public class XlsxWriterTests
    {
        private string _path;

        [SetUp]
        public void SetUp() => _path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }

        private static string ReadEntry(ZipArchive zip, string name)
        {
            var entry = zip.GetEntry(name);
            Assert.IsNotNull(entry, $"Expected zip entry '{name}' to exist.");
            using (var reader = new StreamReader(entry.Open()))
                return reader.ReadToEnd();
        }

        [Test]
        public void Write_ProducesAValidZipWithExpectedParts()
        {
            XlsxWriter.Write(_path, "a\tb\r\n1\t2", new ExcelStyle());

            using (var zip = ZipFile.OpenRead(_path))
            {
                Assert.IsNotNull(zip.GetEntry("[Content_Types].xml"));
                Assert.IsNotNull(zip.GetEntry("_rels/.rels"));
                Assert.IsNotNull(zip.GetEntry("xl/workbook.xml"));
                Assert.IsNotNull(zip.GetEntry("xl/styles.xml"));
                Assert.IsNotNull(zip.GetEntry("xl/worksheets/sheet1.xml"));
            }
        }

        [Test]
        public void Write_CellValues_AppearAsInlineStringsInSheetXml()
        {
            XlsxWriter.Write(_path, "Name\tAge\r\nAlice\t30", new ExcelStyle());

            using (var zip = ZipFile.OpenRead(_path))
            {
                var sheetXml = ReadEntry(zip, "xl/worksheets/sheet1.xml");
                StringAssert.Contains("<t xml:space=\"preserve\">Name</t>", sheetXml);
                StringAssert.Contains("<t xml:space=\"preserve\">Alice</t>", sheetXml);
            }
        }

        [Test]
        public void Write_XmlSpecialCharacters_AreEscaped()
        {
            XlsxWriter.Write(_path, "Col\r\nA & B < C", new ExcelStyle());

            using (var zip = ZipFile.OpenRead(_path))
            {
                var sheetXml = ReadEntry(zip, "xl/worksheets/sheet1.xml");
                StringAssert.Contains("A &amp; B &lt; C", sheetXml);
                StringAssert.DoesNotContain("A & B < C", sheetXml);
            }
        }

        [Test]
        public void WriteSheets_MultipleSheets_CreatesOneWorksheetPartEach()
        {
            var sheets = new System.Collections.Generic.List<XlsxSheet>
            {
                new XlsxSheet { Name = "First", Tsv = "a\r\n1" },
                new XlsxSheet { Name = "Second", Tsv = "b\r\n2" }
            };

            XlsxWriter.WriteSheets(_path, sheets, new ExcelStyle());

            using (var zip = ZipFile.OpenRead(_path))
            {
                Assert.IsNotNull(zip.GetEntry("xl/worksheets/sheet1.xml"));
                Assert.IsNotNull(zip.GetEntry("xl/worksheets/sheet2.xml"));
                var workbookXml = ReadEntry(zip, "xl/workbook.xml");
                StringAssert.Contains("First", workbookXml);
                StringAssert.Contains("Second", workbookXml);
            }
        }

        [Test]
        public void WriteSheets_PlainSheet_HasNoAutoFilter()
        {
            var sheets = new System.Collections.Generic.List<XlsxSheet>
            {
                new XlsxSheet { Name = "Query", Tsv = "SELECT 1", Plain = true }
            };

            XlsxWriter.WriteSheets(_path, sheets, new ExcelStyle());

            using (var zip = ZipFile.OpenRead(_path))
            {
                var sheetXml = ReadEntry(zip, "xl/worksheets/sheet1.xml");
                StringAssert.DoesNotContain("autoFilter", sheetXml);
            }
        }
    }
}
