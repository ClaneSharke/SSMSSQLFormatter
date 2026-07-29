using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SsmsSqlFormatter.Options;

namespace SsmsSqlFormatter.Formatting
{
    /// <summary>
    /// Formats T-SQL via the Anthropic Messages API using the user's own API key.
    /// Advantages over the rule-based engine: preserves comments, follows free-form
    /// style instructions, and handles vendor-specific constructs gracefully.
    /// </summary>
    public static class AiFormatter
    {
        private static readonly HttpClient Http = new HttpClient();

        public static async Task<FormatResult> FormatAsync(string sql, GeneralOptions general, AiOptions ai)
        {
            var result = new FormatResult();

            if (string.IsNullOrWhiteSpace(ai.ApiKey))
            {
                result.ErrorMessage = "No Anthropic API key configured. Set it under Tools > Options > Format T-SQL Script > AI Engine.";
                return result;
            }

            var systemPrompt = BuildSystemPrompt(general, ai);

            var payload = new JObject
            {
                ["model"] = ai.Model,
                ["max_tokens"] = Math.Max(1024, ai.MaxTokens),
                ["system"] = systemPrompt,
                ["messages"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = "Format this T-SQL script:\n\n" + sql
                    }
                }
            };

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, ai.Endpoint))
                {
                    request.Headers.Add("x-api-key", ai.ApiKey);
                    request.Headers.Add("anthropic-version", "2023-06-01");
                    request.Content = new StringContent(payload.ToString(Formatting_None()), Encoding.UTF8, "application/json");

                    using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(10, ai.TimeoutSeconds))))
                    using (var response = await Http.SendAsync(request, cts.Token).ConfigureAwait(false))
                    {
                        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        if (!response.IsSuccessStatusCode)
                        {
                            var apiError = TryGetApiError(body);
                            result.ErrorMessage = $"Anthropic API error ({(int)response.StatusCode}): {apiError}";
                            return result;
                        }

                        var json = JObject.Parse(body);
                        var text = ParseModelOutput(json);
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            result.ErrorMessage = "The AI returned an empty response.";
                            return result;
                        }
                        text = StripCodeFences(text.Trim());

                        result.FormattedSql = text;
                        result.Success = true;
                        return result;
                    }
                }
            }
            catch (TaskCanceledException)
            {
                result.ErrorMessage = $"The AI request timed out after {ai.TimeoutSeconds}s. Increase the timeout or reduce script size.";
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = "AI request failed: " + ex.Message;
                return result;
            }
        }

        internal static string BuildSystemPrompt(IFormatterOptions general, AiOptions ai)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are a T-SQL code formatter. You receive a T-SQL script and return the SAME script, reformatted.");
            sb.AppendLine("Hard rules:");
            sb.AppendLine("- Do NOT change the logic, identifiers, literals, or semantics in any way.");
            sb.AppendLine("- PRESERVE all comments exactly, keeping them attached to the code they describe.");
            sb.AppendLine("- Return ONLY the formatted SQL. No explanations, no markdown code fences, no commentary.");
            sb.AppendLine("- If part of the script is not valid T-SQL, leave that part untouched and format the rest.");

            if (ai.UseGeneralOptionsAsStyleGuide)
            {
                sb.AppendLine();
                sb.AppendLine("Style guide: " + ScriptDomFormatter.DescribeStyle(general));
            }

            if (!string.IsNullOrWhiteSpace(ai.CustomInstructions))
            {
                sb.AppendLine();
                sb.AppendLine("Additional user style instructions (follow these, but never violate the hard rules above):");
                sb.AppendLine(ai.CustomInstructions);
            }

            return sb.ToString();
        }

        internal static string ParseModelOutput(JObject json)
        {
            if (json["content"] is JArray contentArray)
            {
                var sb = new StringBuilder();
                foreach (var block in contentArray)
                {
                    if ((string)block["type"] == "text")
                        sb.Append((string)block["text"] ?? string.Empty);
                }

                if (sb.Length > 0)
                    return sb.ToString();
            }

            if (json["completion"] != null)
            {
                return (string)json["completion"] ?? string.Empty;
            }

            if (json["response"] != null)
            {
                return (string)json["response"] ?? string.Empty;
            }

            return string.Empty;
        }

        internal static string StripCodeFences(string text)
        {
            const string tripleFence = "```";
            if (text.StartsWith(tripleFence, StringComparison.Ordinal))
            {
                int firstNewline = text.IndexOf('\n');
                if (firstNewline >= 0)
                    text = text.Substring(firstNewline + 1);
                int lastFence = text.LastIndexOf(tripleFence, StringComparison.Ordinal);
                if (lastFence >= 0)
                    text = text.Substring(0, lastFence);
            }

            if (text.StartsWith("`", StringComparison.Ordinal) && text.EndsWith("`", StringComparison.Ordinal))
            {
                text = text.Trim('`').Trim();
            }

            return text.Trim();
        }

        internal static string TryGetApiError(string body)
        {
            try
            {
                var json = JObject.Parse(body);
                return (string)json["error"]?["message"] ?? body;
            }
            catch
            {
                return body;
            }
        }

        private static Newtonsoft.Json.Formatting Formatting_None() => Newtonsoft.Json.Formatting.None;
    }
}
