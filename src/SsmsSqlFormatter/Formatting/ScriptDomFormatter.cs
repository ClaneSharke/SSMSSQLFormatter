using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SsmsSqlFormatter.Options;

namespace SsmsSqlFormatter.Formatting
{
    public class FormatResult
    {
        public bool Success { get; set; }
        public string FormattedSql { get; set; }
        public string ErrorMessage { get; set; }
        public int CommentCount { get; set; }
    }

    /// <summary>
    /// Offline rule-based formatter built on Microsoft.SqlServer.TransactSql.ScriptDom —
    /// the same parser SQL Server tooling uses, so it understands all of T-SQL.
    /// </summary>
    public static class ScriptDomFormatter
    {
        public static FormatResult Format(string sql, GeneralOptions options)
        {
            var result = new FormatResult();
            try
            {
                var parser = new TSql160Parser(initialQuotedIdentifiers: true);
                TSqlFragment fragment;
                IList<ParseError> errors;
                using (var reader = new StringReader(sql))
                {
                    fragment = parser.Parse(reader, out errors);
                }

                if (errors != null && errors.Count > 0)
                {
                    var first = errors[0];
                    result.ErrorMessage =
                        $"The script could not be parsed, so it was left unchanged.\r\n\r\n" +
                        $"Line {first.Line}, column {first.Column}: {first.Message}" +
                        (errors.Count > 1 ? $"\r\n(+{errors.Count - 1} more error(s))" : string.Empty);
                    return result;
                }

                // Count comments so the caller can warn (ScriptDom regeneration can drop them).
                if (fragment.ScriptTokenStream != null)
                {
                    result.CommentCount = fragment.ScriptTokenStream.Count(t =>
                        t.TokenType == TSqlTokenType.SingleLineComment ||
                        t.TokenType == TSqlTokenType.MultilineComment);
                }

                var generator = new Sql160ScriptGenerator(BuildOptions(options));
                generator.GenerateScript(fragment, out string formatted);

                result.FormattedSql = formatted;
                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = "Unexpected formatter error: " + ex.Message;
                return result;
            }
        }

        private static SqlScriptGeneratorOptions BuildOptions(GeneralOptions o)
        {
            var g = new SqlScriptGeneratorOptions
            {
                SqlVersion = SqlVersion.Sql160,
                KeywordCasing = MapCasing(o.KeywordCasing),
                IndentationSize = Math.Max(1, o.IndentationSize),
                IncludeSemicolons = o.IncludeSemicolons
            };

            switch (o.Preset)
            {
                case StylePreset.Classic:
                    // Old-format: compact, minimal line breaks, uppercase keywords.
                    g.KeywordCasing = KeywordCasing.Uppercase;
                    g.AlignClauseBodies = false;
                    g.AlignColumnDefinitionFields = false;
                    g.AsKeywordOnOwnLine = false;
                    g.MultilineSelectElementsList = false;
                    g.MultilineWherePredicatesList = false;
                    g.MultilineInsertSourcesList = false;
                    g.MultilineInsertTargetsList = false;
                    g.MultilineViewColumnsList = false;
                    g.NewLineBeforeFromClause = true;
                    g.NewLineBeforeWhereClause = true;
                    g.NewLineBeforeJoinClause = false;
                    g.NewLineBeforeGroupByClause = true;
                    g.NewLineBeforeOrderByClause = true;
                    g.NewLineBeforeHavingClause = true;
                    g.NewLineBeforeOutputClause = false;
                    g.NewLineBeforeOpenParenthesisInMultilineList = false;
                    g.NewLineBeforeCloseParenthesisInMultilineList = false;
                    break;

                case StylePreset.Modern:
                    // New-format: everything expanded and aligned.
                    g.AlignClauseBodies = true;
                    g.AlignColumnDefinitionFields = true;
                    g.AsKeywordOnOwnLine = true;
                    g.MultilineSelectElementsList = true;
                    g.MultilineWherePredicatesList = true;
                    g.MultilineInsertSourcesList = true;
                    g.MultilineInsertTargetsList = true;
                    g.MultilineViewColumnsList = true;
                    g.NewLineBeforeFromClause = true;
                    g.NewLineBeforeWhereClause = true;
                    g.NewLineBeforeJoinClause = true;
                    g.NewLineBeforeGroupByClause = true;
                    g.NewLineBeforeOrderByClause = true;
                    g.NewLineBeforeHavingClause = true;
                    g.NewLineBeforeOutputClause = true;
                    g.NewLineBeforeOffsetClause = true;
                    g.NewLineBeforeOpenParenthesisInMultilineList = true;
                    g.NewLineBeforeCloseParenthesisInMultilineList = true;
                    g.IndentViewBody = true;
                    g.IndentSetClause = true;
                    break;

                case StylePreset.Custom:
                default:
                    g.AlignClauseBodies = o.AlignClauseBodies;
                    g.AlignColumnDefinitionFields = o.AlignColumnDefinitions;
                    g.AsKeywordOnOwnLine = o.AsKeywordOnOwnLine;
                    g.MultilineSelectElementsList = o.MultilineSelectList;
                    g.MultilineWherePredicatesList = o.MultilineWherePredicates;
                    g.MultilineInsertSourcesList = o.MultilineInsertLists;
                    g.MultilineInsertTargetsList = o.MultilineInsertLists;
                    g.NewLineBeforeFromClause = o.NewLineBeforeFrom;
                    g.NewLineBeforeWhereClause = o.NewLineBeforeWhere;
                    g.NewLineBeforeJoinClause = o.NewLineBeforeJoin;
                    g.NewLineBeforeGroupByClause = o.NewLineBeforeGroupBy;
                    g.NewLineBeforeOrderByClause = o.NewLineBeforeOrderBy;
                    g.NewLineBeforeHavingClause = o.NewLineBeforeHaving;
                    g.IndentViewBody = true;
                    g.IndentSetClause = true;
                    break;
            }

            return g;
        }

