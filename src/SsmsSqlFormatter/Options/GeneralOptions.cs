using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace SsmsSqlFormatter.Options
{
    public enum FormatterEngine
    {
        RuleBased,
        Ai
    }

    public enum StylePreset
    {
        /// <summary>Old-school compact style: UPPERCASE keywords, trailing commas, fewer line breaks.</summary>
        Classic,
        /// <summary>Modern style: each clause on its own line, multiline SELECT lists, aligned bodies.</summary>
        Modern,
        /// <summary>Use the individual options below exactly as set.</summary>
        Custom
    }

    public enum KeywordCase
    {
        Uppercase,
        Lowercase,
        PascalCase
    }

    public enum CommaPlacement
    {
        /// <summary>Commas at the end of each line (a, ↵ b).</summary>
        Trailing,
        /// <summary>Commas at the start of the next line (a ↵ , b).</summary>
        Leading
    }

    public enum ExcelResultAction
    {
        /// <summary>Put an Excel-format table on the clipboard for Ctrl+V.</summary>
        CopyToClipboard,
        /// <summary>Write a real .xlsx workbook and open it - no clipboard involved.</summary>
        OpenInExcel
    }

    public class GeneralOptions : DialogPage
    {
        // ---------- Engine ----------
        [Category("1. Engine")]
        [DisplayName("Formatting engine")]
        [Description("RuleBased = Microsoft ScriptDom parser (offline, instant). Ai = Anthropic Claude API (preserves comments, follows custom instructions; configure under 'AI Engine').")]
        public FormatterEngine Engine { get; set; } = FormatterEngine.RuleBased;

        [Category("1. Engine")]
        [DisplayName("Style preset")]
        [Description("Classic = old-format compact style. Modern = new expanded style. Custom = use the individual options below.")]
        public StylePreset Preset { get; set; } = StylePreset.Modern;

        // ---------- Casing / basics ----------
        [Category("2. Basics")]
        [DisplayName("Keyword casing")]
        public KeywordCase KeywordCasing { get; set; } = KeywordCase.Uppercase;

        [Category("2. Basics")]
        [DisplayName("Indent size (spaces)")]
        public int IndentationSize { get; set; } = 4;

        [Category("2. Basics")]
        [DisplayName("Indent with tabs")]
        [Description("Convert each indent level ('Indent size' spaces) into a tab character.")]
        public bool UseTabsForIndentation { get; set; } = false;

        [Category("2. Basics")]
        [DisplayName("Re-indent subqueries")]
        [Description("Guarantees the body of every nested subquery / derived table is indented at least one level per nesting depth. Fixes cases where the formatter leaves subquery clauses flush with the outer query. Only ever adds indentation - lines already indented deeper are left alone.")]
        public bool ReindentSubqueries { get; set; } = true;

        [Category("2. Basics")]
        [DisplayName("Comma placement")]
        [Description("Trailing = commas end each line. Leading = commas start the next line (a common style for easy column commenting). Applies to all presets.")]
        public CommaPlacement Commas { get; set; } = CommaPlacement.Trailing;

        [Category("2. Basics")]
        [DisplayName("Add semicolons")]
        [Description("Terminate statements with semicolons.")]
        public bool IncludeSemicolons { get; set; } = true;

        [Category("2. Basics")]
        [DisplayName("AS keyword on its own line")]
        [Description("Place AS on a new line in views/procedures (Custom preset only).")]
        public bool AsKeywordOnOwnLine { get; set; } = true;

        // ---------- Line breaks ----------
        [Category("3. Line breaks")]
        [DisplayName("New line before FROM")]
        public bool NewLineBeforeFrom { get; set; } = true;

        [Category("3. Line breaks")]
        [DisplayName("New line before WHERE")]
        public bool NewLineBeforeWhere { get; set; } = true;

        [Category("3. Line breaks")]
        [DisplayName("New line before JOIN")]
        public bool NewLineBeforeJoin { get; set; } = true;

        [Category("3. Line breaks")]
        [DisplayName("New line before GROUP BY")]
        public bool NewLineBeforeGroupBy { get; set; } = true;

        [Category("3. Line breaks")]
        [DisplayName("New line before ORDER BY")]
        public bool NewLineBeforeOrderBy { get; set; } = true;

        [Category("3. Line breaks")]
        [DisplayName("New line before HAVING")]
        public bool NewLineBeforeHaving { get; set; } = true;

        [Category("3. Line breaks")]
        [DisplayName("New line before OUTPUT")]
        public bool NewLineBeforeOutput { get; set; } = true;

        [Category("3. Line breaks")]
        [DisplayName("New line before OFFSET")]
        public bool NewLineBeforeOffset { get; set; } = true;

        [Category("3. Line breaks")]
        [DisplayName("New line before ( in multiline lists")]
        [Description("Opening parenthesis of a multiline list goes on its own line.")]
        public bool NewLineBeforeOpenParen { get; set; } = true;

        [Category("3. Line breaks")]
        [DisplayName("New line before ) in multiline lists")]
        [Description("Closing parenthesis of a multiline list goes on its own line.")]
        public bool NewLineBeforeCloseParen { get; set; } = true;

        // ---------- Lists ----------
        [Category("4. Lists")]
        [DisplayName("Multiline SELECT list")]
        [Description("Each selected column on its own line.")]
        public bool MultilineSelectList { get; set; } = true;

        [Category("4. Lists")]
        [DisplayName("Multiline WHERE predicates")]
        [Description("Each AND/OR predicate on its own line.")]
        public bool MultilineWherePredicates { get; set; } = true;

        [Category("4. Lists")]
        [DisplayName("Multiline INSERT lists")]
        public bool MultilineInsertLists { get; set; } = true;

        [Category("4. Lists")]
        [DisplayName("Align clause bodies")]
        [Description("Align the body of clauses (SELECT list, SET clauses, etc.).")]
        public bool AlignClauseBodies { get; set; } = true;

        [Category("4. Lists")]
        [DisplayName("Align column definitions")]
        [Description("Align column definition fields in CREATE TABLE.")]
        public bool AlignColumnDefinitions { get; set; } = true;

        [Category("4. Lists")]
        [DisplayName("Multiline view columns")]
        [Description("Each column in a view's column list on its own line.")]
        public bool MultilineViewColumns { get; set; } = true;

        [Category("4. Lists")]
        [DisplayName("Indent view body")]
        public bool IndentViewBody { get; set; } = true;

        [Category("4. Lists")]
        [DisplayName("Indent SET clause")]
        [Description("Indent the SET clause body in UPDATE statements.")]
        public bool IndentSetClause { get; set; } = true;

        // ---------- Blank lines & GO ----------
        [Category("5. Blank lines and GO")]
        [DisplayName("Blank lines before GO")]
        [Description("Exact number of blank lines before each GO batch separator. Set both GO options to -1 to leave GO spacing untouched.")]
        public int BlankLinesBeforeGo { get; set; } = 1;

        [Category("5. Blank lines and GO")]
        [DisplayName("Blank lines after GO")]
        [Description("Exact number of blank lines after each GO batch separator. Set both GO options to -1 to leave GO spacing untouched.")]
        public int BlankLinesAfterGo { get; set; } = 1;

        [Category("5. Blank lines and GO")]
        [DisplayName("Blank lines between statements")]
        [Description("Exact number of blank lines between consecutive top-level statements within a batch. Statements nested inside BEGIN...END bodies are not affected. A comment above a statement stays attached to it (blank lines go above the comment). -1 = leave as-is.")]
        public int BlankLinesBetweenStatements { get; set; } = 1;

        [Category("5. Blank lines and GO")]
        [DisplayName("Max consecutive blank lines")]
        [Description("Collapse runs of blank lines anywhere in the script down to this many. -1 = unlimited. Blank lines inside /* */ comments are never touched, and the GO settings above take precedence around GO.")]
        public int MaxConsecutiveBlankLines { get; set; } = 1;

        [Category("5. Blank lines and GO")]
        [DisplayName("Trim trailing whitespace")]
        [Description("Remove spaces and tabs at the ends of lines.")]
        public bool TrimTrailingWhitespace { get; set; } = true;

        // ---------- Copy results for Excel ----------
        [Category("7. Copy results for Excel")]
        [DisplayName("What Ctrl+Shift+Alt+X does")]
        [Description("CopyToClipboard = put an Excel-format table on the clipboard for Ctrl+V. OpenInExcel = write a real .xlsx workbook and open it immediately - no clipboard involved, so it always works even when SSMS runs elevated or over remote desktop.")]
        public ExcelResultAction ExcelAction { get; set; } = ExcelResultAction.CopyToClipboard;

        [Category("7. Copy results for Excel")]
        [DisplayName("Try to copy the grid automatically")]
        [Description("Sends the grid's Copy-with-Headers keystroke before converting. This only works when the results grid still has focus, i.e. when you use the Ctrl+Shift+Alt+X shortcut - clicking a toolbar button or menu item moves focus away, in which case whatever you copied yourself is converted instead.")]
        public bool ExcelSimulateCopyFirst { get; set; } = true;

        [Category("7. Copy results for Excel")]
        [DisplayName("First row contains headers")]
        [Description("Format the first copied row as a bold header row. Turn this off if you copied without headers (plain Ctrl+C), so the first data row isn't styled as a header.")]
        public bool ExcelFirstRowIsHeader { get; set; } = true;

        [Category("7. Copy results for Excel")]
        [DisplayName("Paste all cells as text")]
        [Description("Marks every data cell as Text so Excel keeps leading zeros and doesn't turn long numbers into scientific notation or ID-like values into dates. Disable to let Excel auto-detect types (numbers become summable).")]
        public bool ExcelForceTextCells { get; set; } = true;

        [Category("7. Copy results for Excel")]
        [DisplayName("Paste NULL as empty cells")]
        [Description("Convert the grid's literal 'NULL' text into empty cells when pasting into Excel.")]
        public bool ExcelNullsAsEmpty { get; set; } = false;

        // ---------- Excel appearance ----------
        [Category("8. Excel appearance")]
        [DisplayName("Font name")]
        [Description("Font applied to the pasted table, e.g. Calibri, Arial, Consolas.")]
        public string ExcelFontName { get; set; } = "Calibri";

        [Category("8. Excel appearance")]
        [DisplayName("Font size (pt)")]
        public int ExcelFontSize { get; set; } = 11;

        [Category("8. Excel appearance")]
        [DisplayName("Bold header row")]
        public bool ExcelHeaderBold { get; set; } = true;

        [Category("8. Excel appearance")]
        [DisplayName("Header background colour")]
        [Description("Hex colour such as #D9E1F2 (light blue), #DDEBF7, #FCE4D6 (peach), or a CSS colour name. Invalid values fall back to the default.")]
        public string ExcelHeaderBackColor { get; set; } = "#D9E1F2";

        [Category("8. Excel appearance")]
        [DisplayName("Header text colour")]
        [Description("Hex colour such as #000000 (black) or #FFFFFF (white for dark headers).")]
        public string ExcelHeaderTextColor { get; set; } = "#000000";

        [Category("8. Excel appearance")]
        [DisplayName("Show cell borders")]
        public bool ExcelShowBorders { get; set; } = true;

        [Category("8. Excel appearance")]
        [DisplayName("Border colour")]
        [Description("Hex colour for cell borders, e.g. #B4C6E7 or #BFBFBF.")]
        public string ExcelBorderColor { get; set; } = "#B4C6E7";

        [Category("8. Excel appearance")]
        [DisplayName("Banded rows")]
        [Description("Shade every second data row for readability.")]
        public bool ExcelBandedRows { get; set; } = false;

        [Category("8. Excel appearance")]
        [DisplayName("Band colour")]
        [Description("Hex colour used for the shaded rows when 'Banded rows' is on.")]
        public string ExcelBandColor { get; set; } = "#F2F6FC";

        // ---------- Safety ----------
        [Category("6. Safety")]
        [DisplayName("Preserve comments")]
        [Description("Re-insert the original script's comments into the formatted output, keeping them attached to the same code (trailing comments stay at line ends, standalone comments keep their own line). If a comment can't be confidently repositioned it is appended at the end under a banner - never silently deleted.")]
        public bool PreserveComments { get; set; } = true;

        [Category("6. Safety")]
        [DisplayName("Warn when script contains comments")]
        [Description("Only applies when 'Preserve comments' is OFF: warns that reformatting may drop or move comments and offers the chance to cancel.")]
        public bool WarnOnComments { get; set; } = true;
    }
}
