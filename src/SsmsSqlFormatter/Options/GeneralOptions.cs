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
        [DisplayName("Warn when script contains comments")]
        [Description("The rule-based engine regenerates the script from its parse tree and can drop comments. When enabled, you get a warning and a chance to cancel (or use the AI engine, which preserves comments).")]
        public bool WarnOnComments { get; set; } = true;
    }
}
