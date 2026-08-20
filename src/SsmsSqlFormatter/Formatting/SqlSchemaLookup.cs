using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace SsmsSqlFormatter.Formatting
{
    /// <summary>
    /// Resolves a table/view's ordered column list via a plain ADO.NET connection - the
    /// only piece of "Expand SELECT *" that ever talks to a database. Used by
    /// FormatSqlCommand.cs to back <see cref="SelectStarExpander.ExpandAsync"/>'s
    /// column-lookup delegate; the pure AST rewrite in SelectStarExpander never depends on
    /// this class directly; it's tested against a fake <see cref="ISchemaCatalog"/> instead.
    /// Any failure (unreachable server, table not found, no permission, timeout) returns
    /// null rather than throwing - "can't resolve this table" is a normal, expected outcome
    /// here, not an error.
    /// </summary>
    public static class SqlSchemaLookup
    {
        private const int CommandTimeoutSeconds = 5;

        /// <summary>
        /// Returns the ordered column list for database.schema.table (schema defaults to
        /// the connection's default schema when null; database defaults to the connection's
        /// current database when null), or null if it can't be resolved.
        /// </summary>
        public static async Task<List<string>> GetColumnsAsync(string connectionString, string database, string schema, string table)
        {
            if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(table)) return null;

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync().ConfigureAwait(false);

                    string catalogPrefix = string.IsNullOrEmpty(database) ? "" : "[" + database.Replace("]", "]]") + "].";
                    string sql =
                        $"SELECT TABLE_SCHEMA, COLUMN_NAME FROM {catalogPrefix}INFORMATION_SCHEMA.COLUMNS " +
                        "WHERE TABLE_NAME = @table AND (@schema IS NULL OR TABLE_SCHEMA = @schema) " +
                        "ORDER BY TABLE_SCHEMA, ORDINAL_POSITION";

                    using (var command = new SqlCommand(sql, connection) { CommandTimeout = CommandTimeoutSeconds })
                    {
                        command.Parameters.AddWithValue("@table", table);
                        command.Parameters.AddWithValue("@schema", (object)schema ?? System.DBNull.Value);

                        var columns = new List<string>();
                        string matchedSchema = null;
                        bool ambiguous = false;
                        using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync().ConfigureAwait(false))
                            {
                                string rowSchema = reader.GetString(0);
                                if (matchedSchema == null) matchedSchema = rowSchema;
                                else if (rowSchema != matchedSchema) ambiguous = true;
                                columns.Add(reader.GetString(1));
                            }
                        }

                        // Ambiguous (matched a same-named table in more than one schema when
                        // no schema was specified) or simply not found - either way, not
                        // confidently resolvable.
                        if (ambiguous || columns.Count == 0) return null;
                        return columns;
                    }
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
