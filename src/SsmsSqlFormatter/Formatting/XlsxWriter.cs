using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace SsmsSqlFormatter.Formatting
{
    /// <summary>
    /// Writes tab-separated results to a genuine .xlsx workbook (Office Open XML)
    /// with the user's styling - no Excel installation, no libraries, no
    /// "file format and extension don't match" warning. An .xlsx file is a zip
    /// of small XML parts, all generated here.
    /// </summary>
    internal static class XlsxWriter
    {
        public static void Write(string path, string tsv, ExcelStyle style)
        {
            if (style == null) style = new ExcelStyle();

            var lines = tsv.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            int colCount = 0;
            var rows = new string[lines.Length][];
            for (int r = 0; r < lines.Length; r++)
            {
                rows[r] = lines[r].Split('\t');
                if (rows[r].Length > colCount) colCount = rows[r].Length;
            }

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                AddEntry(zip, "[Content_Types].xml", ContentTypesXml());
                AddEntry(zip, "_rels/.rels", RootRelsXml());
                AddEntry(zip, "xl/workbook.xml", WorkbookXml());
                AddEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRelsXml());
                AddEntry(zip, "xl/styles.xml", StylesXml(style));
                AddEntry(zip, "xl/worksheets/sheet1.xml", SheetXml(rows, colCount, style));
            }
        }

        private static void AddEntry(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                writer.Write(content);
        }

        private static string ContentTypesXml() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            "</Types>";

        private static string RootRelsXml() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>";

        private static string WorkbookXml() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheets><sheet name=\"Results\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";

        private static string WorkbookRelsXml() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "</Relationships>";

        private static string StylesXml(ExcelStyle style)
        {
            string font = XmlEscape(SanitizeFont(style.FontName));
            int size = style.FontSize < 6 || style.FontSize > 72 ? 11 : style.FontSize;
            string headerBack = Argb(style.HeaderBackColor, "FFD9E1F2");
            string headerText = Argb(style.HeaderTextColor, "FF000000");
            string borderCol = Argb(style.BorderColor, "FFB4C6E7");
            string bandCol = Argb(style.BandColor, "FFF2F6FC");
            int borderId = style.ShowBorders ? 1 : 0;
            string headerBold = style.HeaderBold ? "<b/>" : "";

            return
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<fonts count=\"2\">" +
            $"<font><sz val=\"{size}\"/><name val=\"{font}\"/></font>" +
            $"<font>{headerBold}<sz val=\"{size}\"/><color rgb=\"{headerText}\"/><name val=\"{font}\"/></font>" +
            "</fonts>" +
            "<fills count=\"4\">" +
            "<fill><patternFill patternType=\"none\"/></fill>" +
            "<fill><patternFill patternType=\"gray125\"/></fill>" +
            $"<fill><patternFill patternType=\"solid\"><fgColor rgb=\"{headerBack}\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            $"<fill><patternFill patternType=\"solid\"><fgColor rgb=\"{bandCol}\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "</fills>" +
            "<borders count=\"2\">" +
            "<border><left/><right/><top/><bottom/><diagonal/></border>" +
            "<border>" +
            $"<left style=\"thin\"><color rgb=\"{borderCol}\"/></left>" +
            $"<right style=\"thin\"><color rgb=\"{borderCol}\"/></right>" +
            $"<top style=\"thin\"><color rgb=\"{borderCol}\"/></top>" +
            $"<bottom style=\"thin\"><color rgb=\"{borderCol}\"/></bottom>" +
            "<diagonal/></border>" +
            "</borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"6\">" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            // 1: header
            $"<xf numFmtId=\"49\" fontId=\"1\" fillId=\"2\" borderId=\"{borderId}\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" +
            // 2: data, text format
            $"<xf numFmtId=\"49\" fontId=\"0\" fillId=\"0\" borderId=\"{borderId}\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\"/>" +
            // 3: data, text format, banded
            $"<xf numFmtId=\"49\" fontId=\"0\" fillId=\"3\" borderId=\"{borderId}\" xfId=\"0\" applyNumberFormat=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" +
            // 4: data, general
            $"<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"{borderId}\" xfId=\"0\" applyBorder=\"1\"/>" +
            // 5: data, general, banded
            $"<xf numFmtId=\"0\" fontId=\"0\" fillId=\"3\" borderId=\"{borderId}\" xfId=\"0\" applyFill=\"1\" applyBorder=\"1\"/>" +
            "</cellXfs>" +
            "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
            "</styleSheet>";
        }

        private static string SheetXml(string[][] rows, int colCount, ExcelStyle style)
        {
            var sb = new StringBuilder(1024 + rows.Length * 64);
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
              .Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");

            // Approximate column widths from content (capped), so results are readable.
            var widths = new int[colCount];
            int scanRows = Math.Min(rows.Length, 500);
            for (int r = 0; r < scanRows; r++)
                for (int c = 0; c < rows[r].Length; c++)
                    if (rows[r][c] != null && rows[r][c].Length > widths[c])
                        widths[c] = rows[r][c].Length;

            sb.Append("<cols>");
            for (int c = 0; c < colCount; c++)
            {
                double w = Math.Max(9, Math.Min(50, widths[c] * 1.1 + 2));
                sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{w.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
            }
            sb.Append("</cols><sheetData>");

            int dataRow = 0;
            for (int r = 0; r < rows.Length; r++)
            {
                bool header = style.FirstRowIsHeader && r == 0;
                bool band = !header && style.BandedRows && (dataRow % 2 == 1);
                if (!header) dataRow++;

                int styleIdx = header ? 1
                    : style.ForceTextCells ? (band ? 3 : 2)
                    : (band ? 5 : 4);

                sb.Append("<row r=\"").Append(r + 1).Append("\">");
                for (int c = 0; c < rows[r].Length; c++)
                {
                    string value = rows[r][c] ?? "";
                    if (style.NullsAsEmpty && value == "NULL") value = "";
                    string cellRef = ColumnRef(c) + (r + 1);

                    if (!header && !style.ForceTextCells && IsPlainNumber(value))
                    {
                        sb.Append($"<c r=\"{cellRef}\" s=\"{styleIdx}\"><v>{value}</v></c>");
                    }
                    else
                    {
                        sb.Append($"<c r=\"{cellRef}\" s=\"{styleIdx}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                          .Append(XmlEscape(value))
                          .Append("</t></is></c>");
                    }
                }
                sb.Append("</row>");
            }

            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }

        /// <summary>Numeric enough for Excel without losing information: no leading zeros, within double precision.</summary>
        private static bool IsPlainNumber(string v)
        {
            if (string.IsNullOrEmpty(v) || v.Length > 15) return false;
            if (!Regex.IsMatch(v, @"^-?\d+(\.\d+)?$")) return false;
            string digits = v.StartsWith("-") ? v.Substring(1) : v;
            if (digits.Length > 1 && digits[0] == '0' && digits[1] != '.') return false;
            return true;
        }

        private static string ColumnRef(int index)
        {
            var sb = new StringBuilder();
            index++;
            while (index > 0)
            {
                int rem = (index - 1) % 26;
                sb.Insert(0, (char)('A' + rem));
                index = (index - 1) / 26;
            }
            return sb.ToString();
        }

        private static string Argb(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            value = value.Trim().TrimStart('#');
            if (Regex.IsMatch(value, "^[0-9A-Fa-f]{6}$")) return "FF" + value.ToUpperInvariant();
            switch (value.ToLowerInvariant())
            {
                case "black": return "FF000000";
                case "white": return "FFFFFFFF";
                case "red": return "FFFF0000";
                case "green": return "FF008000";
                case "blue": return "FF0000FF";
                case "gray": case "grey": return "FF808080";
                case "lightgray": case "lightgrey": return "FFD3D3D3";
                case "yellow": return "FFFFFF00";
                case "orange": return "FFFFA500";
                case "navy": return "FF000080";
                default: return fallback;
            }
        }

        private static string SanitizeFont(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Calibri";
            value = value.Trim();
            return Regex.IsMatch(value, "^[A-Za-z0-9 ]{1,40}$") ? value : "Calibri";
        }

        private static string XmlEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;");
        }
    }
}
