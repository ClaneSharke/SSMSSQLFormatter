using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

                if (options.PreserveComments && result.CommentCount > 0)
                    formatted = ReinjectComments(sql, formatted);

                formatted = PostProcess(formatted, options);

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

            if (options.Commas == CommaPlacement.Leading)
                sql = MoveCommasToLineStart(sql);

            if (options.UseTabsForIndentation)
                sql = ConvertIndentToTabs(sql, Math.Max(1, options.IndentationSize));

            return sql;
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

            if (o.Preset == StylePreset.Classic)
            {
                return "Classic compact style: UPPERCASE keywords, trailing commas, SELECT list on one line " +
                       "unless very long, FROM/WHERE/GROUP BY/ORDER BY each start a new line, JOINs stay inline " +
                       "with their table, minimal blank lines, indent " + o.IndentationSize + " spaces" +
                       (o.IncludeSemicolons ? ", terminate statements with semicolons." : ".") + commaNote + indentNote + goNote;
            }

            if (o.Preset == StylePreset.Modern)
            {
                return "Modern expanded style: UPPERCASE keywords, each selected column on its own line, " +
                       "each JOIN and each ON condition on its own line, each AND/OR predicate in WHERE on its own line, " +
                       "clause bodies aligned and indented " + o.IndentationSize + " spaces" +
                       (o.IncludeSemicolons ? ", terminate statements with semicolons." : ".") + commaNote + indentNote + goNote;
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
            return "Custom style: " + string.Join(", ", parts) + "." + commaNote + indentNote + goNote;
        }
    }
}