        private static KeywordCasing MapCasing(KeywordCase c)
        {
            switch (c)
            {
                case KeywordCase.Lowercase: return KeywordCasing.Lowercase;
                case KeywordCase.PascalCase: return KeywordCasing.PascalCase;
                default: return KeywordCasing.Uppercase;
            }
        }

        /// <summary>
        /// Builds a human-readable style guide from the general options,
        /// used to keep the AI engine consistent with the rule-based one.
        /// </summary>
        public static string DescribeStyle(GeneralOptions o)
        {
            if (o.Preset == StylePreset.Classic)
            {
                return "Classic compact style: UPPERCASE keywords, trailing commas, SELECT list on one line " +
                       "unless very long, FROM/WHERE/GROUP BY/ORDER BY each start a new line, JOINs stay inline " +
                       "with their table, minimal blank lines, indent " + o.IndentationSize + " spaces" +
                       (o.IncludeSemicolons ? ", terminate statements with semicolons." : ".");
            }

            if (o.Preset == StylePreset.Modern)
            {
                return "Modern expanded style: UPPERCASE keywords, each selected column on its own line, " +
                       "each JOIN and each ON condition on its own line, each AND/OR predicate in WHERE on its own line, " +
                       "clause bodies aligned and indented " + o.IndentationSize + " spaces" +
                       (o.IncludeSemicolons ? ", terminate statements with semicolons." : ".");
            }

            var parts = new List<string>
            {
                o.KeywordCasing + " keywords",
                "indent " + o.IndentationSize + " spaces"
            };
            if (o.MultilineSelectList) parts.Add("each selected column on its own line");
            if (o.MultilineWherePredicates) parts.Add("each AND/OR predicate on its own line");
            if (o.NewLineBeforeJoin) parts.Add("each JOIN on its own line");
            if (o.NewLineBeforeFrom) parts.Add("FROM on a new line");
            if (o.NewLineBeforeWhere) parts.Add("WHERE on a new line");
            if (o.NewLineBeforeGroupBy) parts.Add("GROUP BY on a new line");
            if (o.NewLineBeforeOrderBy) parts.Add("ORDER BY on a new line");
            if (o.IncludeSemicolons) parts.Add("terminate statements with semicolons");
            return "Custom style: " + string.Join(", ", parts) + ".";
        }
    }
}
