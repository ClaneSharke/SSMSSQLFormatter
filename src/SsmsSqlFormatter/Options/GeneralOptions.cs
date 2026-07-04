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

        // ---------- Safety ----------
        [Category("5. Safety")]
        [DisplayName("Preserve comments")]
        [Description("Re-insert the original script's comments into the formatted output, keeping them attached to the same code (trailing comments stay at line ends, standalone comments keep their own line). If a comment can't be confidently repositioned it is appended at the end under a banner - never silently deleted.")]
        public bool PreserveComments { get; set; } = true;

        [Category("5. Safety")]
        [DisplayName("Warn when script contains comments")]
        [Description("Only applies when 'Preserve comments' is OFF: warns that reformatting may drop or move comments and offers the chance to cancel.")]
        public bool WarnOnComments { get; set; } = true;
    }
}
