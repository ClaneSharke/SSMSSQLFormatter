using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.Runtime.Caching;
using System.Security.Cryptography;
using System.Reflection;
using Newtonsoft.Json;
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
        private static readonly MemoryCache _formatCache = MemoryCache.Default;
        private static readonly object _cacheLock = new object();

        /// <summary>
        /// Matches a SQLCMD-mode directive line (:setvar, :r, :connect, :on error, :!!, ...).
        /// T-SQL statements never begin with a bare colon, so this is unambiguous.
        /// </summary>
        private static readonly Regex SqlCmdLineRegex =
            new Regex(@"^[ \t]*:(?:!!|[A-Za-z]+)\b[^\r\n]*", RegexOptions.Multiline | RegexOptions.Compiled);

        private const string SqlCmdMarkerPrefix = "§SQLCMD#";
        private const string SqlCmdMarkerSuffix = "§";

        public static FormatResult Format(string sql, GeneralOptions options)
        {
            if (options != null && !string.IsNullOrEmpty(sql) && sql.IndexOf(':') >= 0)
            {
                string prepared = ExtractSqlCmdLines(sql, out List<string> sqlCmdLines);
                if (sqlCmdLines.Count > 0)
                    return FormatWithSqlCmdLines(prepared, sqlCmdLines, options);
            }

            return FormatCore(sql, options);
        }

        /// <summary>
        /// Formats a script that contains SQLCMD-mode directives. ScriptDom cannot parse
        /// those lines at all, so they are replaced with marker comments before parsing
        /// and spliced back in verbatim afterwards. Comment preservation is forced on for
        /// this call regardless of the "Preserve comments" setting, because the markers
        /// travel through formatting as comments - unlike a decorative comment, losing one
        /// would silently corrupt the script (it would no longer run under sqlcmd/SSMS).
        /// </summary>
        private static FormatResult FormatWithSqlCmdLines(string prepared, List<string> sqlCmdLines, GeneralOptions options)
        {
            var innerOptions = CloneWithPreserveComments(options, true);
            var result = FormatCore(prepared, innerOptions);
            if (!result.Success) return result;

            result.FormattedSql = ReinsertSqlCmdLines(result.FormattedSql, sqlCmdLines);
            result.CommentCount = Math.Max(0, result.CommentCount - sqlCmdLines.Count);
            return result;
        }

        private static string ExtractSqlCmdLines(string sql, out List<string> sqlCmdLines)
        {
            var lines = new List<string>();
            string prepared = SqlCmdLineRegex.Replace(sql, m =>
            {
                lines.Add(m.Value);
                return "--" + SqlCmdMarkerPrefix + (lines.Count - 1) + SqlCmdMarkerSuffix;
            });
            sqlCmdLines = lines;
            return prepared;
        }

        /// <summary>
        /// Splices each SQLCMD directive back into its marker's position, verbatim. A
        /// marker that can't be found (e.g. the generator failed to round-trip that
        /// comment) is never silently dropped - it's appended at the end under a banner
        /// instead, matching how lost comments are handled elsewhere.
        /// </summary>
        private static string ReinsertSqlCmdLines(string formatted, List<string> sqlCmdLines)
        {
            var lost = new List<string>();
            for (int i = 0; i < sqlCmdLines.Count; i++)
            {
                string marker = "--" + SqlCmdMarkerPrefix + i + SqlCmdMarkerSuffix;
                int idx = formatted.IndexOf(marker, StringComparison.Ordinal);
                if (idx < 0)
                {
                    lost.Add(sqlCmdLines[i]);
                    continue;
                }

                int lineStart = formatted.LastIndexOf('\n', Math.Max(0, idx - 1)) + 1;
                int lineEnd = formatted.IndexOf('\n', idx);
                if (lineEnd < 0) lineEnd = formatted.Length;
                int cut = lineEnd;
                if (cut > lineStart && formatted[cut - 1] == '\r') cut--;

                formatted = formatted.Substring(0, lineStart) + sqlCmdLines[i] + formatted.Substring(cut);
            }

            if (lost.Count > 0)
            {
                formatted += "\r\n\r\n-- [SQL Formatter] the following SQLCMD directive(s) could not be " +
                             "repositioned automatically - move them back into place manually:";
                foreach (var line in lost) formatted += "\r\n" + line;
            }

            return formatted;
        }

        /// <summary>Shallow copy of a GeneralOptions instance with one property overridden - never mutates the source.</summary>
        private static GeneralOptions CloneWithPreserveComments(GeneralOptions source, bool preserveComments)
        {
            var clone = new GeneralOptions();
            foreach (var prop in typeof(GeneralOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || !prop.CanWrite) continue;
                try { prop.SetValue(clone, prop.GetValue(source)); } catch { /* skip */ }
            }
            clone.PreserveComments = preserveComments;
            return clone;
        }

        private static FormatResult FormatCore(string sql, GeneralOptions options)
        {
            if (options != null && options.EnableFormattingCache && !string.IsNullOrEmpty(sql))
            {
                try
                {
                    var sig = ComputeSignature(sql, options);
                    if (_formatCache.Contains(sig))
                    {
                        var cached = _formatCache.Get(sig) as FormatResult;
                        if (cached != null)
                        {
                            return new FormatResult
                            {
                                Success = cached.Success,
                                FormattedSql = cached.FormattedSql,
                                ErrorMessage = cached.ErrorMessage,
                                CommentCount = cached.CommentCount
                            };
                        }
                    }
                }
                catch { /* caching best-effort */ }
            }

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

                if (options.PreserveComments && result.CommentCount > 0)
                    formatted = ReinjectComments(sql, formatted);

                formatted = PostProcess(formatted, options);

                result.FormattedSql = formatted;
                result.Success = true;

                if (options != null && options.EnableFormattingCache && !string.IsNullOrEmpty(sql))
                {
                    try
                    {
                        var sig = ComputeSignature(sql, options);
                        var policy = new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(30) };
                        _formatCache.Set(sig, new FormatResult
                        {
                            Success = result.Success,
                            FormattedSql = result.FormattedSql,
                            ErrorMessage = result.ErrorMessage,
                            CommentCount = result.CommentCount
                        }, policy);
                    }
                    catch { /* ignore cache errors */ }
                }

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
                    g.MultilineViewColumnsList = o.MultilineViewColumns;
                    g.NewLineBeforeFromClause = o.NewLineBeforeFrom;
                    g.NewLineBeforeWhereClause = o.NewLineBeforeWhere;
                    g.NewLineBeforeJoinClause = o.NewLineBeforeJoin;
                    g.NewLineBeforeGroupByClause = o.NewLineBeforeGroupBy;
                    g.NewLineBeforeOrderByClause = o.NewLineBeforeOrderBy;
                    g.NewLineBeforeHavingClause = o.NewLineBeforeHaving;
                    g.NewLineBeforeOutputClause = o.NewLineBeforeOutput;
                    g.NewLineBeforeOffsetClause = o.NewLineBeforeOffset;
                    g.NewLineBeforeOpenParenthesisInMultilineList = o.NewLineBeforeOpenParen;
                    g.NewLineBeforeCloseParenthesisInMultilineList = o.NewLineBeforeCloseParen;
                    g.IndentViewBody = o.IndentViewBody;
                    g.IndentSetClause = o.IndentSetClause;
                    break;
            }

            return g;
        }

        private static string ComputeSignature(string sql, GeneralOptions o)
        {
            // Build a stable signature from the SQL and the relevant option properties.
            var sb = new StringBuilder();
            sb.Append(sql).Append("||");
            foreach (var prop in typeof(GeneralOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead) continue;
                object val;
                try { val = prop.GetValue(o); } catch { val = null; }
                if (val is System.Drawing.Color c) sb.Append(prop.Name).Append("=").Append(c.ToArgb()).Append(";");
                else if (val != null) sb.Append(prop.Name).Append("=").Append(val.ToString()).Append(";");
                else sb.Append(prop.Name).Append("=null;");
            }

            // Hash the signature to keep keys small
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                var hash = sha.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }

        private class OrigItem
        {
            public bool IsComment;
            public string Text;
            public bool OwnLine;
        }

        /// <summary>
        /// Puts the original script's comments back into the freshly formatted text.
        /// Walks both token streams in parallel (case-insensitive, tolerant of
        /// added/removed semicolons), attaching each comment to the same code it
        /// preceded in the original: trailing comments stay at line ends, own-line
        /// comments get their own line at the current indentation. If alignment
        /// fails, ALL comments are appended under a banner - never silently dropped.
        /// </summary>
        private static string ReinjectComments(string originalSql, string formattedSql)
        {
            var parser = new TSql160Parser(initialQuotedIdentifiers: true);
            TSqlFragment origFrag, fmtFrag;
            IList<ParseError> err;
            using (var r = new StringReader(originalSql)) origFrag = parser.Parse(r, out err);
            using (var r = new StringReader(formattedSql)) fmtFrag = parser.Parse(r, out err);
            if (origFrag?.ScriptTokenStream == null || fmtFrag?.ScriptTokenStream == null)
                return formattedSql;

            // Original stream reduced to code tokens + comments (with own-line flag).
            var items = new List<OrigItem>();
            bool newlineSinceCode = true;
            foreach (var t in origFrag.ScriptTokenStream)
            {
                if (t.TokenType == TSqlTokenType.EndOfFile) continue;
                if (t.TokenType == TSqlTokenType.WhiteSpace)
                {
                    if (t.Text != null && t.Text.IndexOf('\n') >= 0) newlineSinceCode = true;
                }
                else if (t.TokenType == TSqlTokenType.SingleLineComment ||
                         t.TokenType == TSqlTokenType.MultilineComment)
                {
                    items.Add(new OrigItem { IsComment = true, Text = t.Text, OwnLine = newlineSinceCode });
                }
                else
                {
                    items.Add(new OrigItem { Text = t.Text });
                    newlineSinceCode = false;
                }
            }
            if (!items.Exists(i => i.IsComment)) return formattedSql;

            var sb = new StringBuilder(formattedSql.Length + 256);
            string ws = "";
            int oi = 0;
            var lost = new List<string>();

            string IndentOf(string w)
            {
                int i = w.LastIndexOf('\n');
                return i >= 0 ? w.Substring(i + 1) : "";
            }

            void EmitPendingComments()
            {
                bool ownLineMode = false;
                string indent = IndentOf(ws);
                while (oi < items.Count && items[oi].IsComment)
                {
                    var c = items[oi];
                    oi++;
                    if (!c.OwnLine && !ownLineMode && sb.Length > 0)
                    {
                        sb.Append(' ').Append(c.Text);   // trailing: stays on previous line
                    }
                    else
                    {
                        if (!ownLineMode)
                        {
                            if (sb.Length == 0) { /* very start of output */ }
                            else if (ws.IndexOf('\n') >= 0) sb.Append(ws);
                            else sb.Append(ws).Append('\n').Append(indent);
                            ws = "";
                            ownLineMode = true;
                            sb.Append(c.Text);
                        }
                        else
                        {
                            sb.Append('\n').Append(indent).Append(c.Text);
                        }
                    }
                }
                if (ownLineMode)
                {
                    sb.Append('\n').Append(indent);
                    ws = "";
                }
            }

            string BannerFallback()
            {
                var rest = new StringBuilder();
                for (int m = oi; m < items.Count; m++)
                    if (items[m].IsComment) rest.Append('\n').Append(items[m].Text);
                foreach (var lc in lost) rest.Append('\n').Append(lc);
                if (rest.Length == 0) return formattedSql;
                return formattedSql + "\n\n-- [SQL Formatter] comments from the original script:" + rest;
            }

            foreach (var tok in fmtFrag.ScriptTokenStream)
            {
                if (tok.TokenType == TSqlTokenType.EndOfFile) continue;
                if (tok.TokenType == TSqlTokenType.WhiteSpace)
                {
                    ws += tok.Text ?? "";
                    continue;
                }

                // Comments after a statement should land after its semicolon.
                if (tok.TokenType != TSqlTokenType.Semicolon) EmitPendingComments();

                if (oi < items.Count && !items[oi].IsComment)
                {
                    string expected = items[oi].Text ?? "";
                    string current = tok.Text ?? "";
                    if (string.Equals(current, expected, StringComparison.OrdinalIgnoreCase))
                    {
                        oi++;
                    }
                    else if (tok.TokenType == TSqlTokenType.Semicolon)
                    {
                        // generator-added semicolon: no counterpart in the original
                    }
                    else if (expected == ";")
                    {
                        oi++;  // original semicolon not emitted by the generator
                        if (oi < items.Count && !items[oi].IsComment &&
                            string.Equals(current, items[oi].Text ?? "", StringComparison.OrdinalIgnoreCase))
                            oi++;
                    }
                    else
                    {
                        // Try to resync within a small window.
                        int k = oi, hops = 0;
                        bool found = false;
                        while (k < items.Count && hops < 4)
                        {
                            if (!items[k].IsComment)
                            {
                                hops++;
                                if (string.Equals(items[k].Text ?? "", current, StringComparison.OrdinalIgnoreCase))
                                {
                                    found = true;
                                    break;
                                }
                            }
                            k++;
                        }
                        if (found)
                        {
                            for (int m = oi; m < k; m++)
                                if (items[m].IsComment) lost.Add(items[m].Text);
                            oi = k + 1;
                        }
                        else
                        {
                            return BannerFallback();
                        }
                    }
                }

                sb.Append(ws);
                ws = "";
                sb.Append(tok.Text);
            }

            EmitPendingComments();
            sb.Append(ws);
            for (; oi < items.Count; oi++)
                if (items[oi].IsComment) sb.Append('\n').Append(items[oi].Text);
            foreach (var lc in lost) sb.Append('\n').Append(lc);
            return sb.ToString();
        }

        /// <summary>
        /// Style transforms that ScriptDom's generator cannot do natively:
        /// leading commas and tab-based indentation.
        /// </summary>
        private static string PostProcess(string sql, GeneralOptions options)
        {
            if (options.TrimTrailingWhitespace || options.MaxConsecutiveBlankLines >= 0)
                sql = ApplyWhitespacePolicy(sql, options.TrimTrailingWhitespace, options.MaxConsecutiveBlankLines);

            if (options.BlankLinesBeforeGo >= 0 && options.BlankLinesAfterGo >= 0)
                sql = NormalizeGoSpacing(sql, options.BlankLinesBeforeGo, options.BlankLinesAfterGo);

            if (options.BlankLinesBetweenStatements >= 0)
                sql = NormalizeStatementSpacing(sql, options.BlankLinesBetweenStatements);

            if (options.ReindentSubqueries)
                sql = ReindentSubqueries(sql, Math.Max(1, options.IndentationSize));

            if (options.FunctionCasing != IdentifierCase.Unchanged ||
                options.DataTypeCasing != IdentifierCase.Unchanged)
                sql = ApplyIdentifierCasing(sql, options.FunctionCasing, options.DataTypeCasing);

            if (options.MaxLineLength > 0)
                sql = WrapLongLines(sql, options.MaxLineLength);

            if (options.Commas == CommaPlacement.Leading)
                sql = MoveCommasToLineStart(sql);

            if (options.AlignSetClauseAssignments)
                sql = AlignAssignments(sql);

            if (options.UseTabsForIndentation)
                sql = ConvertIndentToTabs(sql, Math.Max(1, options.IndentationSize));

            return sql;
        }

        /// <summary>Collects every <see cref="StatementList"/> node in a script - the AST
        /// building block used for BEGIN...END bodies, IF/WHILE branches, TRY/CATCH
        /// bodies, and procedure/function/trigger bodies alike - so blank-line spacing
        /// can be applied uniformly at every nesting level, not just directly inside a batch.</summary>
        private class StatementListCollector : TSqlFragmentVisitor
        {
            public readonly List<StatementList> Lists = new List<StatementList>();
            public override void ExplicitVisit(StatementList node)
            {
                Lists.Add(node);
                base.ExplicitVisit(node);
            }
        }

        /// <summary>
        /// Sets an exact number of blank lines between consecutive statements, at every
        /// nesting level (top-level batch statements, and statements inside BEGIN...END,
        /// IF/WHILE bodies, TRY/CATCH bodies, and procedure/function/trigger bodies).
        /// Trailing comments stay on their statement's line; a comment block above a
        /// statement stays attached to it, with the blank lines above.
        /// </summary>
        private static string NormalizeStatementSpacing(string sql, int blankBetween)
        {
            var parser = new TSql160Parser(initialQuotedIdentifiers: true);
            TSqlFragment frag;
            IList<ParseError> errors;
            using (var reader = new StringReader(sql))
            {
                frag = parser.Parse(reader, out errors);
            }
            var script = frag as TSqlScript;
            if (script?.ScriptTokenStream == null || (errors != null && errors.Count > 0))
                return sql;

            var toks = script.ScriptTokenStream;
            var replacements = new Dictionary<int, string>();

            string NewLines(int blanks)
            {
                var r = new StringBuilder();
                for (int k = 0; k < blanks + 1; k++) r.Append("\r\n");
                return r.ToString();
            }

            void ProcessList(IList<TSqlStatement> statements)
            {
                for (int s = 0; s < statements.Count - 1; s++)
                {
                    int i = statements[s].LastTokenIndex + 1;

                    // Skip past the terminator and anything that should stay on the
                    // statement's own line: semicolon, same-line whitespace, and
                    // trailing comments.
                    bool progressed = true;
                    while (i < toks.Count && progressed)
                    {
                        progressed = false;
                        if (toks[i].TokenType == TSqlTokenType.WhiteSpace &&
                            (toks[i].Text ?? "").IndexOf('\n') < 0)
                        {
                            i++; progressed = true; continue;
                        }
                        if (toks[i].TokenType == TSqlTokenType.Semicolon ||
                            toks[i].TokenType == TSqlTokenType.SingleLineComment ||
                            toks[i].TokenType == TSqlTokenType.MultilineComment)
                        {
                            i++; progressed = true; continue;
                        }
                    }

                    if (i < toks.Count && toks[i].TokenType == TSqlTokenType.WhiteSpace)
                    {
                        string text = toks[i].Text ?? "";
                        if (text.IndexOf('\n') >= 0)
                        {
                            string tail = text.Substring(text.LastIndexOf('\n') + 1);
                            replacements[i] = NewLines(blankBetween) + tail;
                        }
                        else
                        {
                            replacements[i] = NewLines(blankBetween);
                        }
                    }
                }
            }

            foreach (var batch in script.Batches)
                ProcessList(batch.Statements);

            var collector = new StatementListCollector();
            script.Accept(collector);
            foreach (var list in collector.Lists)
                ProcessList(list.Statements);

            if (replacements.Count == 0) return sql;

            var sb = new StringBuilder(sql.Length + replacements.Count * 4);
            for (int i = 0; i < toks.Count; i++)
            {
                if (toks[i].TokenType == TSqlTokenType.EndOfFile) continue;
                sb.Append(replacements.TryGetValue(i, out string repl) ? repl : (toks[i].Text ?? ""));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Ensures every line inside parentheses (subqueries, derived tables, IN lists)
        /// is indented at least one level per nesting depth. Token-aware, so parentheses
        /// inside strings and comments don't count. Never removes indentation.
        /// </summary>
        private static string ReindentSubqueries(string sql, int indentSize)
        {
            var parser = new TSql160Parser(initialQuotedIdentifiers: true);
            TSqlFragment frag;
            IList<ParseError> errors;
            using (var reader = new StringReader(sql))
            {
                frag = parser.Parse(reader, out errors);
            }
            if (frag?.ScriptTokenStream == null) return sql;

            var toks = frag.ScriptTokenStream;
            var sb = new StringBuilder(sql.Length + 128);
            int depth = 0;

            for (int i = 0; i < toks.Count; i++)
            {
                var t = toks[i];
                if (t.TokenType == TSqlTokenType.EndOfFile) continue;
                string text = t.Text ?? "";

                if (t.TokenType == TSqlTokenType.LeftParenthesis) depth++;
                else if (t.TokenType == TSqlTokenType.RightParenthesis) depth = Math.Max(0, depth - 1);

                if (t.TokenType == TSqlTokenType.WhiteSpace && text.IndexOf('\n') >= 0 && depth > 0)
                {
                    // A closing parenthesis starting the next line belongs one level out.
                    int k = i + 1;
                    while (k < toks.Count && toks[k].TokenType == TSqlTokenType.WhiteSpace) k++;
                    int effective = (k < toks.Count && toks[k].TokenType == TSqlTokenType.RightParenthesis)
                        ? depth - 1 : depth;

                    if (effective > 0)
                    {
                        int cut = text.LastIndexOf('\n');
                        string head = text.Substring(0, cut + 1);
                        string tail = text.Substring(cut + 1);
                        int width = 0;
                        foreach (char c in tail) width += c == '\t' ? indentSize : 1;
                        int required = effective * indentSize;
                        if (width < required)
                            text = head + new string(' ', required);
                    }
                }

                sb.Append(text);
            }
            return sb.ToString();
        }

        private static readonly System.Collections.Generic.HashSet<string> BuiltInFunctions =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ABS","AVG","CAST","CEILING","CHARINDEX","COALESCE","CONCAT","CONVERT","COUNT",
                "COUNT_BIG","CURRENT_TIMESTAMP","DATEADD","DATEDIFF","DATENAME","DATEPART",
                "DAY","EOMONTH","FLOOR","FORMAT","GETDATE","GETUTCDATE","IIF","ISDATE","ISNULL",
                "ISNUMERIC","LEFT","LEN","LOWER","LTRIM","MAX","MIN","MONTH","NEWID","NULLIF",
                "PATINDEX","POWER","REPLACE","REPLICATE","REVERSE","RIGHT","ROUND","ROW_NUMBER",
                "RTRIM","SCOPE_IDENTITY","STRING_AGG","STUFF","SUBSTRING","SUM","SYSDATETIME",
                "TRIM","TRY_CAST","TRY_CONVERT","UPPER","YEAR"
            };

        private static readonly System.Collections.Generic.HashSet<string> DataTypeNames =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "BIGINT","BINARY","BIT","CHAR","DATE","DATETIME","DATETIME2","DATETIMEOFFSET",
                "DECIMAL","FLOAT","GEOGRAPHY","GEOMETRY","HIERARCHYID","IMAGE","INT","MONEY",
                "NCHAR","NTEXT","NUMERIC","NVARCHAR","REAL","SMALLDATETIME","SMALLINT",
                "SMALLMONEY","SQL_VARIANT","SYSNAME","TEXT","TIME","TIMESTAMP","TINYINT",
                "UNIQUEIDENTIFIER","VARBINARY","VARCHAR","XML"
            };

        /// <summary>
        /// Applies casing to built-in function names and data type keywords.
        /// Token-aware: strings, comments, quoted identifiers and variables are never
        /// touched. Function names are only re-cased when immediately followed by '(',
        /// so a column that happens to share a function name is left alone; data types
        /// are only re-cased when the parser classified them as type keywords rather
        /// than identifiers.
        /// </summary>
        private static string ApplyIdentifierCasing(string sql, IdentifierCase functionCase,
                                                    IdentifierCase dataTypeCase)
        {
            var parser = new TSql160Parser(initialQuotedIdentifiers: true);
            TSqlFragment frag;
            IList<ParseError> errors;
            using (var reader = new StringReader(sql))
            {
                frag = parser.Parse(reader, out errors);
            }
            if (frag?.ScriptTokenStream == null) return sql;

            var toks = frag.ScriptTokenStream;
            var sb = new StringBuilder(sql.Length);

            for (int i = 0; i < toks.Count; i++)
            {
                var t = toks[i];
                if (t.TokenType == TSqlTokenType.EndOfFile) continue;
                string text = t.Text ?? "";

                bool safeToRecase =
                    t.TokenType != TSqlTokenType.AsciiStringLiteral &&
                    t.TokenType != TSqlTokenType.UnicodeStringLiteral &&
                    t.TokenType != TSqlTokenType.QuotedIdentifier &&
                    t.TokenType != TSqlTokenType.Variable &&
                    t.TokenType != TSqlTokenType.SingleLineComment &&
                    t.TokenType != TSqlTokenType.MultilineComment &&
                    t.TokenType != TSqlTokenType.WhiteSpace;

                if (safeToRecase && text.Length > 0)
                {
                    if (dataTypeCase != IdentifierCase.Unchanged &&
                        t.TokenType != TSqlTokenType.Identifier &&
                        DataTypeNames.Contains(text))
                    {
                        text = ChangeCase(text, dataTypeCase);
                    }
                    else if (functionCase != IdentifierCase.Unchanged &&
                             BuiltInFunctions.Contains(text) &&
                             NextNonWhitespaceIsOpenParen(toks, i))
                    {
                        text = ChangeCase(text, functionCase);
                    }
                }

                sb.Append(text);
            }
            return sb.ToString();
        }

        private static bool NextNonWhitespaceIsOpenParen(IList<TSqlParserToken> toks, int index)
        {
            for (int j = index + 1; j < toks.Count; j++)
            {
                if (toks[j].TokenType == TSqlTokenType.WhiteSpace) continue;
                return toks[j].TokenType == TSqlTokenType.LeftParenthesis;
            }
            return false;
        }

        private static string ChangeCase(string value, IdentifierCase mode)
        {
            return mode == IdentifierCase.Lowercase
                ? value.ToLowerInvariant()
                : value.ToUpperInvariant();
        }

        /// <summary>
        /// Token-aware whitespace cleanup: trims trailing spaces/tabs at line ends and
        /// collapses runs of blank lines to a maximum. Operates only on whitespace
        /// tokens, so blank lines inside /* */ comments are never touched.
        /// </summary>
        private static string ApplyWhitespacePolicy(string sql, bool trim, int maxBlank)
        {
            var parser = new TSql160Parser(initialQuotedIdentifiers: true);
            TSqlFragment frag;
            IList<ParseError> errors;
            using (var reader = new StringReader(sql))
            {
                frag = parser.Parse(reader, out errors);
            }
            if (frag?.ScriptTokenStream == null) return sql;

            var sb = new StringBuilder(sql.Length);
            foreach (var t in frag.ScriptTokenStream)
            {
                if (t.TokenType == TSqlTokenType.EndOfFile) continue;
                string text = t.Text ?? "";
                if (t.TokenType == TSqlTokenType.WhiteSpace)
                {
                    if (trim)
                        text = System.Text.RegularExpressions.Regex.Replace(text, "[ \\t]+(\\r?\\n)", "$1");
                    if (maxBlank >= 0)
                    {
                        int newlines = 0;
                        foreach (char c in text) if (c == '\n') newlines++;
                        if (newlines > maxBlank + 1)
                        {
                            string tail = text.Substring(text.LastIndexOf('\n') + 1);
                            var rebuilt = new StringBuilder();
                            for (int k = 0; k < maxBlank + 1; k++) rebuilt.Append("\r\n");
                            rebuilt.Append(tail);
                            text = rebuilt.ToString();
                        }
                    }
                }
                sb.Append(text);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Enforces an exact number of blank lines before and after each GO batch
        /// separator. Token-aware: keeps "GO 5" batch counts and "GO -- comment"
        /// trailing comments on the GO line, and never touches GO inside comments
        /// or string literals.
        /// </summary>
        private static string NormalizeGoSpacing(string sql, int blankBefore, int blankAfter)
        {
            var parser = new TSql160Parser(initialQuotedIdentifiers: true);
            TSqlFragment frag;
            IList<ParseError> errors;
            using (var reader = new StringReader(sql))
            {
                frag = parser.Parse(reader, out errors);
            }
            if (frag?.ScriptTokenStream == null) return sql;

            var toks = frag.ScriptTokenStream;
            var sb = new StringBuilder(sql.Length);
            string pendingWs = null;

            string NewLines(int blanks)
            {
                var r = new StringBuilder();
                for (int k = 0; k < blanks + 1; k++) r.Append("\r\n");
                return r.ToString();
            }

            void StripTrailingNewlines()
            {
                while (sb.Length > 0 && (sb[sb.Length - 1] == '\n' || sb[sb.Length - 1] == '\r'))
                    sb.Length--;
            }

            for (int i = 0; i < toks.Count; i++)
            {
                var t = toks[i];
                if (t.TokenType == TSqlTokenType.EndOfFile) continue;

                if (t.TokenType == TSqlTokenType.WhiteSpace)
                {
                    pendingWs = (pendingWs ?? "") + (t.Text ?? "");
                    continue;
                }

                if (t.TokenType == TSqlTokenType.Go)
                {
                    if (sb.Length > 0)
                    {
                        StripTrailingNewlines();
                        sb.Append(NewLines(blankBefore));
                    }
                    pendingWs = null;
                    sb.Append(t.Text);

                    // Keep a batch count ("GO 5") or a trailing comment on the GO line.
                    int j = i + 1;
                    string wsAfter = "";
                    while (j < toks.Count && toks[j].TokenType == TSqlTokenType.WhiteSpace)
                    {
                        wsAfter += toks[j].Text ?? "";
                        j++;
                    }
                    if (j < toks.Count && wsAfter.IndexOf('\n') < 0 &&
                        toks[j].TokenType == TSqlTokenType.Integer)
                    {
                        sb.Append(' ').Append(toks[j].Text);
                        j++;
                        while (j < toks.Count && toks[j].TokenType == TSqlTokenType.WhiteSpace) j++;
                    }
                    else if (j < toks.Count && wsAfter.IndexOf('\n') < 0 &&
                             (toks[j].TokenType == TSqlTokenType.SingleLineComment ||
                              toks[j].TokenType == TSqlTokenType.MultilineComment))
                    {
                        sb.Append(' ').Append(toks[j].Text);
                        j++;
                        while (j < toks.Count && toks[j].TokenType == TSqlTokenType.WhiteSpace) j++;
                    }

                    bool moreContent = false;
                    for (int k = j; k < toks.Count; k++)
                    {
                        if (toks[k].TokenType != TSqlTokenType.WhiteSpace &&
                            toks[k].TokenType != TSqlTokenType.EndOfFile)
                        {
                            moreContent = true;
                            break;
                        }
                    }
                    sb.Append(moreContent ? NewLines(blankAfter) : "\r\n");
                    i = j - 1;
                    continue;
                }

                if (pendingWs != null)
                {
                    sb.Append(pendingWs);
                    pendingWs = null;
                }
                sb.Append(t.Text);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Token-aware pass: any comma that is the last token on its line is moved
        /// past the newline so the next line starts with ", ". Because it walks the
        /// parser's token stream, commas inside string literals are never touched.
        /// </summary>
        private static string MoveCommasToLineStart(string sql)
        {
            var parser = new TSql160Parser(initialQuotedIdentifiers: true);
            TSqlFragment fragment;
            IList<ParseError> errors;
            using (var reader = new StringReader(sql))
            {
                fragment = parser.Parse(reader, out errors);
            }
            // The input was just generated, so it should always parse;
            // if it somehow doesn't, leave the text unchanged.
            if (fragment?.ScriptTokenStream == null || (errors != null && errors.Count > 0))
                return sql;

            var tokens = fragment.ScriptTokenStream;
            var sb = new System.Text.StringBuilder(sql.Length + 64);

            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.TokenType == TSqlTokenType.Comma)
                {
                    // Gather any whitespace that follows the comma.
                    int j = i + 1;
                    var ws = new System.Text.StringBuilder();
                    while (j < tokens.Count && tokens[j].TokenType == TSqlTokenType.WhiteSpace)
                    {
                        ws.Append(tokens[j].Text);
                        j++;
                    }

                    if (ws.ToString().IndexOf('\n') >= 0)
                    {
                        // Comma ends the line: emit newline+indent first, then ", ".
                        sb.Append(ws).Append(", ");
                        i = j - 1;
                        continue;
                    }
                }

                if (token.Text != null)
                    sb.Append(token.Text);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Wraps top-level (depth-0) comma-separated lists that ended up on one line
        /// and exceed <paramref name="maxLength"/> characters, one item per line at the
        /// line's own indentation. Lists nested inside parentheses (IN-lists, function
        /// call arguments) are left untouched - reliably wrapping just the innermost
        /// list on a line with several nested parenthesized groups needs more context
        /// than this pass tracks, so it only handles the unambiguous depth-0 case.
        /// </summary>
        private static string WrapLongLines(string sql, int maxLength)
        {
            var parser = new TSql160Parser(initialQuotedIdentifiers: true);
            TSqlFragment frag;
            IList<ParseError> errors;
            using (var reader = new StringReader(sql))
            {
                frag = parser.Parse(reader, out errors);
            }
            if (frag?.ScriptTokenStream == null) return sql;

            var toks = frag.ScriptTokenStream;
            var result = new StringBuilder(sql.Length + 256);
            var lineBuf = new StringBuilder();
            var commaOffsets = new List<int>();
            string indent = "";
            bool atLineStart = true;
            int depth = 0;

            void FlushLine()
            {
                string line = lineBuf.ToString();
                if (indent.Length + line.Length > maxLength && commaOffsets.Count > 0)
                {
                    int last = 0;
                    foreach (int pos in commaOffsets)
                    {
                        int splitAt = pos + 1; // just after the comma
                        while (splitAt < line.Length && line[splitAt] == ' ') splitAt++;
                        result.Append(line, last, splitAt - last).Append("\r\n").Append(indent);
                        last = splitAt;
                    }
                    result.Append(line, last, line.Length - last);
                }
                else
                {
                    result.Append(line);
                }
                lineBuf.Clear();
                commaOffsets.Clear();
            }

            for (int i = 0; i < toks.Count; i++)
            {
                var t = toks[i];
                if (t.TokenType == TSqlTokenType.EndOfFile) continue;
                string text = t.Text ?? "";

                if (t.TokenType == TSqlTokenType.WhiteSpace)
                {
                    if (text.IndexOf('\n') >= 0)
                    {
                        FlushLine();
                        result.Append(text);
                        indent = text.Substring(text.LastIndexOf('\n') + 1);
                        atLineStart = true;
                        continue;
                    }
                    if (atLineStart)
                    {
                        // A whitespace-only token continuing the leading indent,
                        // tokenized separately from the preceding newline.
                        indent += text;
                        result.Append(text);
                        continue;
                    }
                    lineBuf.Append(text);
                    continue;
                }

                atLineStart = false;
                if (t.TokenType == TSqlTokenType.LeftParenthesis) depth++;
                else if (t.TokenType == TSqlTokenType.RightParenthesis) depth = Math.Max(0, depth - 1);
                else if (t.TokenType == TSqlTokenType.Comma && depth == 0)
                {
                    commaOffsets.Add(lineBuf.Length);
                }

                lineBuf.Append(text);
            }
            FlushLine();

            return result.ToString();
        }

        /// <summary>
        /// Aligns the '=' in runs of consecutive "name = expr" lines (SET clause
        /// assignments, old-style "alias = expr" SELECT items, simple equality lines)
        /// by padding the shorter left-hand sides with spaces. Only lines with exactly
        /// one top-level '=' (not nested inside parentheses) participate; anything else
        /// - including a line with more than one top-level '=' - breaks the run, so
        /// unrelated code (comparisons, multi-condition lines) is never touched.
        /// </summary>
        private static string AlignAssignments(string sql)
        {
            var parser = new TSql160Parser(initialQuotedIdentifiers: true);
            TSqlFragment frag;
            IList<ParseError> errors;
            using (var reader = new StringReader(sql))
            {
                frag = parser.Parse(reader, out errors);
            }
            if (frag?.ScriptTokenStream == null) return sql;

            var toks = frag.ScriptTokenStream;
            var equalsColumnByLine = new Dictionary<int, int>();
            var equalsCountByLine = new Dictionary<int, int>();
            var equalsTokenIndexByLine = new Dictionary<int, int>();
            var firstTokenTypeByLine = new Dictionary<int, TSqlTokenType>();

            int depth = 0;
            int currentLine = 0;
            int col = 0;
            for (int i = 0; i < toks.Count; i++)
            {
                var t = toks[i];
                if (t.TokenType == TSqlTokenType.EndOfFile) continue;
                string text = t.Text ?? "";

                if (t.TokenType == TSqlTokenType.WhiteSpace && text.IndexOf('\n') >= 0)
                {
                    currentLine++;
                    col = text.Length - text.LastIndexOf('\n') - 1;
                    continue;
                }
                if (t.TokenType == TSqlTokenType.WhiteSpace)
                {
                    col += text.Length;
                    continue;
                }

                if (!firstTokenTypeByLine.ContainsKey(currentLine))
                    firstTokenTypeByLine[currentLine] = t.TokenType;

                if (t.TokenType == TSqlTokenType.LeftParenthesis) depth++;
                else if (t.TokenType == TSqlTokenType.RightParenthesis) depth = Math.Max(0, depth - 1);
                else if (t.TokenType == TSqlTokenType.EqualsSign && depth == 0)
                {
                    equalsCountByLine[currentLine] = equalsCountByLine.TryGetValue(currentLine, out int c) ? c + 1 : 1;
                    if (!equalsColumnByLine.ContainsKey(currentLine))
                    {
                        equalsColumnByLine[currentLine] = col;
                        equalsTokenIndexByLine[currentLine] = i;
                    }
                }

                col += text.Length;
            }

            // A line only qualifies as a plain assignment when it starts with an
            // identifier (or a leading comma, for the leading-comma style) - this
            // excludes clause keywords (WHERE, HAVING, ON, AND, OR, ...) so an
            // unrelated condition never gets pulled into a SET clause's alignment run.
            bool IsAssignmentLineStart(TSqlTokenType tt) =>
                tt == TSqlTokenType.Identifier || tt == TSqlTokenType.QuotedIdentifier ||
                tt == TSqlTokenType.Variable || tt == TSqlTokenType.Comma;

            var eligibleLines = equalsColumnByLine.Keys
                .Where(l => equalsCountByLine[l] == 1 &&
                            firstTokenTypeByLine.TryGetValue(l, out var ft) && IsAssignmentLineStart(ft))
                .OrderBy(l => l)
                .ToList();
            if (eligibleLines.Count < 2) return sql;

            // Group into runs of consecutive eligible lines; align each run independently.
            var targetColumnByLine = new Dictionary<int, int>();
            int runStart = 0;
            for (int k = 1; k <= eligibleLines.Count; k++)
            {
                bool endOfRun = k == eligibleLines.Count || eligibleLines[k] != eligibleLines[k - 1] + 1;
                if (endOfRun)
                {
                    if (k - runStart >= 2)
                    {
                        int maxCol = 0;
                        for (int m = runStart; m < k; m++)
                            maxCol = Math.Max(maxCol, equalsColumnByLine[eligibleLines[m]]);
                        for (int m = runStart; m < k; m++)
                            targetColumnByLine[eligibleLines[m]] = maxCol;
                    }
                    runStart = k;
                }
            }
            if (targetColumnByLine.Count == 0) return sql;

            var padBeforeIndex = new Dictionary<int, int>();
            foreach (var kvp in targetColumnByLine)
            {
                int pad = kvp.Value - equalsColumnByLine[kvp.Key];
                if (pad > 0) padBeforeIndex[equalsTokenIndexByLine[kvp.Key]] = pad;
            }
            if (padBeforeIndex.Count == 0) return sql;

            var sb = new StringBuilder(sql.Length + padBeforeIndex.Count * 4);
            for (int i = 0; i < toks.Count; i++)
            {
                if (toks[i].TokenType == TSqlTokenType.EndOfFile) continue;
                if (padBeforeIndex.TryGetValue(i, out int pad))
                    sb.Append(' ', pad);
                sb.Append(toks[i].Text ?? "");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Converts each leading group of <paramref name="indentSize"/> spaces into a tab.
        /// Only affects indentation at the start of lines, never spacing inside a line.
        /// </summary>
        private static string ConvertIndentToTabs(string sql, int indentSize)
        {
            var lines = sql.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                int spaces = 0;
                while (spaces < line.Length && line[spaces] == ' ')
                    spaces++;

                if (spaces >= indentSize)
                {
                    int tabs = spaces / indentSize;
                    int remainder = spaces % indentSize;
                    lines[i] = new string('\t', tabs) + new string(' ', remainder) + line.Substring(spaces);
                }
            }
            return string.Join("\n", lines);
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
            var commaNote = o.Commas == CommaPlacement.Leading
                ? " Use LEADING commas: in multi-line lists each line after the first starts with a comma."
                : " Use trailing commas at line ends.";
            var indentNote = o.UseTabsForIndentation ? " Indent using tab characters." : "";
            var goNote = (o.BlankLinesBeforeGo >= 0 && o.BlankLinesAfterGo >= 0)
                ? $" Put exactly {o.BlankLinesBeforeGo} blank line(s) before each GO and {o.BlankLinesAfterGo} after."
                : "";
            var stmtNote = o.BlankLinesBetweenStatements >= 0
                ? $" Put exactly {o.BlankLinesBetweenStatements} blank line(s) between consecutive statements, including inside BEGIN...END/IF/WHILE/TRY-CATCH bodies."
                : "";

            if (o.Preset == StylePreset.Classic)
            {
                return "Classic compact style: UPPERCASE keywords, trailing commas, SELECT list on one line " +
                       "unless very long, FROM/WHERE/GROUP BY/ORDER BY each start a new line, JOINs stay inline " +
                       "with their table, minimal blank lines, indent " + o.IndentationSize + " spaces" +
                       (o.IncludeSemicolons ? ", terminate statements with semicolons." : ".") + commaNote + indentNote + goNote + stmtNote;
            }

            if (o.Preset == StylePreset.Modern)
            {
                return "Modern expanded style: UPPERCASE keywords, each selected column on its own line, " +
                       "each JOIN and each ON condition on its own line, each AND/OR predicate in WHERE on its own line, " +
                       "clause bodies aligned and indented " + o.IndentationSize + " spaces" +
                       (o.IncludeSemicolons ? ", terminate statements with semicolons." : ".") + commaNote + indentNote + goNote + stmtNote;
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
            return "Custom style: " + string.Join(", ", parts) + "." + commaNote + indentNote + goNote + stmtNote;
        }
    }
}
