using System;
using System.Text;

namespace SsmsSqlFormatter.Formatting
{
    /// <summary>
    /// Converts the tab-separated text that SSMS's results grid places on the
    /// clipboard ("Copy with Headers") into CF_HTML - the clipboard format Excel
    /// treats as a real table. Optionally forces every data cell to Text format
    /// so leading zeros, long IDs, and account numbers survive the paste.
    /// </summary>
    public static class ExcelClipboard
    {
        public static string BuildCfHtml(string tsv, bool forceTextCells, bool nullsAsEmpty,
                                         bool firstRowIsHeader,
                                         out int rowCount, out int colCount)
        {
            var lines = tsv.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            var sb = new StringBuilder(tsv.Length * 2);
            sb.Append("<table border=\"1\" style=\"border-collapse:collapse;font-family:Calibri,Arial,sans-serif;font-size:11pt;\">");

            string tdStyle = forceTextCells
                ? " style=\"mso-number-format:'\\@';padding:2px 6px;\""
                : " style=\"padding:2px 6px;\"";
            const string thStyle = " style=\"background:#D9E1F2;font-weight:bold;padding:2px 6px;\"";

            rowCount = 0;
            colCount = 0;

            for (int r = 0; r < lines.Length; r++)
            {
                var cells = lines[r].Split('\t');
                if (cells.Length > colCount) colCount = cells.Length;
                bool header = firstRowIsHeader && r == 0;
                sb.Append("<tr>");
                foreach (var raw in cells)
                {
                    string value = raw;
                    if (nullsAsEmpty && value == "NULL") value = "";
                    string tag = header ? "th" : "td";
                    string style = header ? thStyle : tdStyle;
                    sb.Append('<').Append(tag).Append(style).Append('>')
                      .Append(HtmlEncode(value))
                      .Append("</").Append(tag).Append('>');
                }
                sb.Append("</tr>");
                rowCount++;
            }
            sb.Append("</table>");

            return WrapCfHtml(sb.ToString());
        }

        private static string HtmlEncode(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        /// <summary>
        /// Wraps an HTML fragment in the CF_HTML clipboard envelope, whose header
        /// must contain exact BYTE offsets (UTF-8) of the html and fragment bounds.
        /// </summary>
        private static string WrapCfHtml(string fragment)
        {
            const string headerFormat =
                "Version:1.0\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
            const string htmlStart = "<html><body>\r\n<!--StartFragment-->";
            const string htmlEnd = "<!--EndFragment-->\r\n</body></html>";

            // Header length is constant because the numbers are zero-padded to 10 digits.
            int headerLen = Encoding.UTF8.GetByteCount(string.Format(headerFormat, 0, 0, 0, 0));
            int startHtml = headerLen;
            int startFragment = startHtml + Encoding.UTF8.GetByteCount(htmlStart);
            int endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
            int endHtml = endFragment + Encoding.UTF8.GetByteCount(htmlEnd);

            return string.Format(headerFormat, startHtml, endHtml, startFragment, endFragment)
                   + htmlStart + fragment + htmlEnd;
        }
    }
}
