using System;
using System.Text;
using System.Text.RegularExpressions;

namespace SsmsSqlFormatter.Formatting
{
    /// <summary>Visual styling for the Excel table produced by <see cref="ExcelClipboard"/>.</summary>
    public class ExcelStyle
    {
        public bool FirstRowIsHeader = true;
        public bool ForceTextCells = true;
        public bool NullsAsEmpty = false;

        public string FontName = "Calibri";
        public int FontSize = 11;

        public bool HeaderBold = true;
        public string HeaderBackColor = "#D9E1F2";
        public string HeaderTextColor = "#000000";

        public bool ShowBorders = true;
        public string BorderColor = "#B4C6E7";

        public bool BandedRows = false;
        public string BandColor = "#F2F6FC";
    }

    /// <summary>
    /// Converts the tab-separated text that SSMS's results grid places on the
    /// clipboard ("Copy with Headers") into CF_HTML - the clipboard format Excel
    /// treats as a real table. Optionally forces every data cell to Text format
    /// so leading zeros, long IDs, and account numbers survive the paste.
    /// </summary>
    public static class ExcelClipboard
    {
        public static string BuildCfHtml(string tsv, ExcelStyle style,
                                         out int rowCount, out int colCount)
        {
            return WrapCfHtml(BuildTableHtml(tsv, style, out rowCount, out colCount));
        }

        /// <summary>Builds a standalone HTML document Excel can open directly as a workbook.</summary>
        public static string BuildHtmlDocument(string tsv, ExcelStyle style,
                                               out int rowCount, out int colCount)
        {
            string table = BuildTableHtml(tsv, style, out rowCount, out colCount);
            return "<html><head><meta charset=\"utf-8\">" +
                   "<!--[if gte mso 9]><xml><x:ExcelWorkbook><x:ExcelWorksheets><x:ExcelWorksheet>" +
                   "<x:Name>Results</x:Name><x:WorksheetOptions><x:DisplayGridlines/>" +
                   "</x:WorksheetOptions></x:ExcelWorksheet></x:ExcelWorksheets></x:ExcelWorkbook></xml><![endif]-->" +
                   "</head><body>" + table + "</body></html>";
        }

        private static string BuildTableHtml(string tsv, ExcelStyle style,
                                             out int rowCount, out int colCount)
        {
            if (style == null) style = new ExcelStyle();

            string font = SanitizeFont(style.FontName);
            int size = style.FontSize < 6 || style.FontSize > 72 ? 11 : style.FontSize;
            string headerBack = SanitizeColor(style.HeaderBackColor, "#D9E1F2");
            string headerText = SanitizeColor(style.HeaderTextColor, "#000000");
            string borderCol = SanitizeColor(style.BorderColor, "#B4C6E7");
            string bandCol = SanitizeColor(style.BandColor, "#F2F6FC");

            string cellBorder = style.ShowBorders
                ? $"border:1px solid {borderCol};" : "border:none;";

            var lines = tsv.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            var sb = new StringBuilder(tsv.Length * 2);

            sb.Append("<table style=\"border-collapse:collapse;")
              .Append("font-family:").Append(font).Append(",Arial,sans-serif;")
              .Append("font-size:").Append(size).Append("pt;\">");

            string numberFormat = style.ForceTextCells ? "mso-number-format:'\\@';" : "";

            rowCount = 0;
            colCount = 0;
            int dataRow = 0;

            for (int r = 0; r < lines.Length; r++)
            {
                var cells = lines[r].Split('\t');
                if (cells.Length > colCount) colCount = cells.Length;

                bool header = style.FirstRowIsHeader && r == 0;
                string rowStyle;

                if (header)
                {
                    rowStyle = $"background:{headerBack};color:{headerText};"
                             + (style.HeaderBold ? "font-weight:bold;" : "")
                             + cellBorder + "padding:2px 6px;";
                }
                else
                {
                    bool band = style.BandedRows && (dataRow % 2 == 1);
                    rowStyle = (band ? $"background:{bandCol};" : "")
                             + numberFormat + cellBorder + "padding:2px 6px;";
                    dataRow++;
                }

                string tag = header ? "th" : "td";
                sb.Append("<tr>");
                foreach (var raw in cells)
                {
                    string value = raw;
                    if (style.NullsAsEmpty && value == "NULL") value = "";
                    sb.Append('<').Append(tag).Append(" style=\"").Append(rowStyle).Append("\">")
                      .Append(HtmlEncode(value))
                      .Append("</").Append(tag).Append('>');
                }
                sb.Append("</tr>");
                rowCount++;
            }
            sb.Append("</table>");

            return sb.ToString();
        }

        /// <summary>Accepts "#RRGGBB", "RRGGBB", or a plain CSS colour name; falls back when invalid.</summary>
        private static string SanitizeColor(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            value = value.Trim();
            if (Regex.IsMatch(value, "^#?[0-9A-Fa-f]{6}$"))
                return value.StartsWith("#") ? value : "#" + value;
            if (Regex.IsMatch(value, "^[A-Za-z]{3,20}$"))
                return value;
            return fallback;
        }

        private static string SanitizeFont(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Calibri";
            value = value.Trim();
            return Regex.IsMatch(value, "^[A-Za-z0-9 ]{1,40}$") ? value : "Calibri";
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
