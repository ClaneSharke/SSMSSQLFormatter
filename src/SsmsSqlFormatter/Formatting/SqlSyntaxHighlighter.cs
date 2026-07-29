using System.Collections.Generic;
using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SsmsSqlFormatter.Formatting
{
    public enum SqlTokenCategory
    {
        Default,
        Keyword,
        String,
        Number,
        Comment,
        Identifier
    }

    /// <summary>
    /// Best-effort T-SQL syntax classification for display purposes (the Preview
    /// window's diff view). Tokenizes one line at a time, since the caller (a line-based
    /// diff) only ever needs to color one line at a time; a line that doesn't parse
    /// standalone (e.g. the continuation of a multi-line string or comment) falls back
    /// to a single Default-category fragment rather than throwing.
    /// </summary>
    public static class SqlSyntaxHighlighter
    {
        private static readonly HashSet<TSqlTokenType> CommentTokens = new HashSet<TSqlTokenType>
        {
            TSqlTokenType.SingleLineComment, TSqlTokenType.MultilineComment
        };

        private static readonly HashSet<TSqlTokenType> StringTokens = new HashSet<TSqlTokenType>
        {
            TSqlTokenType.AsciiStringLiteral, TSqlTokenType.UnicodeStringLiteral,
            TSqlTokenType.AsciiStringOrQuotedIdentifier
        };

        private static readonly HashSet<TSqlTokenType> NumberTokens = new HashSet<TSqlTokenType>
        {
            TSqlTokenType.Integer, TSqlTokenType.Numeric, TSqlTokenType.Real,
            TSqlTokenType.HexLiteral, TSqlTokenType.Money
        };

        private static readonly HashSet<TSqlTokenType> IdentifierTokens = new HashSet<TSqlTokenType>
        {
            TSqlTokenType.Identifier, TSqlTokenType.QuotedIdentifier, TSqlTokenType.Variable,
            TSqlTokenType.SqlCommandIdentifier, TSqlTokenType.PseudoColumn, TSqlTokenType.DollarPartition,
            TSqlTokenType.OdbcInitiator, TSqlTokenType.ProcNameSemicolon
        };

        // Punctuation/operators and whitespace/end-of-file are rendered in the default
        // color, alongside plain identifiers - only keywords, literals and comments get
        // a distinct color.
        private static readonly HashSet<TSqlTokenType> DefaultColoredTokens = new HashSet<TSqlTokenType>
        {
            TSqlTokenType.WhiteSpace, TSqlTokenType.EndOfFile, TSqlTokenType.None,
            TSqlTokenType.Bang, TSqlTokenType.PercentSign, TSqlTokenType.Ampersand,
            TSqlTokenType.LeftParenthesis, TSqlTokenType.RightParenthesis,
            TSqlTokenType.LeftCurly, TSqlTokenType.RightCurly, TSqlTokenType.Star,
            TSqlTokenType.MultiplyEquals, TSqlTokenType.Plus, TSqlTokenType.Comma,
            TSqlTokenType.Minus, TSqlTokenType.Dot, TSqlTokenType.Divide, TSqlTokenType.Colon,
            TSqlTokenType.DoubleColon, TSqlTokenType.Semicolon, TSqlTokenType.LessThan,
            TSqlTokenType.EqualsSign, TSqlTokenType.GreaterThan, TSqlTokenType.Circumflex,
            TSqlTokenType.VerticalLine, TSqlTokenType.Tilde, TSqlTokenType.AddEquals,
            TSqlTokenType.SubtractEquals, TSqlTokenType.DivideEquals, TSqlTokenType.ModEquals,
            TSqlTokenType.BitwiseAndEquals, TSqlTokenType.BitwiseOrEquals, TSqlTokenType.BitwiseXorEquals,
            TSqlTokenType.LeftShift, TSqlTokenType.RightShift, TSqlTokenType.Concat,
            TSqlTokenType.ConcatEquals, TSqlTokenType.Label
        };

        public static SqlTokenCategory Classify(TSqlTokenType type)
        {
            if (CommentTokens.Contains(type)) return SqlTokenCategory.Comment;
            if (StringTokens.Contains(type)) return SqlTokenCategory.String;
            if (NumberTokens.Contains(type)) return SqlTokenCategory.Number;
            if (IdentifierTokens.Contains(type)) return SqlTokenCategory.Identifier;
            if (DefaultColoredTokens.Contains(type)) return SqlTokenCategory.Default;
            return SqlTokenCategory.Keyword; // everything else in the 240+ token types is a reserved keyword
        }

        /// <summary>Splits one line of T-SQL text into (text, category) fragments in order, for syntax-colored rendering.</summary>
        public static List<(string Text, SqlTokenCategory Category)> TokenizeLine(string lineText)
        {
            var result = new List<(string, SqlTokenCategory)>();
            if (string.IsNullOrEmpty(lineText))
            {
                result.Add((lineText ?? string.Empty, SqlTokenCategory.Default));
                return result;
            }

            try
            {
                var parser = new TSql160Parser(initialQuotedIdentifiers: true);
                TSqlFragment frag;
                IList<ParseError> errors;
                using (var reader = new StringReader(lineText))
                {
                    frag = parser.Parse(reader, out errors);
                }

                if (frag?.ScriptTokenStream != null)
                {
                    foreach (var t in frag.ScriptTokenStream)
                    {
                        if (t.TokenType == TSqlTokenType.EndOfFile) continue;
                        string text = t.Text ?? string.Empty;
                        if (text.Length == 0) continue;
                        result.Add((text, Classify(t.TokenType)));
                    }
                }
            }
            catch
            {
                result.Clear();
            }

            if (result.Count == 0)
                result.Add((lineText, SqlTokenCategory.Default));

            return result;
        }
    }
}
