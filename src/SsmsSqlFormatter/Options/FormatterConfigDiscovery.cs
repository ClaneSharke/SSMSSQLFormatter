using System.IO;

namespace SsmsSqlFormatter.Options
{
    /// <summary>
    /// Finds and applies a folder-level ".sqlformatter.json" settings file - the same
    /// JSON format produced by "Export Formatter Settings" inside SSMS - so a team can
    /// check one file into a repo and have it apply automatically to anyone (or any CI
    /// run) formatting a file under that folder, without each person configuring
    /// Tools > Options individually. Never mutates the caller's own settings object;
    /// every result is a fresh clone.
    /// </summary>
    public static class FormatterConfigDiscovery
    {
        public const string ConfigFileName = ".sqlformatter.json";

        /// <summary>Walks upward from a starting directory looking for a config file, returning its path or null if none is found.</summary>
        public static string FindConfigPath(string startDirectory)
        {
            try
            {
                var dir = string.IsNullOrEmpty(startDirectory) ? null : new DirectoryInfo(startDirectory);
                while (dir != null)
                {
                    var candidate = Path.Combine(dir.FullName, ConfigFileName);
                    if (File.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
            }
            catch
            {
                // Inaccessible/invalid path - behave as if nothing was found.
            }
            return null;
        }

        /// <summary>
        /// Resolves the effective settings for formatting a specific file: <paramref name="baseSettings"/>
        /// overlaid with a discovered .sqlformatter.json (if any) from the file's own
        /// directory upward. Returns <paramref name="baseSettings"/> unchanged if none is
        /// found, or if the file itself can't be read - a bad/missing repo config must
        /// never block formatting.
        /// </summary>
        public static IFormatterOptions ResolveEffectiveSettings(string filePath, IFormatterOptions baseSettings)
        {
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
                var configPath = FindConfigPath(dir);
                if (configPath == null) return baseSettings;

                return FormatterSettingsSerializer.CloneAndApplyJson(baseSettings, File.ReadAllText(configPath));
            }
            catch
            {
                return baseSettings;
            }
        }
    }
}
