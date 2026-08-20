namespace SsmsSqlFormatter.Options
{
    /// <summary>
    /// The subset of formatting settings that <see cref="Formatting.ScriptDomFormatter"/>
    /// actually reads. Exists so the formatter core has no dependency on
    /// <see cref="Microsoft.VisualStudio.Shell.DialogPage"/> (and therefore no dependency
    /// on the Visual Studio SDK being installed) - <see cref="GeneralOptions"/> implements
    /// this for the VSIX, and <see cref="FormatterSettings"/> implements it as a plain
    /// POCO for standalone/CI use (see the SsmsSqlFormatter.Cli project).
    /// </summary>
    public interface IFormatterOptions
    {
        StylePreset Preset { get; }

        KeywordCase KeywordCasing { get; }
        IdentifierCase FunctionCasing { get; }
        IdentifierCase DataTypeCasing { get; }
        int IndentationSize { get; }
        bool UseTabsForIndentation { get; }
        bool ReindentSubqueries { get; }
        CommaPlacement Commas { get; }
        bool IncludeSemicolons { get; }
        int MaxLineLength { get; }
        bool AsKeywordOnOwnLine { get; }

        bool NewLineBeforeFrom { get; }
        bool NewLineBeforeWhere { get; }
        bool NewLineBeforeJoin { get; }
        bool NewLineBeforeGroupBy { get; }
        bool NewLineBeforeOrderBy { get; }
        bool NewLineBeforeHaving { get; }
        bool NewLineBeforeOutput { get; }
        bool NewLineBeforeOffset { get; }
        bool NewLineBeforeOpenParen { get; }
        bool NewLineBeforeCloseParen { get; }

        bool MultilineSelectList { get; }
        bool MultilineWherePredicates { get; }
        bool MultilineInsertLists { get; }
        bool AlignClauseBodies { get; }
        bool AlignColumnDefinitions { get; }
        bool MultilineViewColumns { get; }
        bool IndentViewBody { get; }
        bool IndentSetClause { get; }
        bool AlignSetClauseAssignments { get; }
        bool AlignJoinConditions { get; }
        bool AlignCaseExpressions { get; }

        int BlankLinesBeforeGo { get; }
        int BlankLinesAfterGo { get; }
        int BlankLinesBetweenStatements { get; }
        int MaxConsecutiveBlankLines { get; }
        bool TrimTrailingWhitespace { get; }

        CommentHandling CommentHandling { get; }
        bool EnableFormattingCache { get; }
    }
}
