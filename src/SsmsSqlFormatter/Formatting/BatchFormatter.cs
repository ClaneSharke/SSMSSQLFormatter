using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SsmsSqlFormatter.Options;

namespace SsmsSqlFormatter.Formatting
{
    /// <summary>Outcome of a batch-format run: how many files changed, how many were already formatted, and any that couldn't be processed.</summary>
    public class BatchFormatResult
    {
        public int FormattedCount;
        public int UnchangedCount;
        public List<string> Failures = new List<string>();
    }

    /// <summary>
    /// Formats (or, in dry-run mode, just checks) a set of .sql files on disk using the
    /// rule-based engine - never AI, since this must run unattended across many files
    /// with no confirmation prompt or network call per file. Shared by the VSIX's
    /// "Format Files..." command and the CLI's "check"/"format" subcommands. A file's
    /// original encoding (including BOM) is detected and preserved; a file that fails
    /// to parse is left completely untouched and reported back.
    /// </summary>
    public static class BatchFormatter
    {
        /// <summary>
        /// Formats each file in <paramref name="paths"/>. When <paramref name="useFolderConfig"/>
        /// is true (the default), each file's own directory (and its ancestors) is checked
        /// for a .sqlformatter.json that overrides <paramref name="options"/> for that file only -
        /// pass false to use <paramref name="options"/> exactly as given for every file
        /// (e.g. the CLI does this when the caller passed an explicit --config).
        /// </summary>
        public static BatchFormatResult FormatFiles(IEnumerable<string> paths, IFormatterOptions options,
            bool dryRun = false, bool useFolderConfig = true)
        {
            var summary = new BatchFormatResult();
            foreach (var path in paths)
            {
                try
                {
                    string original;
                    Encoding encoding;
                    using (var reader = new StreamReader(path, Encoding.UTF8, true))
                    {
                        original = reader.ReadToEnd();
                        encoding = reader.CurrentEncoding;
                    }

                    var effectiveOptions = useFolderConfig
                        ? FormatterConfigDiscovery.ResolveEffectiveSettings(path, options)
                        : options;

                    var result = ScriptDomFormatter.Format(original, effectiveOptions);
                    if (!result.Success)
                    {
                        summary.Failures.Add(Path.GetFileName(path) + ": " + result.ErrorMessage);
                        continue;
                    }

                    if (result.FormattedSql == original)
                    {
                        summary.UnchangedCount++;
                        continue;
                    }

                    if (!dryRun)
                        File.WriteAllText(path, result.FormattedSql, encoding);
                    summary.FormattedCount++;
                }
                catch (Exception ex)
                {
                    summary.Failures.Add(Path.GetFileName(path) + ": " + ex.Message);
                }
            }
            return summary;
        }
    }
}
