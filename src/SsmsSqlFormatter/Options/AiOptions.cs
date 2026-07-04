using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace SsmsSqlFormatter.Options
{
    public class AiOptions : DialogPage
    {
        [Category("1. Connection")]
        [DisplayName("Anthropic API key")]
        [Description("Your Anthropic API key (sk-ant-...). NOTE: stored in the SSMS settings registry hive in plain text — do not use on shared machines. Get a key at console.anthropic.com.")]
        [PasswordPropertyText(true)]
        public string ApiKey { get; set; } = string.Empty;

        [Category("1. Connection")]
        [DisplayName("Model")]
        [Description("Anthropic model ID to use for formatting.")]
        public string Model { get; set; } = "claude-sonnet-4-5";

        [Category("1. Connection")]
        [DisplayName("API endpoint")]
        public string Endpoint { get; set; } = "https://api.anthropic.com/v1/messages";

        [Category("1. Connection")]
        [DisplayName("Max output tokens")]
        [Description("Upper limit for the formatted script size. Increase for very large scripts.")]
        public int MaxTokens { get; set; } = 16000;

        [Category("1. Connection")]
        [DisplayName("Timeout (seconds)")]
        public int TimeoutSeconds { get; set; } = 120;

        [Category("2. Behaviour")]
        [DisplayName("Custom style instructions")]
        [Description("Extra instructions appended to the formatting prompt, e.g. 'leading commas', 'align equals signs in SET clauses', 'keep short CASE expressions on one line'.")]
        public string CustomInstructions { get; set; } = string.Empty;

        [Category("2. Behaviour")]
        [DisplayName("Send General options as style guide")]
        [Description("When enabled, your rule-based settings (casing, indent, line breaks) are translated into the AI prompt so both engines produce a consistent style.")]
        public bool UseGeneralOptionsAsStyleGuide { get; set; } = true;

        [Category("2. Behaviour")]
        [DisplayName("Fall back to rule-based on error")]
        [Description("If the AI call fails (no network, bad key, timeout), silently format with the rule-based engine instead.")]
        public bool FallbackToRuleBased { get; set; } = true;

        [Category("3. Privacy")]
        [DisplayName("Confirm before sending script")]
        [Description("Ask for confirmation before sending SQL to the Anthropic API. Recommended: scripts may contain table names, literals, or embedded data you consider sensitive.")]
        public bool ConfirmBeforeSending { get; set; } = true;
    }
}
