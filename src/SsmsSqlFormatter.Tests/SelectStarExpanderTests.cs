using System.Collections.Generic;
using NUnit.Framework;
using SsmsSqlFormatter.Formatting;

namespace SsmsSqlFormatter.Tests
{
    [TestFixture]
    public class SelectStarExpanderTests
    {
        private class FakeSchemaCatalog : ISchemaCatalog
        {
            private readonly Dictionary<(string, string, string, string), List<string>> _map =
                new Dictionary<(string, string, string, string), List<string>>();

            public FakeSchemaCatalog Add(string server, string database, string schema, string table, params string[] columns)
            {
                _map[Key(server, database, schema, table)] = new List<string>(columns);
                return this;
            }

            public List<string> TryGetColumns(string server, string database, string schema, string table) =>
                _map.TryGetValue(Key(server, database, schema, table), out var cols) ? cols : null;

            private static (string, string, string, string) Key(string server, string database, string schema, string table) =>
                ((server ?? "").ToUpperInvariant(), (database ?? "").ToUpperInvariant(), (schema ?? "").ToUpperInvariant(), (table ?? "").ToUpperInvariant());
        }

        [Test]
        public void SingleUnaliasedTable_ExpandsToUnqualifiedColumns()
        {
            var schema = new FakeSchemaCatalog().Add(null, null, "dbo", "Widgets", "Id", "Name", "CreatedDate");
            var sql = "SELECT * FROM dbo.Widgets";

            var result = SelectStarExpander.RewriteGivenSchema(sql, schema);

            Assert.AreEqual(1, result.ExpandedCount);
            Assert.AreEqual(0, result.UnresolvedCount);
            Assert.AreEqual("SELECT [Id], [Name], [CreatedDate] FROM dbo.Widgets", result.ExpandedSql);
        }

        [Test]
        public void SingleAliasedTable_BareStar_ExpandsUnqualified()
        {
            var schema = new FakeSchemaCatalog().Add(null, null, "dbo", "Widgets", "Id", "Name");
            var sql = "SELECT * FROM dbo.Widgets AS w";

            var result = SelectStarExpander.RewriteGivenSchema(sql, schema);

            Assert.AreEqual(1, result.ExpandedCount);
            Assert.AreEqual("SELECT [Id], [Name] FROM dbo.Widgets AS w", result.ExpandedSql);
        }

        [Test]
        public void MultiTableJoin_BareStar_QualifiesEachColumnWithItsAlias()
        {
            var schema = new FakeSchemaCatalog()
                .Add(null, null, "dbo", "Widgets", "Id", "Name")
                .Add(null, null, "dbo", "Orders", "Id", "WidgetId");
            var sql = "SELECT * FROM dbo.Widgets AS w JOIN dbo.Orders AS o ON o.WidgetId = w.Id";

            var result = SelectStarExpander.RewriteGivenSchema(sql, schema);

            Assert.AreEqual(1, result.ExpandedCount);
            StringAssert.Contains("[w].[Id], [w].[Name], [o].[Id], [o].[WidgetId]", result.ExpandedSql);
        }

        [Test]
        public void QualifiedStar_ExpandsOnlyThatTable_QualifiedWithItsAlias()
        {
            var schema = new FakeSchemaCatalog()
                .Add(null, null, "dbo", "Widgets", "Id", "Name")
                .Add(null, null, "dbo", "Orders", "Id", "WidgetId");
            var sql = "SELECT w.* FROM dbo.Widgets AS w JOIN dbo.Orders AS o ON o.WidgetId = w.Id";

            var result = SelectStarExpander.RewriteGivenSchema(sql, schema);

            Assert.AreEqual(1, result.ExpandedCount);
            Assert.AreEqual(
                "SELECT [w].[Id], [w].[Name] FROM dbo.Widgets AS w JOIN dbo.Orders AS o ON o.WidgetId = w.Id",
                result.ExpandedSql);
        }

        [Test]
        public void QualifiedStar_ResolvesIndependently_EvenWhenSiblingJoinedTableIsUnresolvable()
        {
            // Orders isn't in the catalog at all - w.* must still expand.
            var schema = new FakeSchemaCatalog().Add(null, null, "dbo", "Widgets", "Id", "Name");
            var sql = "SELECT w.* FROM dbo.Widgets AS w JOIN dbo.Orders AS o ON o.WidgetId = w.Id";

            var result = SelectStarExpander.RewriteGivenSchema(sql, schema);

            Assert.AreEqual(1, result.ExpandedCount);
            StringAssert.Contains("SELECT [w].[Id], [w].[Name] FROM", result.ExpandedSql);
        }

        [Test]
        public void UnknownTable_LeavesStarUntouched()
        {
            var schema = new FakeSchemaCatalog(); // empty catalog
            var sql = "SELECT * FROM dbo.Widgets";

            var result = SelectStarExpander.RewriteGivenSchema(sql, schema);

            Assert.AreEqual(0, result.ExpandedCount);
            Assert.AreEqual(1, result.UnresolvedCount);
            Assert.AreEqual(sql, result.ExpandedSql);
        }

        [Test]
        public void CteReference_LeavesStarUntouched()
        {
            var schema = new FakeSchemaCatalog().Add(null, null, "dbo", "Widgets", "Id", "Name");
            var sql = "WITH x AS (SELECT * FROM dbo.Widgets) SELECT * FROM x";

            var result = SelectStarExpander.RewriteGivenSchema(sql, schema);

            // The inner "SELECT * FROM dbo.Widgets" is resolvable; the outer "SELECT * FROM x"
            // is not, because x is a CTE, not a real table.
            Assert.AreEqual(1, result.ExpandedCount);
            Assert.AreEqual(1, result.UnresolvedCount);
            StringAssert.Contains("WITH x AS (SELECT [Id], [Name] FROM dbo.Widgets) SELECT * FROM x", result.ExpandedSql);
        }

