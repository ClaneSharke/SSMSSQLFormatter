using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace SsmsSqlFormatter.Options
{
    public enum AiProvider
    {
        Anthropic,
        Copilot
    }

    public class AiOptions : DialogPage
    {
        private const string ApiKeyCredentialTarget = "SsmsSqlFormatter:AnthropicApiKey";

        [Category("1. Connection")]
        [DisplayName("Provider")]
        [Description("Select which AI backend should format the SQL. Anthropic uses the Messages API; Copilot uses a Copilot-style chat endpoint.")]
        public AiProvider Provider { get; set; } = AiProvider.Anthropic;

        [Category("1. Connection")]
        [DisplayName("API key / token")]
        [Description("Your Anthropic API key (sk-ant-...) or Copilot bearer token. Stored securely in Windows Credential Manager for the current user (not in plain text).")]
        [PasswordPropertyText(true)]
        public string ApiKey { get; set; } = string.Empty;

        [Category("1. Connection")]
        [DisplayName("Model")]
        [Description("Model ID to use for formatting. Anthropic examples: claude-sonnet-4-5. Copilot examples: gpt-4.1 or another chat model supported by your endpoint.")]
        public string Model { get; set; } = "claude-sonnet-4-5";

        [Category("1. Connection")]
        [DisplayName("API endpoint")]
        [Description("Endpoint for the selected provider. Anthropic default is https://api.anthropic.com/v1/messages; Copilot commonly uses a compatible chat endpoint.")]
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

        /// <summary>
        /// Loads the other settings normally, then substitutes the real API key from
        /// Windows Credential Manager. A non-empty key surviving from the registry-backed
        /// store at this point is a legacy plain-text key from an older version - it gets
        /// migrated into Credential Manager once and used for this load.
        /// </summary>
        public override void LoadSettingsFromStorage()
        {
            base.LoadSettingsFromStorage();

            string legacyPlainTextKey = ApiKey;
            string stored = CredentialVault.TryLoad(ApiKeyCredentialTarget);
            if (stored != null)
            {
                ApiKey = stored;
            }
            else if (!string.IsNullOrEmpty(legacyPlainTextKey))
            {
                if (CredentialVault.TrySave(ApiKeyCredentialTarget, legacyPlainTextKey))
                    ApiKey = legacyPlainTextKey; // migrated; the next save blanks the registry copy
            }
        }

        /// <summary>
        /// Saves the API key to Credential Manager and blanks it out of the
        /// registry-backed store before saving everything else - so the key never
        /// lands in plain text. If Credential Manager is unavailable, falls back to
        /// the old plain-text storage rather than silently losing the key.
        /// </summary>
        public override void SaveSettingsToStorage()
        {
            string realKey = ApiKey;
            bool savedSecurely = CredentialVault.TrySave(ApiKeyCredentialTarget, realKey);
            try
            {
                ApiKey = savedSecurely ? string.Empty : realKey;
                base.SaveSettingsToStorage();
            }
            finally
            {
                ApiKey = realKey;
            }
        }
    }
}
