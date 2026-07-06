using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace SsmsSqlFormatter.Options
{
    /// <summary>
    /// Read-only help page under Tools > Options > Format T-SQL Script > Help.
    /// Properties have no setters, so the grid shows them as informational text;
    /// clicking a row shows the full text in the description panel below the grid.
    /// </summary>
    public class HelpOptions : DialogPage
    {
        [Category("1. How to format")]
        [DisplayName("Run the formatter")]
        [Description("Press Ctrl+Shift+Alt+F in a query window, or right-click the editor and choose 'Format T-SQL Script', or use the Tools menu. With text selected only the selection is formatted; otherwise the whole document. Ctrl+Z undoes the entire format in one step.")]
        public string HowToFormat => "Ctrl+Shift+Alt+F  |  right-click > Format T-SQL Script";

        [Category("1. How to format")]
        [DisplayName("Parse errors")]
        [Description("If the script contains a syntax error the formatter refuses to change anything and reports the line and column of the first error. Fix the error and format again.")]
        public string ParseErrors => "Scripts with syntax errors are never modified";

        [Category("2. Engines")]
        [DisplayName("Rule-based engine")]
        [Description("Default engine. Uses Microsoft's ScriptDom parser - works fully offline, formats instantly, and understands all of T-SQL. Configure it on the General page: choose the Classic or Modern preset, or set the preset to Custom to control every individual rule (casing, indentation, line breaks, comma placement, GO spacing, and more).")]
        public string RuleBasedEngine => "Offline, instant, configured on the General page";

        [Category("2. Engines")]
        [DisplayName("AI engine")]
        [Description("Optional. Sends the script to the Anthropic API (you supply your own API key on the AI Engine page) and follows free-form style instructions such as 'align equals signs in SET clauses'. Your script text leaves this machine when this engine is used - a confirmation prompt is shown by default. API usage is billed to your Anthropic key.")]
        public string AiEngine => "Optional, needs an Anthropic API key (AI Engine page)";

        [Category("3. Comments and GO")]
        [DisplayName("Comment preservation")]
        [Description("'Preserve comments' (General page, on by default) re-inserts your comments next to the code they belong to after formatting. Any comment that cannot be confidently repositioned is appended at the end of the script under a banner - comments are never silently deleted.")]
        public string Comments => "Comments are kept and repositioned; never silently deleted";

        [Category("3. Comments and GO")]
        [DisplayName("GO spacing")]
        [Description("'Blank lines before/after GO' (General page) set the exact number of blank lines around each GO batch separator. 'GO 5' batch counts and comments trailing a GO stay on the GO line. Set both options to -1 to leave GO spacing untouched.")]
        public string GoSpacing => "Exact blank-line control around GO batch separators";

        [Category("4. Support")]
        [DisplayName("Project page and updates")]
        [Description("Documentation, source code, new releases, and the issue tracker live on GitHub: https://github.com/ClaneSharke/SSMSSQLFormatter - report scripts that format oddly as GitHub issues, ideally with a small example script.")]
        public string ProjectPage => "github.com/ClaneSharke/SSMSSQLFormatter";

        [Category("4. Support")]
        [DisplayName("Version")]
        public string Version => "1.4.0";
    }
}
