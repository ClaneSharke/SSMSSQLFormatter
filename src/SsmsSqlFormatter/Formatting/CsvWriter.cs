using System.IO;
using System.Text;

namespace SsmsSqlFormatter.Formatting
{
    /// <summary>
    /// Writes the captured tab-separated results to a CSV file, quoting fields
    /// per RFC 4180 so separators, quotes and newlines inside values survive.
    /// </summary>
    internal static class CsvWriter
    {
        public static void Write(string path, System.Collections.Generic.IList<string> tsvs,
                                 char separator, bool nullsAsEmpty)
        {
            var sb = new StringBuilder();

            for (int s = 0; s < tsvs.Count; s++)
            {
                if (s > 0) sb.AppendLine();   // blank line between result sets

                var lines = (tsvs[s] ?? "").Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
                foreach (var line in lines)
                {
                    var cells = line.Split('\t');
                    for (int c = 0; c < cells.Length; c++)
                    {
                        if (c > 0) sb.Append(separator);
                        string value = cells[c] ?? "";
                        if (nullsAsEmpty && value == "NULL") value = "";
                        sb.Append(Quote(value, separator));
                    }
                    sb.AppendLine();
                }
            }

            // UTF-8 with BOM so Excel opens non-ASCII characters correctly.
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static string Quote(string value, char separator)
        {
            bool needsQuotes = value.IndexOf(separator) >= 0
                            || value.IndexOf('"') >= 0
                            || value.IndexOf('\n') >= 0
                            || value.IndexOf('\r') >= 0;
            if (!needsQuotes) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
