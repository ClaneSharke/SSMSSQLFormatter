using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SsmsSqlFormatter.Formatting
{
    public class SelectStarExpandResult
    {
        /// <summary>The input SQL with every resolvable SELECT * / alias.* replaced by an
        /// explicit column list. Identical to the input when nothing was expandable.</summary>
        public string ExpandedSql { get; set; }
        public int ExpandedCount { get; set; }
        public int UnresolvedCount { get; set; }
    }

    /// <summary>
    /// Table/column schema lookup <see cref="SelectStarExpander"/> depends on. Production
    /// code backs this with a live database connection (see Options/SsmsConnectionDiscovery.cs
    /// and Formatting/SqlSchemaLookup.cs, both VSIX-only); tests back it with a fake
    /// in-memory catalog so the AST rewrite logic is fully testable without SSMS or a
    /// database. Column order must match the table's real ordinal column order.
    /// </summary>
    public interface ISchemaCatalog
    {
        /// <summary>Returns the ordered column list for a table/view, or null if it can't be resolved.</summary>
        List<string> TryGetColumns(string database, string schema, string table);
    }

    /// <summary>
    /// Expands SELECT * (and alias.*) into explicit, ordered column lists resolved from
    /// real table/view structure - wherever that structure is confidently known. Anything
    /// it can't resolve (a join to a CTE or derived table, an unknown table, a table with no
    /// columns reported) is left as SELECT * untouched; never guesses.
    ///
    /// Deliberately does not regenerate the script through ScriptDom's script generator -
    /// that would drop comments (the same reason ScriptDomFormatter needs its own comment
    /// reinjection), and this expander's output is meant to be fed into
    /// ScriptDomFormatter.Format afterwards as if it were hand-edited input. Instead it
    /// splices plain replacement text directly into the original source at each
    /// SelectStarExpression's own character span (TSqlFragment.StartOffset/FragmentLength),
    /// leaving every other character - including comments and whitespace - untouched.
    /// </summary>
    public static class SelectStarExpander
    {
        private class StarItem
        {
            public SelectStarExpression Star;
            public FromClause FromClause;
        }

        private class TableRef
        {
            public string Database;
            public string Schema;
            public string Table;
            public string EffectiveAlias;
        }

        private class SelectStarFinder : TSqlFragmentVisitor
        {
            public readonly List<StarItem> Items = new List<StarItem>();

            public override void ExplicitVisit(QuerySpecification node)
            {
                foreach (var elem in node.SelectElements)
                {
                    if (elem is SelectStarExpression star)
                        Items.Add(new StarItem { Star = star, FromClause = node.FromClause });
                }
                base.ExplicitVisit(node);
            }
        }

        private class CteNameCollector : TSqlFragmentVisitor
        {
            public readonly HashSet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public override void ExplicitVisit(CommonTableExpression node)
            {
                if (node.ExpressionName?.Value != null) Names.Add(node.ExpressionName.Value);
                base.ExplicitVisit(node);
            }
        }

        private class NamedTableCollector : TSqlFragmentVisitor
        {
            public readonly List<NamedTableReference> Tables = new List<NamedTableReference>();

            public override void ExplicitVisit(NamedTableReference node)
            {
                Tables.Add(node);
                base.ExplicitVisit(node);
            }
        }

        private class DictionarySchemaCatalog : ISchemaCatalog
        {
            private readonly Dictionary<(string, string, string), List<string>> _map =
                new Dictionary<(string, string, string), List<string>>();

            public void Add(string database, string schema, string table, List<string> columns) =>
                _map[Key(database, schema, table)] = columns;

            public List<string> TryGetColumns(string database, string schema, string table) =>
                _map.TryGetValue(Key(database, schema, table), out var cols) ? cols : null;

            private static (string, string, string) Key(string database, string schema, string table) =>
                ((database ?? "").ToUpperInvariant(), (schema ?? "").ToUpperInvariant(), (table ?? "").ToUpperInvariant());
        }

        /// <summary>
        /// Pure, synchronous rewrite given an already-populated schema catalog. This is the
        /// half that's unit-tested directly (see SelectStarExpanderTests.cs) - no connection,
        /// no I/O, no SSMS dependency.
        /// </summary>
        public static SelectStarExpandResult RewriteGivenSchema(string sql, ISchemaCatalog schema)
        {
            var result = new SelectStarExpandResult { ExpandedSql = sql };
            if (string.IsNullOrEmpty(sql) || schema == null) return result;

            var fragment = Parse(sql);
            if (fragment == null) return result;

            var cteNames = CollectCteNames(fragment);

            var starFinder = new SelectStarFinder();
            fragment.Accept(starFinder);
            if (starFinder.Items.Count == 0) return result;

            var replacements = new List<(int start, int length, string text)>();

            foreach (var item in starFinder.Items)
            {
                var leaves = new List<TableReference>();
                bool sawUnresolvableLeaf = false;
                CollectLeafTables(item.FromClause, leaves, ref sawUnresolvableLeaf);

                var candidateTables = new List<TableRef>();
                foreach (var leaf in leaves)
                {
                    if (!(leaf is NamedTableReference named)) { sawUnresolvableLeaf = true; continue; }
                    var so = named.SchemaObject;
                    string tableName = so?.BaseIdentifier?.Value;
                    if (string.IsNullOrEmpty(tableName)) { sawUnresolvableLeaf = true; continue; }
                    if (so.SchemaIdentifier == null && cteNames.Contains(tableName))
                    {
                        sawUnresolvableLeaf = true; // CTE reference, not a real table
                        continue;
                    }
                    candidateTables.Add(new TableRef
                    {
                        Database = so.DatabaseIdentifier?.Value,
                        Schema = so.SchemaIdentifier?.Value,
                        Table = tableName,
                        EffectiveAlias = named.Alias?.Value ?? tableName
                    });
                }

                string qualifierName = item.Star.Qualifier != null && item.Star.Qualifier.Identifiers.Count > 0
                    ? item.Star.Qualifier.Identifiers[item.Star.Qualifier.Identifiers.Count - 1].Value
                    : null;

                List<TableRef> tablesToExpand;
                if (qualifierName != null)
                {
                    var match = candidateTables.FirstOrDefault(t =>
                        string.Equals(t.EffectiveAlias, qualifierName, StringComparison.OrdinalIgnoreCase));
                    if (match == null) { result.UnresolvedCount++; continue; }
                    tablesToExpand = new List<TableRef> { match };
                }
                else
                {
                    if (sawUnresolvableLeaf || candidateTables.Count == 0) { result.UnresolvedCount++; continue; }
                    tablesToExpand = candidateTables;
                }

                bool qualifyColumns = tablesToExpand.Count > 1 || qualifierName != null;
                var columnParts = new List<string>();
                bool allResolved = true;
                foreach (var t in tablesToExpand)
                {
                    var cols = schema.TryGetColumns(t.Database, t.Schema, t.Table);
                    if (cols == null || cols.Count == 0) { allResolved = false; break; }
                    foreach (var c in cols)
                        columnParts.Add(qualifyColumns ? Bracket(t.EffectiveAlias) + "." + Bracket(c) : Bracket(c));
                }

                if (!allResolved || columnParts.Count == 0) { result.UnresolvedCount++; continue; }

                replacements.Add((item.Star.StartOffset, item.Star.FragmentLength, string.Join(", ", columnParts)));
                result.ExpandedCount++;
            }

            if (replacements.Count == 0) return result;

            var sb = new StringBuilder(sql);
            foreach (var r in replacements.OrderByDescending(r => r.start))
                sb.Remove(r.start, r.length).Insert(r.start, r.text);

            result.ExpandedSql = sb.ToString();
            return result;
        }

        /// <summary>
        /// Discovers which tables are actually referenced, resolves their columns through
        /// <paramref name="columnLookup"/> (backed by a live connection in production), and
        /// hands the resolved set to <see cref="RewriteGivenSchema"/>. Never touches
        /// <paramref name="columnLookup"/> at all when the script contains no SELECT * -
        /// no point discovering/using a connection for a script that doesn't need one.
        /// </summary>
        public static async Task<SelectStarExpandResult> ExpandAsync(
            string sql,
            Func<string, string, string, Task<List<string>>> columnLookup,
            CancellationToken cancellationToken)
        {
            var result = new SelectStarExpandResult { ExpandedSql = sql };
            if (string.IsNullOrEmpty(sql) || columnLookup == null) return result;

            var fragment = Parse(sql);
            if (fragment == null) return result;

            var starFinder = new SelectStarFinder();
            fragment.Accept(starFinder);
            if (starFinder.Items.Count == 0) return result;

            var cteNames = CollectCteNames(fragment);
            var tablesCollector = new NamedTableCollector();
            fragment.Accept(tablesCollector);

            var seen = new HashSet<(string, string, string)>();
            var catalog = new DictionarySchemaCatalog();
            foreach (var named in tablesCollector.Tables)
            {
                var so = named.SchemaObject;
                string tableName = so?.BaseIdentifier?.Value;
                if (string.IsNullOrEmpty(tableName)) continue;
                if (so.SchemaIdentifier == null && cteNames.Contains(tableName)) continue;

                string database = so.DatabaseIdentifier?.Value;
                string schemaName = so.SchemaIdentifier?.Value;
                var key = (database ?? "", schemaName ?? "", tableName);
                if (!seen.Add(key)) continue;

                cancellationToken.ThrowIfCancellationRequested();
                List<string> cols;
                try { cols = await columnLookup(database, schemaName, tableName).ConfigureAwait(false); }
                catch { cols = null; }
                if (cols != null && cols.Count > 0)
                    catalog.Add(database, schemaName, tableName, cols);
            }

            return RewriteGivenSchema(sql, catalog);
        }

        private static TSqlFragment Parse(string sql)
        {
            var parser = new TSql160Parser(initialQuotedIdentifiers: true);
            TSqlFragment fragment;
            IList<ParseError> errors;
            using (var reader = new StringReader(sql))
                fragment = parser.Parse(reader, out errors);
            if (fragment == null || (errors != null && errors.Count > 0)) return null;
            return fragment;
        }

        private static HashSet<string> CollectCteNames(TSqlFragment fragment)
        {
            var collector = new CteNameCollector();
            fragment.Accept(collector);
            return collector.Names;
        }

        private static void CollectLeafTables(FromClause fromClause, List<TableReference> leaves, ref bool sawUnresolvable)
        {
            if (fromClause?.TableReferences == null || fromClause.TableReferences.Count == 0)
            {
                sawUnresolvable = true;
                return;
            }
            foreach (var tr in fromClause.TableReferences)
                CollectLeaf(tr, leaves, ref sawUnresolvable);
        }

        private static void CollectLeaf(TableReference tr, List<TableReference> leaves, ref bool sawUnresolvable)
        {
            if (tr is JoinTableReference join)
            {
                CollectLeaf(join.FirstTableReference, leaves, ref sawUnresolvable);
                CollectLeaf(join.SecondTableReference, leaves, ref sawUnresolvable);
            }
            else if (tr is NamedTableReference)
            {
                leaves.Add(tr);
            }
            else
            {
                // Derived table, table-valued function, PIVOT/UNPIVOT, table variable,
                // OPENROWSET, etc. - none of these are resolvable via a table/column
                // metadata lookup, so any SELECT * depending on this leaf stays untouched.
                sawUnresolvable = true;
            }
        }

        private static string Bracket(string identifier) => "[" + identifier.Replace("]", "]]") + "]";
    }
}
