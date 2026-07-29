using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SsmsSqlFormatter.Formatting;
using SsmsSqlFormatter.Options;

namespace SsmsSqlFormatter.Cli
{
    /// <summary>
    /// Standalone command-line entry point for the same rule-based formatter used by
    /// the SSMS extension - built for CI: "check" reports which .sql files would be
    /// reformatted without touching them (for gating a build/PR), "format" applies the
    /// same change the VSIX's "Format Files..." command would. Takes an optional
    /// --config file in the same JSON format produced by "Export Formatter Settings"
    /// inside SSMS, so a team can share one settings file between the IDE and CI.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h" || args[0] == "help")
            {
                PrintUsage();
                return args.Length == 0 ? 1 : 0;
            }

            string command = args[0].ToLowerInvariant();
            if (command != "check" && command != "format")
            {
                Console.Error.WriteLine("Unknown command: " + args[0]);
                PrintUsage();
                return 1;
            }

            if (!TryParseArgs(args, out string configPath, out List<string> pathArgs, out string parseError))
            {
                Console.Error.WriteLine(parseError);
                return 1;
            }

            if (pathArgs.Count == 0)
            {
                Console.Error.WriteLine("No files or directories specified.");
                PrintUsage();
                return 1;
            }

            IFormatterOptions settings;
            try
            {
                settings = configPath != null
                    ? FormatterSettingsSerializer.LoadFromJsonFile<FormatterSettings>(configPath)
                    : new FormatterSettings();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Could not load --config file: " + ex.Message);
                return 1;
            }

            List<string> files;
            try
            {
                files = ResolveSqlFiles(pathArgs);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            if (files.Count == 0)
            {
                Console.WriteLine("No .sql files found.");
                return 0;
            }

            bool dryRun = command == "check";
            // An explicit --config always wins outright; otherwise each file's own
            // directory (and its ancestors) is checked for a .sqlformatter.json.
            bool useFolderConfig = configPath == null;
            var result = BatchFormatter.FormatFiles(files, settings, dryRun, useFolderConfig);

            string verb = dryRun ? "would be formatted" : "formatted";
            Console.WriteLine($"{result.FormattedCount} of {files.Count} file(s) {verb}. " +
                               $"{result.UnchangedCount} already match the current style.");

            if (result.Failures.Count > 0)
            {
                Console.WriteLine($"{result.Failures.Count} file(s) could not be checked:");
                foreach (var f in result.Failures) Console.WriteLine("  " + f);
            }

            if (dryRun)
                return (result.FormattedCount > 0 || result.Failures.Count > 0) ? 1 : 0;

            return result.Failures.Count > 0 ? 1 : 0;
        }

        private static bool TryParseArgs(string[] args, out string configPath, out List<string> pathArgs, out string error)
        {
            configPath = null;
            pathArgs = new List<string>();
            error = null;

            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--config")
                {
                    if (i + 1 >= args.Length)
                    {
                        error = "--config requires a path.";
                        return false;
                    }
                    configPath = args[++i];
                }
                else
                {
                    pathArgs.Add(args[i]);
                }
            }
            return true;
        }

        private static List<string> ResolveSqlFiles(List<string> pathArgs)
        {
            var files = new List<string>();
            foreach (var p in pathArgs)
            {
                if (Directory.Exists(p))
                    files.AddRange(Directory.EnumerateFiles(p, "*.sql", SearchOption.AllDirectories));
                else if (File.Exists(p))
                    files.Add(p);
                else
                    throw new FileNotFoundException("Path not found: " + p);
            }
            return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void PrintUsage()
        {
            Console.WriteLine("ssmssqlfmt - standalone T-SQL formatter (rule-based engine), for CI use.");
            Console.WriteLine();
            Console.WriteLine("Usage: ssmssqlfmt <check|format> <file-or-directory...> [--config settings.json]");
            Console.WriteLine();
            Console.WriteLine("  check   Reports which .sql files would be reformatted, without modifying them.");
            Console.WriteLine("          Exits 1 if any file would change or failed to parse - use to gate a build.");
            Console.WriteLine("  format  Formats matching .sql files on disk in place (same as the VSIX's");
            Console.WriteLine("          'Format Files...' command). Exits 1 only if a file could not be read/written.");
            Console.WriteLine();
            Console.WriteLine("  --config <path>  JSON settings file, same format produced by 'Export Formatter");
            Console.WriteLine("                   Settings' inside SSMS. Uses formatter defaults if omitted.");
            Console.WriteLine();
            Console.WriteLine("A directory argument is searched recursively for *.sql files.");
        }
    }
}
