using System.Linq;
using NUnit.Framework;
using SsmsSqlFormatter.Formatting;

namespace SsmsSqlFormatter.Tests
{
    [TestFixture]
    public class LineDiffTests
    {
        [Test]
        public void Compute_IdenticalText_IsAllEqual()
        {
            var diff = LineDiff.Compute("select 1\r\nselect 2", "select 1\r\nselect 2");
            Assert.IsTrue(diff.All(d => d.Op == DiffOp.Equal));
            Assert.AreEqual(2, diff.Count);
        }

        [Test]
        public void Compute_OneLineChanged_ReportsDeleteThenInsert()
        {
            var original = "SELECT a\r\nFROM t";
            var formatted = "SELECT A\r\nFROM t";
            var diff = LineDiff.Compute(original, formatted);

            Assert.AreEqual(3, diff.Count);
            Assert.AreEqual(DiffOp.Delete, diff[0].Op);
            Assert.AreEqual("SELECT a", diff[0].Text);
            Assert.AreEqual(DiffOp.Insert, diff[1].Op);
            Assert.AreEqual("SELECT A", diff[1].Text);
            Assert.AreEqual(DiffOp.Equal, diff[2].Op);
            Assert.AreEqual("FROM t", diff[2].Text);
        }

        [Test]
        public void Compute_LineInserted_KeepsSurroundingLinesEqual()
        {
            var original = "SELECT a\r\nFROM t";
            var formatted = "SELECT a\r\nWHERE 1 = 1\r\nFROM t";
            var diff = LineDiff.Compute(original, formatted);

            Assert.AreEqual(DiffOp.Equal, diff[0].Op);
            Assert.AreEqual("SELECT a", diff[0].Text);
            Assert.IsTrue(diff.Any(d => d.Op == DiffOp.Insert && d.Text == "WHERE 1 = 1"));
            Assert.AreEqual(DiffOp.Equal, diff.Last().Op);
            Assert.AreEqual("FROM t", diff.Last().Text);
        }

        [Test]
        public void Compute_LineRemoved_ReportsOnlyDeleteForThatLine()
        {
            var original = "SELECT a\r\nWHERE 1 = 1\r\nFROM t";
            var formatted = "SELECT a\r\nFROM t";
            var diff = LineDiff.Compute(original, formatted);

            Assert.IsTrue(diff.Any(d => d.Op == DiffOp.Delete && d.Text == "WHERE 1 = 1"));
            Assert.IsFalse(diff.Any(d => d.Op == DiffOp.Insert));
        }

        [Test]
        public void Compute_EmptyOriginal_IsAllInsert()
        {
            var diff = LineDiff.Compute("", "a\r\nb");
            Assert.IsTrue(diff.All(d => d.Op != DiffOp.Delete));
            Assert.IsTrue(diff.Any(d => d.Op == DiffOp.Insert && d.Text == "a"));
            Assert.IsTrue(diff.Any(d => d.Op == DiffOp.Insert && d.Text == "b"));
        }

        [Test]
        public void Compute_EmptyFormatted_IsAllDelete()
        {
            var diff = LineDiff.Compute("a\r\nb", "");
            Assert.IsTrue(diff.All(d => d.Op != DiffOp.Insert));
        }

        [Test]
        public void Compute_ReconstructsBothSidesFromTheDiff()
        {
            var original = "one\r\ntwo\r\nthree\r\nfour";
            var formatted = "one\r\nTWO\r\nthree\r\nfive";
            var diff = LineDiff.Compute(original, formatted);

            var reconstructedOriginal = string.Join("\r\n",
                diff.Where(d => d.Op != DiffOp.Insert).Select(d => d.Text));
            var reconstructedFormatted = string.Join("\r\n",
                diff.Where(d => d.Op != DiffOp.Delete).Select(d => d.Text));

            Assert.AreEqual(original, reconstructedOriginal);
            Assert.AreEqual(formatted, reconstructedFormatted);
        }
    }
}
