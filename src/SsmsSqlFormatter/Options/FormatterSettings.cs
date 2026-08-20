namespace SsmsSqlFormatter.Options
{
    /// <summary>
    /// Plain, dependency-free implementation of <see cref="IFormatterOptions"/> - no base
    /// class, no Visual Studio SDK dependency. Used by the CLI (and directly testable
    /// without a VS install), with the same defaults as <see cref="GeneralOptions"/>.
    /// </summary>
    public class FormatterSettings : IFormatterOptions
    {
        public StylePreset Preset { get; set; } = StylePreset.Modern;

        public KeywordCase KeywordCasing { get; set; } = KeywordCase.Uppercase;
        public IdentifierCase FunctionCasing { get; set; } = IdentifierCase.Unchanged;
        public IdentifierCase DataTypeCasing { get; set; } = IdentifierCase.Unchanged;
        public int IndentationSize { get; set; } = 4;
        public bool UseTabsForIndentation { get; set; } = false;
        public bool ReindentSubqueries { get; set; } = true;
        public CommaPlacement Commas { get; set; } = CommaPlacement.Trailing;
        public bool IncludeSemicolons { get; set; } = true;
        public int MaxLineLength { get; set; } = 0;
        public bool AsKeywordOnOwnLine { get; set; } = true;

        public bool NewLineBeforeFrom { get; set; } = true;
        public bool NewLineBeforeWhere { get; set; } = true;
        public bool NewLineBeforeJoin { get; set; } = true;
        public bool NewLineBeforeGroupBy { get; set; } = true;
        public bool NewLineBeforeOrderBy { get; set; } = true;
        public bool NewLineBeforeHaving { get; set; } = true;
        public bool NewLineBeforeOutput { get; set; } = true;
        public bool NewLineBeforeOffset { get; set; } = true;
        public bool NewLineBeforeOpenParen { get; set; } = true;
        public bool NewLineBeforeCloseParen { get; set; } = true;

        public bool MultilineSelectList { get; set; } = true;
        public bool MultilineWherePredicates { get; set; } = true;
        public bool MultilineInsertLists { get; set; } = true;
        public bool AlignClauseBodies { get; set; } = true;
        public bool AlignColumnDefinitions { get; set; } = true;
        public bool MultilineViewColumns { get; set; } = true;
        public bool IndentViewBody { get; set; } = true;
        public bool IndentSetClause { get; set; } = true;
        public bool AlignSetClauseAssignments { get; set; } = false;
        public bool AlignJoinConditions { get; set; } = false;
        public bool AlignCaseExpressions { get; set; } = false;

        public int BlankLinesBeforeGo { get; set; } = 1;
        public int BlankLinesAfterGo { get; set; } = 1;
        public int BlankLinesBetweenStatements { get; set; } = 1;
        public int MaxConsecutiveBlankLines { get; set; } = 1;
        public bool TrimTrailingWhitespace { get; set; } = true;

        public CommentHandling CommentHandling { get; set; } = CommentHandling.Inline;
        public bool EnableFormattingCache { get; set; } = false;
    }
}