        [Test]
        public void DerivedTable_LeavesStarUntouched()
        {
            var schema = new FakeSchemaCatalog().Add(null, null, "dbo", "Widgets", "Id", "Name");
            var sql = "SELECT * FROM (SELECT * FROM dbo.Widgets) AS x";

            var result = SelectStarExpander.RewriteGivenSchema(sql, schema);

            // Inner star (real base table) resolves; outer star (derived table source) does not.
            Assert.AreEqual(1, result.ExpandedCount);
            Assert.AreEqual(1, result.UnresolvedCount);
            StringAssert.Contains("SELECT * FROM (SELECT [Id], [Name] FROM dbo.Widgets) AS x", result.ExpandedSql);
        }

        [Test]
        public void MixedScript_ResolvableAndUnresolvableStarsHandledIndependently()
        {
            var schema = new FakeSchemaCatalog().Add(null, null, "dbo", "Widgets", "Id", "Name");
            var sql = "SELECT * FROM dbo.Widgets; SELECT * FROM dbo.Unknown;";

            var result = SelectStarExpander.RewriteGivenSchema(sql, schema);

            Assert.AreEqual(1, result.ExpandedCount);
            Assert.AreEqual(1, result.UnresolvedCount);
            StringAssert.Contains("SELECT [Id], [Name] FROM dbo.Widgets", result.ExpandedSql);
            StringAssert.Contains("SELECT * FROM dbo.Unknown", result.ExpandedSql);
        }

        [Test]
        public void NoStarInScript_ReturnsInputUnchanged()
        {
            var schema = new FakeSchemaCatalog().Add(null, null, "dbo", "Widgets", "Id", "Name");
            var sql = "SELECT Id, Name FROM dbo.Widgets";

            var result = SelectStarExpander.RewriteGivenSchema(sql, schema);

            Assert.AreEqual(0, result.ExpandedCount);
            Assert.AreEqual(0, result.UnresolvedCount);
            Assert.AreEqual(sql, result.ExpandedSql);
        }

        [Test]
        public void LinkedServerFourPartName_ExpandsUsingRemoteServerColumns()
        {
            var schema = new FakeSchemaCatalog().Add("LinkedSrv", "RemoteDb", "dbo", "Widgets", "Id", "Name");
            var sql = "SELECT * FROM LinkedSrv.RemoteDb.dbo.Widgets";

            var result = SelectStarExpander.RewriteGivenSchema(sql, schema);

            Assert.AreEqual(1, result.ExpandedCount);
            Assert.AreEqual(0, result.UnresolvedCount);
            Assert.AreEqual("SELECT [Id], [Name] FROM LinkedSrv.RemoteDb.dbo.Widgets", result.ExpandedSql);
        }

        [Test]
        public void LinkedServerReference_NotInCatalog_LeavesStarUntouched()
        {
            // Simulates a linked server that isn't SQL Server (or is unreachable, or lacks
            // permission) - the lookup simply never produces an entry for it, same as any
            // other unresolvable table.
            var schema = new FakeSchemaCatalog();
            var sql = "SELECT * FROM LinkedSrv.RemoteDb.dbo.Widgets";

            var result = SelectStarExpander.RewriteGivenSchema(sql, schema);

            Assert.AreEqual(0, result.ExpandedCount);
            Assert.AreEqual(1, result.UnresolvedCount);
            Assert.AreEqual(sql, result.ExpandedSql);
        }

        [Test]
        public void LocalAndLinkedServerJoin_QualifiesEachColumnWithItsAlias()
        {
            var schema = new FakeSchemaCatalog()
                .Add(null, null, "dbo", "Widgets", "Id", "Name")
                .Add("LinkedSrv", "RemoteDb", "dbo", "Orders", "Id", "WidgetId");
            var sql = "SELECT * FROM dbo.Widgets AS w JOIN LinkedSrv.RemoteDb.dbo.Orders AS o ON o.WidgetId = w.Id";

            var result = SelectStarExpander.RewriteGivenSchema(sql, schema);

            Assert.AreEqual(1, result.ExpandedCount);
            StringAssert.Contains("[w].[Id], [w].[Name], [o].[Id], [o].[WidgetId]", result.ExpandedSql);
        }

        [Test]
        public void QualifiedStarOnLinkedServerTable_ResolvesIndependently()
        {
            // The local table isn't in the catalog at all - o.* (the linked-server table)
            // must still expand on its own.
            var schema = new FakeSchemaCatalog().Add("LinkedSrv", "RemoteDb", "dbo", "Orders", "Id", "WidgetId");
            var sql = "SELECT o.* FROM dbo.Widgets AS w JOIN LinkedSrv.RemoteDb.dbo.Orders AS o ON o.WidgetId = w.Id";

            var result = SelectStarExpander.RewriteGivenSchema(sql, schema);

            Assert.AreEqual(1, result.ExpandedCount);
            Assert.AreEqual(
                "SELECT [o].[Id], [o].[WidgetId] FROM dbo.Widgets AS w JOIN LinkedSrv.RemoteDb.dbo.Orders AS o ON o.WidgetId = w.Id",
                result.ExpandedSql);
        }
    }
}
