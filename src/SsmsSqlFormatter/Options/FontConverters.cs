using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SsmsSqlFormatter.Options
{
    /// <summary>
    /// Provides a dropdown of standard Windows fonts for the options grid,
    /// filtered to those actually installed so the list can never offer a font
    /// the machine doesn't have. Typing a different name is still allowed for
    /// corporate or custom fonts.
    /// </summary>
    public class FontNameConverter : StringConverter
    {
        private static string[] _cache;

        /// <summary>Fonts that ship with Windows and are sensible for spreadsheets.</summary>
        private static readonly string[] StandardWindowsFonts =
        {
            "Aptos",
            "Arial",
            "Arial Narrow",
            "Calibri",
            "Cambria",
            "Candara",
            "Consolas",
            "Constantia",
            "Corbel",
            "Courier New",
            "Franklin Gothic Book",
            "Georgia",
            "Lucida Console",
            "Lucida Sans Unicode",
            "Palatino Linotype",
            "Segoe UI",
            "Tahoma",
            "Times New Roman",
            "Trebuchet MS",
            "Verdana"
        };

        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;

        /// <summary>False so a font outside the list can still be typed in.</summary>
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => false;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            => new StandardValuesCollection(InstalledStandardFonts());

        private static string[] InstalledStandardFonts()
        {
            if (_cache != null) return _cache;

            try
            {
                var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var family in System.Drawing.FontFamily.Families)
                    installed.Add(family.Name);

                var available = new List<string>();
                foreach (var name in StandardWindowsFonts)
                    if (installed.Contains(name))
                        available.Add(name);

                _cache = available.Count > 0 ? available.ToArray() : StandardWindowsFonts;
            }
            catch
            {
                // Font enumeration can fail in constrained environments; the
                // standard list is a safe fallback.
                _cache = StandardWindowsFonts;
            }

            return _cache;
        }
    }

    /// <summary>Common point sizes as a dropdown; other values can still be typed.</summary>
    public class FontSizeConverter : Int32Converter
    {
        private static readonly object[] Sizes = { 8, 9, 10, 11, 12, 14, 16, 18, 20, 24 };

        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;

        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => false;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            => new StandardValuesCollection(Sizes);
    }
}
