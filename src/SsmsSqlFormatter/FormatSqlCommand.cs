using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Threading;
using System.Windows;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using SsmsSqlFormatter.Formatting;
using SsmsSqlFormatter.Options;
using Task = System.Threading.Tasks.Task;

namespace SsmsSqlFormatter
{
    /// <summary>
    /// The "Format T-SQL Script" command. Formats the current selection,
    /// or the whole document when nothing is selected.
    /// </summary>
    internal sealed class FormatSqlCommand
    {
        public static readonly Guid CommandSet = new Guid("c8d2f6a4-7e19-4b3c-a5d8-0f9e6b3c2a71");
        public const int CommandId = 0x0100;
        public const int ContextCommandId = 0x0101;
        public const int HelpCommandId = 0x0102;
        public const int CopyExcelCommandId = 0x0103;
        public const int CopyExcelContextCommandId = 0x0104;
        public const int OpenExcelCommandId = 0x0105;
        public const int AddSheetCommandId = 0x0106;
        public const int ExportSettingsCommandId = 0x0107;
        public const int ImportSettingsCommandId = 0x0108;
        public const int PreviewFormatCommandId = 0x0109;
        public const int BatchFormatCommandId = 0x010A;
        public const int FormatAllOpenCommandId = 0x010B;
        public const int ExpandSelectStarCommandId = 0x010C;

        private readonly SsmsSqlFormatterPackage _package;

        private FormatSqlCommand(SsmsSqlFormatterPackage package, OleMenuCommandService commandService)
        {
            _package = package;
            commandService.AddCommand(new MenuCommand(Execute, new CommandID(CommandSet, CommandId)));
            commandService.AddCommand(new MenuCommand(Execute, new CommandID(CommandSet, ContextCommandId)));
            commandService.AddCommand(new MenuCommand(ExecuteHelp, new CommandID(CommandSet, HelpCommandId)));
            commandService.AddCommand(new MenuCommand(ExecuteCopyExcel, new CommandID(CommandSet, CopyExcelCommandId)));
            commandService.AddCommand(new MenuCommand(ExecuteCopyExcel, new CommandID(CommandSet, CopyExcelContextCommandId)));
            commandService.AddCommand(new MenuCommand(ExecuteOpenExcel, new CommandID(CommandSet, OpenExcelCommandId)));
            commandService.AddCommand(new MenuCommand(ExecuteAddSheet, new CommandID(CommandSet, AddSheetCommandId)));
            commandService.AddCommand(new MenuCommand(ExecuteExportSettings, new CommandID(CommandSet, ExportSettingsCommandId)));
            commandService.AddCommand(new MenuCommand(ExecuteImportSettings, new CommandID(CommandSet, ImportSettingsCommandId)));
            commandService.AddCommand(new MenuCommand(ExecutePreviewFormat, new CommandID(CommandSet, PreviewFormatCommandId)));
            commandService.AddCommand(new MenuCommand(ExecuteBatchFormat, new CommandID(CommandSet, BatchFormatCommandId)));
            commandService.AddCommand(new MenuCommand(ExecuteFormatAllOpen, new CommandID(CommandSet, FormatAllOpenCommandId)));
            commandService.AddCommand(new MenuCommand(ExecuteExpandSelectStar, new CommandID(CommandSet, ExpandSelectStarCommandId)));
        }

        /// <summary>
        /// Best-effort column-lookup delegate backed by the active query window's own
        /// connection (see Options/SsmsConnectionDiscovery.cs), or null if no connection
        /// could be determined - callers treat that as "SELECT * expansion unavailable right
        /// now" and either skip it (main Format command) or tell the user why (the dedicated
        /// Expand SELECT * command).
        /// </summary>
        private static Func<string, string, string, System.Threading.Tasks.Task<List<string>>> BuildSelectStarColumnLookup()
        {
            string connectionString = SsmsConnectionDiscovery.TryGetActiveConnectionString();
            if (connectionString == null) return null;
            return (database, schema, table) => SqlSchemaLookup.GetColumnsAsync(connectionString, database, schema, table);
        }

        // Result sets queued by "Add Results as Sheet", exported together as one workbook.
        private static readonly System.Collections.Generic.List<string> PendingSheets =
            new System.Collections.Generic.List<string>();

        /// <summary>
        /// Captures the results grid via the grid's own copy command, using the
        /// clipboard purely as an invisible transport channel. Freshness is
        /// enforced: the clipboard is cleared (best effort) before the copy, and
        /// content is only accepted when it changed or clearly looks like grid
        /// data - so stale clipboard content (URLs, text from other apps) can
        /// never be exported by mistake.
        /// </summary>
        private async System.Threading.Tasks.Task<string> AcquireResultsAsync(Options.GeneralOptions general)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            string before = ReadClipboardText();

            if (general.ExcelSimulateCopyFirst)
            {
                try { System.Windows.Forms.Clipboard.Clear(); } catch { /* best effort */ }

                // CRITICAL: the synthesised keystroke is delivered through the
                // Windows message queue, which only SSMS's UI thread can drain.
                // Every wait here must be an AWAIT (which yields and lets the
                // message pump run) and never Thread.Sleep, which would block the
                // pump so the grid never processes the keystroke at all.
                try
                {
                    ReleaseModifierKeys();
                    await System.Threading.Tasks.Task.Delay(60);
                    SendCopyKeystroke(withShift: true);
                    await System.Threading.Tasks.Task.Delay(450);
                }
                catch { /* fall through */ }
            }

            string after = ReadClipboardText();

            // Some grids/builds only respond to plain copy; if nothing fresh
            // arrived, try Ctrl+C once before giving up.
            if (general.ExcelSimulateCopyFirst &&
                (string.IsNullOrWhiteSpace(after) || (after == before && !LooksLikeGridData(after))))
            {
                try
                {
                    ReleaseModifierKeys();
                    await System.Threading.Tasks.Task.Delay(60);
                    SendCopyKeystroke(withShift: false);
                    await System.Threading.Tasks.Task.Delay(450);
                    after = ReadClipboardText();
                }
                catch { /* keep what we have */ }
            }

            string text;

            if (!string.IsNullOrWhiteSpace(after) && after != before)
            {
                // Fresh content produced by the copy we just triggered.
                text = after;
            }
            else if (!string.IsNullOrWhiteSpace(after) && LooksLikeGridData(after))
            {
                // Unchanged, but unmistakably tabular - a repeat export of the
                // same selection.
                text = after;
            }
            else
            {
                ShowInfo(
                    "No result data was captured, so nothing was exported.\r\n\r\n" +
                    "The capture works by sending a copy keystroke to the focused " +
                    "control, so the results grid must have focus:\r\n" +
                    "  - Click inside the results grid, press Ctrl+A, then use the " +
                    "KEYBOARD shortcut Ctrl+Shift+Alt+X.\r\n" +
                    "  - Using a toolbar button or menu instead? Copy the grid " +
                    "yourself first (Ctrl+A, then Ctrl+Shift+C), then click the " +
                    "button - your copy will be used.");
                return null;
            }

            if (LooksLikeActiveQueryText(text))
            {
                ShowInfo(
                    "That looks like the query text, not the results - the query " +
                    "editor had focus when the copy ran.\r\n\r\n" +
                    "Click anywhere inside the results grid first (Ctrl+A selects all " +
                    "cells), then run this command again.");
                return null;
            }

            return text;
        }

        /// <summary>Tab-separated or multi-line content - the shape of a grid copy.</summary>
        internal static bool LooksLikeGridData(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (s.IndexOf('\t') >= 0) return true;
            return s.TrimEnd('\n', '\r').IndexOf('\n') >= 0;
        }


        /// <summary>
        /// Runs an export action shortly AFTER the invoking click/keystroke has
        /// fully completed. When a toolbar button or menu item is clicked, focus
        /// belongs to the toolbar while the command handler runs and only returns
        /// to the results grid afterwards - capturing immediately would send the
        /// copy keystroke into the toolbar. Deferring lets focus settle first.
        /// </summary>
        private void RunDeferred(Func<System.Threading.Tasks.Task> action)
        {
            _ = _package.JoinableTaskFactory.RunAsync(async () =>
            {
                await System.Threading.Tasks.Task.Delay(300);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                try { await action(); }
                catch (Exception ex) { ShowError("Export failed: " + ex.Message); }
            });
        }

        private void ExecuteCopyExcel(object sender, EventArgs e) => RunDeferred(ExecuteCopyExcelCoreAsync);
        private void ExecuteOpenExcel(object sender, EventArgs e) => RunDeferred(ExecuteOpenExcelCoreAsync);
        private void ExecuteAddSheet(object sender, EventArgs e) => RunDeferred(ExecuteAddSheetCoreAsync);

        /// <summary>
        /// Queues the current result set as a worksheet. Copy each result set in
        /// turn and run this for each; then Copy Results as Excel Table opens one
        /// workbook containing every queued set on its own sheet.
        /// </summary>
        private async System.Threading.Tasks.Task ExecuteAddSheetCoreAsync()
        {
            try
            {
                var general = _package.GetGeneralOptions();
                string text = await AcquireResultsAsync(general);
                if (text == null) return;

                if (PendingSheets.Count > 0 && PendingSheets[PendingSheets.Count - 1] == text)
                {
                    ShowInfo("This result set is already queued as sheet " + PendingSheets.Count +
                             ". Copy the next result set, then run Add Results as Sheet again.");
                    return;
                }

                PendingSheets.Add(text);
                var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
                SetStatus(dte, $"Queued sheet {PendingSheets.Count}. Copy the next result set and press " +
                               "Ctrl+Shift+Alt+A again, or Ctrl+Shift+Alt+X to open the workbook.");
            }
            catch (Exception ex)
            {
                ShowError("Add Results as Sheet failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Exports the results grid to a styled .xlsx workbook and opens it.
        /// Includes any sheets queued via Add Results as Sheet. The clipboard is
        /// used only internally to capture the grid; nothing is left for pasting.
        /// </summary>
        private async System.Threading.Tasks.Task ExecuteCopyExcelCoreAsync()
        {
            try
            {
                var general = _package.GetGeneralOptions();
                string text = await AcquireResultsAsync(general);
                if (text == null) return;

                var sheets = new System.Collections.Generic.List<string>(PendingSheets);
                if (sheets.Count == 0 || sheets[sheets.Count - 1] != text) sheets.Add(text);
                PendingSheets.Clear();

                ExportResults(sheets, general);
                var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
                SetStatus(dte, $"Workbook opened with {sheets.Count} sheet(s).");
            }
            catch (Exception ex)
            {
                ShowError("Export to Excel failed: " + ex.Message);
            }
        }



        /// <summary>Reads clipboard text, retrying briefly - the clipboard is often locked momentarily by other apps.</summary>
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const uint KEYEVENTF_KEYUP = 0x0002;

        /// <summary>
        /// Releases Ctrl, Shift and Alt at the OS level. When our command is
        /// invoked by its keyboard shortcut, the user is still physically holding
        /// those modifiers - a synthesised Ctrl+Shift+C then combines with them
        /// into Ctrl+Shift+Alt+C, which the grid ignores. Releasing first makes
        /// the synthesised copy arrive clean.
        /// </summary>
        private const byte VK_SHIFT_KEY = 0x10;
        private const byte VK_CONTROL_KEY = 0x11;
        private const byte VK_C_KEY = 0x43;

        /// <summary>
        /// Synthesises Ctrl+C / Ctrl+Shift+C with direct Win32 calls. Used instead
        /// of SendKeys, whose journal-hook implementation behaves unreliably inside
        /// the Visual Studio shell.
        /// </summary>
        private static void SendCopyKeystroke(bool withShift)
        {
            keybd_event(VK_CONTROL_KEY, 0, 0, UIntPtr.Zero);
            if (withShift) keybd_event(VK_SHIFT_KEY, 0, 0, UIntPtr.Zero);
            keybd_event(VK_C_KEY, 0, 0, UIntPtr.Zero);
            keybd_event(VK_C_KEY, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            if (withShift) keybd_event(VK_SHIFT_KEY, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL_KEY, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private static void ReleaseModifierKeys()
        {
            byte[] keys = { 0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 }; // Shift, Ctrl, Alt + L/R variants
            foreach (var k in keys)
                keybd_event(k, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private static string ReadClipboardText()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (System.Windows.Forms.Clipboard.ContainsText())
                        return System.Windows.Forms.Clipboard.GetText();
                    return null;
                }
                catch
                {
                    System.Threading.Thread.Sleep(60);
                }
            }
            return null;
        }


        /// <summary>
        /// Writes the copied results straight to a workbook and opens it. Includes
        /// any sheets queued via Add Results as Sheet.
        /// </summary>
        private async System.Threading.Tasks.Task ExecuteOpenExcelCoreAsync()
        {
            try
            {
                var general = _package.GetGeneralOptions();
                string text = await AcquireResultsAsync(general);
                if (text == null) return;

                var sheets = new System.Collections.Generic.List<string>(PendingSheets);
                if (sheets.Count == 0 || sheets[sheets.Count - 1] != text) sheets.Add(text);
                PendingSheets.Clear();

                ExportResults(sheets, general);
                var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
                SetStatus(dte, $"Workbook opened with {sheets.Count} sheet(s).");
            }
            catch (Exception ex)
            {
                ShowError("Could not open the results in Excel: " + ex.Message);
            }
        }



        private void ExecuteExportSettings(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var general = _package.GetGeneralOptions();
                using (var dialog = new System.Windows.Forms.SaveFileDialog())
                {
                    dialog.Title = "Export formatter settings";
                    dialog.FileName = "SsmsSqlFormatter.settings.json";
                    dialog.DefaultExt = "json";
                    dialog.Filter = "JSON file (*.json)|*.json|All files (*.*)|*.*";
                    dialog.OverwritePrompt = true;
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

                    System.IO.File.WriteAllText(dialog.FileName, Options.FormatterSettingsSerializer.ToJson(general));
                    ShowInfo("Settings exported to:\r\n" + dialog.FileName);
                }
            }
            catch (Exception ex)
            {
                ShowError("Could not export settings: " + ex.Message);
            }
        }

        /// <summary>Applies settings previously exported to a JSON file.</summary>
        private void ExecuteImportSettings(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var general = _package.GetGeneralOptions();
                using (var dialog = new System.Windows.Forms.OpenFileDialog())
                {
                    dialog.Title = "Import formatter settings";
                    dialog.Filter = "JSON file (*.json)|*.json|All files (*.*)|*.*";
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

                    var (applied, skipped) = Options.FormatterSettingsSerializer.ApplyFromJson(
                        general, System.IO.File.ReadAllText(dialog.FileName));

                    general.SaveSettingsToStorage();
                    ShowInfo($"Imported {applied} setting(s)." +
                             (skipped > 0 ? $" {skipped} value(s) were not recognised and were left unchanged." : ""));
                }
            }
            catch (Exception ex)
            {
                ShowError("Could not import settings: " + ex.Message);
            }
        }


        internal static string Hex(System.Drawing.Color c) =>
            "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");

        /// <summary>
        /// True when the clipboard text is actually the query editor's content -
        /// which happens when the editor (not the results grid) had focus during
        /// the automatic copy. Compares against both the current selection and the
        /// whole document, ignoring line-ending and edge-whitespace differences.
        /// </summary>
        private static bool LooksLikeActiveQueryText(string clipText)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
                var textDoc = dte?.ActiveDocument?.Object("TextDocument") as TextDocument;
                if (textDoc == null) return false;

                string Norm(string s) =>
                    (s ?? "").Replace("\r\n", "\n").Trim();

                string clip = Norm(clipText);
                if (clip.Length == 0) return false;

                var selection = textDoc.Selection;
                if (selection != null && !selection.IsEmpty &&
                    Norm(selection.Text) == clip)
                    return true;

                string full = textDoc.StartPoint.CreateEditPoint().GetText(textDoc.EndPoint);
                return Norm(full) == clip;
            }
            catch
            {
                return false;
            }
        }

        private static Formatting.ExcelStyle BuildStyle(Options.GeneralOptions general)
        {
            return new Formatting.ExcelStyle
            {
                FirstRowIsHeader = general.ExcelFirstRowIsHeader,
                ForceTextCells = general.ExcelForceTextCells,
                NullsAsEmpty = general.ExcelNullsAsEmpty,
                FontName = general.ExcelFontName,
                FontSize = general.ExcelFontSize,
                HeaderBold = general.ExcelHeaderBold,
                HeaderBackColor = Hex(general.ExcelHeaderBackColor),
                HeaderTextColor = Hex(general.ExcelHeaderTextColor),
                ShowBorders = general.ExcelShowBorders,
                BorderColor = Hex(general.ExcelBorderColor),
                BandedRows = general.ExcelBandedRows,
                BandColor = Hex(general.ExcelBandColor)
            };
        }
        /// <summary>
        /// Writes the captured result sets to a workbook (or CSV) and opens it.
        /// Honours the output folder, format, save-prompt and include-query options.
        /// </summary>
        private void ExportResults(System.Collections.Generic.IList<string> tsvs,
                                   Options.GeneralOptions general)
        {
            try
            {
                bool csv = general.ExportAs == Options.ExportFormat.Csv;
                string extension = csv ? ".csv" : ".xlsx";
                string fileName = "SsmsResults_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + extension;

                string path = BuildOutputPath(general, fileName, extension, csv);
                if (path == null) return;   // user cancelled the save dialog

                if (csv)
                {
                    Formatting.CsvWriter.Write(path, tsvs, ',', general.ExcelNullsAsEmpty);
                }
                else
                {
                    var sheets = new System.Collections.Generic.List<Formatting.XlsxSheet>();
                    for (int i = 0; i < tsvs.Count; i++)
                        sheets.Add(new Formatting.XlsxSheet
                        {
                            Name = tsvs.Count == 1 ? "Results" : "Results " + (i + 1),
                            Tsv = tsvs[i]
                        });

                    if (general.ExportIncludeQuery)
                    {
                        string query = GetActiveQueryText();
                        if (!string.IsNullOrWhiteSpace(query))
                            sheets.Add(new Formatting.XlsxSheet { Name = "Query", Tsv = query, Plain = true });
                    }

                    Formatting.XlsxWriter.WriteSheets(path, sheets, BuildStyle(general));
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowError("Could not export the results: " + ex.Message);
            }
        }

        /// <summary>
        /// Decides where the export is written: a Save As dialog when requested,
        /// otherwise the configured output folder, falling back to the temp folder
        /// when that folder is missing or unusable.
        /// </summary>
        private static string BuildOutputPath(Options.GeneralOptions general, string fileName,
                                              string extension, bool csv)
        {
            string folder = System.IO.Path.GetTempPath();

            if (!string.IsNullOrWhiteSpace(general.ExportFolder))
            {
                try
                {
                    if (!System.IO.Directory.Exists(general.ExportFolder))
                        System.IO.Directory.CreateDirectory(general.ExportFolder);
                    folder = general.ExportFolder;
                }
                catch
                {
                    // Unusable folder - fall back to temp rather than failing the export.
                }
            }

            if (!general.ExportPrompt)
                return System.IO.Path.Combine(folder, fileName);

            using (var dialog = new System.Windows.Forms.SaveFileDialog())
            {
                dialog.Title = "Export results";
                dialog.FileName = fileName;
                dialog.InitialDirectory = folder;
                dialog.DefaultExt = extension.TrimStart('.');
                dialog.Filter = csv
                    ? "CSV file (*.csv)|*.csv|All files (*.*)|*.*"
                    : "Excel workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*";
                dialog.OverwritePrompt = true;
                return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
                    ? dialog.FileName : null;
            }
        }

        /// <summary>Text of the active query window, used for the optional Query sheet.</summary>
        private static string GetActiveQueryText()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
                var textDoc = dte?.ActiveDocument?.Object("TextDocument") as TextDocument;
                if (textDoc == null) return null;
                return textDoc.StartPoint.CreateEditPoint().GetText(textDoc.EndPoint);
            }
            catch
            {
                return null;
            }
        }


        private void ExecuteBatchFormat(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                string[] paths;
                using (var dialog = new System.Windows.Forms.OpenFileDialog())
                {
                    dialog.Title = "Select .sql files to format";
                    dialog.Filter = "SQL scripts (*.sql)|*.sql|All files (*.*)|*.*";
                    dialog.Multiselect = true;
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    paths = dialog.FileNames;
                }
                if (paths == null || paths.Length == 0) return;

                var confirm = MessageBox.Show(
                    $"This will format {paths.Length} file(s) directly on disk, overwriting each in place.\r\n\r\n" +
                    "Files that fail to parse are left untouched. Close any of these files first if they're " +
                    "open with unsaved changes, so the editor doesn't end up out of sync with the file on disk.\r\n\r\n" +
                    "Continue?",
                    "Format Files", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes) return;

                var general = _package.GetGeneralOptions();
                var summary = Formatting.BatchFormatter.FormatFiles(paths, general, dryRun: false, useFolderConfig: general.UseFolderConfig);

                var message = $"Formatted {summary.FormattedCount} of {paths.Length} file(s). " +
                              $"{summary.UnchangedCount} already matched the current style.";
                if (summary.Failures.Count > 0)
                    message += $"\r\n\r\n{summary.Failures.Count} file(s) were skipped:\r\n" + string.Join("\r\n", summary.Failures);
                ShowInfo(message);
            }
            catch (Exception ex)
            {
                ShowError("Batch format failed: " + ex.Message);
            }
        }

        private void ExecuteFormatAllOpen(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _ = _package.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ExecuteFormatAllOpenCoreAsync();
                }
                catch (Exception ex)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    ShowError("Unexpected error: " + ex.Message);
                }
            });
        }

        /// <summary>
        /// Formats every currently-open .sql document in place - a normal editor edit
        /// per window (Ctrl+Z in that window undoes it; nothing is written to disk
        /// unless the user saves afterward), unlike "Format Files..." which writes
        /// straight to disk. Always uses the rule-based engine, for the same reasons as
        /// batch/save/paste formatting. A document that fails to parse is left untouched.
        /// </summary>
        private async Task ExecuteFormatAllOpenCoreAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dte = (DTE2)await _package.GetServiceAsync(typeof(DTE));
            if (dte == null) return;

            var candidates = new System.Collections.Generic.List<Document>();
            foreach (Document doc in dte.Documents)
            {
                if (FormatOnSaveDocTableEvents.IsSqlFile(doc.FullName) && doc.Object("TextDocument") is TextDocument)
                    candidates.Add(doc);
            }

            if (candidates.Count == 0)
            {
                ShowInfo("No open .sql documents found.");
                return;
            }

            var confirm = MessageBox.Show(
                $"This will format {candidates.Count} open .sql document(s) in place.\r\n\r\n" +
                "Each is a normal editor edit - Ctrl+Z in that window undoes it, and nothing " +
                "is written to disk unless you save afterward. Documents that fail to parse are left untouched.\r\n\r\n" +
                "Continue?",
                "Format All Open Files", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            var general = _package.GetGeneralOptions();
            int formatted = 0, unchanged = 0;
            var failures = new System.Collections.Generic.List<string>();

            foreach (var doc in candidates)
            {
                try
                {
                    var textDoc = doc.Object("TextDocument") as TextDocument;
                    if (textDoc == null) continue;

                    string original = textDoc.StartPoint.CreateEditPoint().GetText(textDoc.EndPoint);
                    if (string.IsNullOrWhiteSpace(original)) { unchanged++; continue; }

                    var effective = ResolveEffectiveGeneralOptions(general, doc.FullName);
                    var result = ScriptDomFormatter.Format(original, effective);
                    if (!result.Success)
                    {
                        failures.Add(doc.Name + ": " + result.ErrorMessage);
                        continue;
                    }
                    if (result.FormattedSql == original) { unchanged++; continue; }

                    dte.UndoContext.Open("Format T-SQL Script (All Open Files)");
                    try
                    {
                        var start = textDoc.StartPoint.CreateEditPoint();
                        start.ReplaceText(textDoc.EndPoint, result.FormattedSql,
                            (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
                    }
                    finally
                    {
                        dte.UndoContext.Close();
                    }
                    formatted++;
                }
                catch (Exception ex)
                {
                    failures.Add(doc.Name + ": " + ex.Message);
                }
            }

            var message = $"Formatted {formatted} of {candidates.Count} open document(s). " +
                          $"{unchanged} already matched the current style.";
            if (failures.Count > 0)
                message += $"\r\n\r\n{failures.Count} document(s) were skipped:\r\n" + string.Join("\r\n", failures);
            ShowInfo(message);
        }

        private void ExecuteHelp(object sender, EventArgs e)
        {
            var answer = MessageBox.Show(
                "FORMAT T-SQL SCRIPT — quick help\r\n" +
                "\r\n" +
                "Format:  Ctrl+Shift+Alt+F, right-click > Format T-SQL Script, or the Tools menu.\r\n" +
                "Formats the selection if there is one, otherwise the whole document.\r\n" +
                "Ctrl+Z undoes the entire format in one step.\r\n" +
                "\r\n" +
                "Format Files...:  Tools menu. Formats one or more .sql files on disk in place\r\n" +
                "using the rule-based engine. A file that fails to parse is left untouched.\r\n" +
                "\r\n" +
                "Format All Open Files:  Tools menu. Formats every open .sql document in place -\r\n" +
                "a normal editor edit per window (Ctrl+Z undoes it), nothing written to disk\r\n" +
                "unless you save afterward.\r\n" +
                "\r\n" +
                "Settings:  Tools > Options > Format T-SQL Script\r\n" +
                "  • General — engine, Classic/Modern/Custom preset, casing, indentation,\r\n" +
                "    comma placement, subquery re-indent, blank lines around GO,\r\n" +
                "    comment preservation, format on save, format on paste, shared\r\n" +
                "    .sqlformatter.json config.\r\n" +
                "  • AI Engine — Anthropic or Copilot provider, API key/token, and custom\r\n" +
                "    style instructions.\r\n" +
                "  • Help — this information inside the options dialog.\r\n" +
                "\r\n" +
                "Scripts with syntax errors are never modified; comments are never\r\n" +
                "silently deleted.\r\n" +
                "\r\n" +
                "Open the project page (documentation, updates, issue tracker) in your browser?",
                "Format T-SQL Script — Help",
                MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (answer == MessageBoxResult.Yes)
            {
                try
                {
                    System.Diagnostics.Process.Start("https://github.com/ClaneSharke/SSMSSQLFormatter");
                }
                catch
                {
                    MessageBox.Show("Could not open a browser. The project page is:\r\nhttps://github.com/ClaneSharke/SSMSSQLFormatter",
                        "Format T-SQL Script", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        public static async Task InitializeAsync(SsmsSqlFormatterPackage package)
        {
            var commandService = (OleMenuCommandService)await package.GetServiceAsync(typeof(IMenuCommandService));
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            _ = new FormatSqlCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _ = _package.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ExecuteCoreAsync();
                }
                catch (Exception ex)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    ShowError("Unexpected error: " + ex.Message);
                }
            });
        }

        private void ExecutePreviewFormat(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _ = _package.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ExecutePreviewCoreAsync();
                }
                catch (Exception ex)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    ShowError("Unexpected error: " + ex.Message);
                }
            });
        }

        private async Task ExecutePreviewCoreAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dte = (DTE2)await _package.GetServiceAsync(typeof(DTE));
            var doc = dte?.ActiveDocument;
            var textDoc = doc?.Object("TextDocument") as TextDocument;
            if (textDoc == null)
            {
                ShowInfo("Open a query window first.");
                return;
            }

            var selection = textDoc.Selection;
            bool useSelection = selection != null && !selection.IsEmpty;
            string original = useSelection
                ? selection.Text
                : textDoc.StartPoint.CreateEditPoint().GetText(textDoc.EndPoint);

            if (string.IsNullOrWhiteSpace(original))
            {
                ShowInfo("Nothing to format.");
                return;
            }

            var general = ResolveEffectiveGeneralOptions(_package.GetGeneralOptions(), doc?.FullName);
            var ai = _package.GetAiOptions();

            FormatResult result;

            if (general.Engine == FormatterEngine.Ai)
            {
                if (ai.ConfirmBeforeSending)
                {
                    var providerName = ai.Provider == AiProvider.Copilot ? "Copilot" : "Anthropic";
                    var confirm = MessageBox.Show(
                        $"Send this script to the {providerName} API for formatting?\r\n\r\n" +
                        "The script text (including any literals it contains) will leave this machine.",
                        "SQL Formatter — AI engine",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (confirm != MessageBoxResult.Yes) return;
                }

                SetStatus(dte, "Formatting with AI (preview)…");
                result = await AiFormatter.FormatAsync(original, general, ai).ConfigureAwait(true);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (!result.Success && ai.FallbackToRuleBased)
                {
                    SetStatus(dte, "AI failed, falling back to rule-based formatter…");
                    var fallback = ScriptDomFormatter.Format(original, general);
                    if (fallback.Success)
                    {
                        fallback.ErrorMessage = result.ErrorMessage; // remember why AI failed
                        result = fallback;
                    }
                }
            }
            else
            {
                string toFormat = original;
                if (general.ExpandSelectStar)
                {
                    var columnLookup = BuildSelectStarColumnLookup();
                    if (columnLookup != null)
                    {
                        var expand = await SelectStarExpander.ExpandAsync(toFormat, columnLookup, CancellationToken.None).ConfigureAwait(true);
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        toFormat = expand.ExpandedSql;
                    }
                }

                result = ScriptDomFormatter.Format(toFormat, general);

                if (result.Success && result.CommentCount > 0 && general.WarnOnComments && general.CommentHandling == CommentHandling.Discard)
                {
                    var proceed = MessageBox.Show(
                        $"This script contains {result.CommentCount} comment(s). Comment handling is set to " +
                        "Discard, so they will be dropped when reformatting.\r\n\r\n" +
                        "Continue anyway? (Tip: set 'Comment handling' to Inline or MoveToEnd under " +
                        "Tools > Options > Format T-SQL Script to keep them.)",
                        "SQL Formatter",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (proceed != MessageBoxResult.Yes) return;
                }
            }

            if (!result.Success)
            {
                ShowError(result.ErrorMessage ?? "Formatting failed.");
                SetStatus(dte, "SQL formatting failed.");
                return;
            }

            // Show preview window
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var preview = new PreviewWindow(original, result.FormattedSql);
                var applied = preview.ShowDialog() == true;

            if (applied)
            {
                    // Apply the formatted SQL (use edited preview text if changed)
                    var toApply = preview.FormattedText ?? result.FormattedSql;
                dte.UndoContext.Open("Format T-SQL Script (Preview)");
                try
                {
                    if (useSelection)
                    {
                            selection.Insert(toApply,
                            (int)vsInsertFlags.vsInsertFlagsContainNewText);
                    }
                    else
                    {
                        var start = textDoc.StartPoint.CreateEditPoint();
                            start.ReplaceText(textDoc.EndPoint, toApply,
                            (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
                    }
                }
                finally
                {
                    dte.UndoContext.Close();
                }

                SetStatus(dte, "Formatted (applied from preview).");
            }
            else
            {
                SetStatus(dte, "Preview closed without applying.");
            }
        }

        private void ExecuteExpandSelectStar(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _ = _package.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ExecuteExpandSelectStarCoreAsync();
                }
                catch (Exception ex)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    ShowError("Unexpected error: " + ex.Message);
                }
            });
        }

        /// <summary>
        /// Standalone "Expand SELECT *" command - unlike the main Format command's
        /// ExpandSelectStar option (which only folds expansion into a normal format pass),
        /// this always attempts expansion regardless of that option or the selected
        /// formatting engine, since it's an explicit, direct request. Always resolves via
        /// the rule-based engine (expansion has nothing to do with AI vs rule-based
        /// formatting), then runs the result through the normal rule-based styling pass and
        /// shows it in the same preview/apply/undo-unit flow as ExecutePreviewCoreAsync.
        /// </summary>
        private async Task ExecuteExpandSelectStarCoreAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dte = (DTE2)await _package.GetServiceAsync(typeof(DTE));
            var doc = dte?.ActiveDocument;
            var textDoc = doc?.Object("TextDocument") as TextDocument;
            if (textDoc == null)
            {
                ShowInfo("Open a query window first.");
                return;
            }

            var selection = textDoc.Selection;
            bool useSelection = selection != null && !selection.IsEmpty;
            string original = useSelection
                ? selection.Text
                : textDoc.StartPoint.CreateEditPoint().GetText(textDoc.EndPoint);

            if (string.IsNullOrWhiteSpace(original))
            {
                ShowInfo("Nothing to format.");
                return;
            }

            var general = ResolveEffectiveGeneralOptions(_package.GetGeneralOptions(), doc?.FullName);

            var columnLookup = BuildSelectStarColumnLookup();
            if (columnLookup == null)
            {
                ShowInfo("Could not determine the active query window's connection, so SELECT * " +
                         "could not be expanded. Make sure the query window is connected to a " +
                         "database and try again.");
                return;
            }

            SetStatus(dte, "Resolving table structure…");
            var expand = await SelectStarExpander.ExpandAsync(original, columnLookup, CancellationToken.None).ConfigureAwait(true);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (expand.ExpandedCount == 0)
            {
                if (expand.UnresolvedCount > 0)
                    ShowInfo("Found SELECT * but couldn't resolve the referenced table/view structure " +
                             "(not found, not accessible, or not a plain table/view) - left unchanged.");
                else
                    ShowInfo("No SELECT * found to expand.");
                SetStatus(dte, "Expand SELECT * made no changes.");
                return;
            }

            var expandResult = ScriptDomFormatter.Format(expand.ExpandedSql, general);
            if (!expandResult.Success)
            {
                ShowError(expandResult.ErrorMessage ?? "Formatting failed.");
                SetStatus(dte, "SQL formatting failed.");
                return;
            }

            var expandPreview = new PreviewWindow(original, expandResult.FormattedSql);
            var expandApplied = expandPreview.ShowDialog() == true;

            if (expandApplied)
            {
                var toApply = expandPreview.FormattedText ?? expandResult.FormattedSql;
                dte.UndoContext.Open("Expand SELECT *");
                try
                {
                    if (useSelection)
                    {
                        selection.Insert(toApply, (int)vsInsertFlags.vsInsertFlagsContainNewText);
                    }
                    else
                    {
                        var start = textDoc.StartPoint.CreateEditPoint();
                        start.ReplaceText(textDoc.EndPoint, toApply, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
                    }
                }
                finally
                {
                    dte.UndoContext.Close();
                }

                SetStatus(dte, $"Expanded {expand.ExpandedCount} SELECT * (applied from preview).");
            }
            else
            {
                SetStatus(dte, "Preview closed without applying.");
            }
        }

        private async Task ExecuteCoreAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dte = (DTE2)await _package.GetServiceAsync(typeof(DTE));
            var doc = dte?.ActiveDocument;
            var textDoc = doc?.Object("TextDocument") as TextDocument;
            if (textDoc == null)
            {
                ShowInfo("Open a query window first.");
                return;
            }

            var selection = textDoc.Selection;
            bool useSelection = selection != null && !selection.IsEmpty;
            string original = useSelection
                ? selection.Text
                : textDoc.StartPoint.CreateEditPoint().GetText(textDoc.EndPoint);

            if (string.IsNullOrWhiteSpace(original))
            {
                ShowInfo("Nothing to format.");
                return;
            }

            var general = ResolveEffectiveGeneralOptions(_package.GetGeneralOptions(), doc?.FullName);
            var ai = _package.GetAiOptions();

            FormatResult result;

            if (general.Engine == FormatterEngine.Ai)
            {
                if (ai.ConfirmBeforeSending)
                {
                    var providerName = ai.Provider == AiProvider.Copilot ? "Copilot" : "Anthropic";
                    var confirm = MessageBox.Show(
                        $"Send this script to the {providerName} API for formatting?\r\n\r\n" +
                        "The script text (including any literals it contains) will leave this machine.",
                        "SQL Formatter — AI engine",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (confirm != MessageBoxResult.Yes) return;
                }

                SetStatus(dte, "Formatting with AI…");
                result = await AiFormatter.FormatAsync(original, general, ai).ConfigureAwait(true);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (!result.Success && ai.FallbackToRuleBased)
                {
                    SetStatus(dte, "AI failed, falling back to rule-based formatter…");
                    var fallback = ScriptDomFormatter.Format(original, general);
                    if (fallback.Success)
                    {
                        fallback.ErrorMessage = result.ErrorMessage; // remember why AI failed
                        result = fallback;
                    }
                }
            }
            else
            {
                string toFormat = original;
                if (general.ExpandSelectStar)
                {
                    var columnLookup = BuildSelectStarColumnLookup();
                    if (columnLookup != null)
                    {
                        var expand = await SelectStarExpander.ExpandAsync(toFormat, columnLookup, CancellationToken.None).ConfigureAwait(true);
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        toFormat = expand.ExpandedSql;
                    }
                }

                result = ScriptDomFormatter.Format(toFormat, general);

                if (result.Success && result.CommentCount > 0 && general.WarnOnComments && general.CommentHandling == CommentHandling.Discard)
                {
                    var proceed = MessageBox.Show(
                        $"This script contains {result.CommentCount} comment(s). Comment handling is set to " +
                        "Discard, so they will be dropped when reformatting.\r\n\r\n" +
                        "Continue anyway? (Tip: set 'Comment handling' to Inline or MoveToEnd under " +
                        "Tools > Options > Format T-SQL Script to keep them.)",
                        "SQL Formatter",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (proceed != MessageBoxResult.Yes) return;
                }
            }

            if (!result.Success)
            {
                ShowError(result.ErrorMessage ?? "Formatting failed.");
                SetStatus(dte, "SQL formatting failed.");
                return;
            }

            // Replace text in a single undo unit so Ctrl+Z reverts the whole format.
            dte.UndoContext.Open("Format T-SQL Script");
            try
            {
                if (useSelection)
                {
                    selection.Insert(result.FormattedSql,
                        (int)vsInsertFlags.vsInsertFlagsContainNewText);
                }
                else
                {
                    var start = textDoc.StartPoint.CreateEditPoint();
                    start.ReplaceText(textDoc.EndPoint, result.FormattedSql,
                        (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
                }
            }
            finally
            {
                dte.UndoContext.Close();
            }

            SetStatus(dte, "SQL formatted" +
                (general.Engine == FormatterEngine.Ai && result.ErrorMessage == null ? " (AI)." :
                 general.Engine == FormatterEngine.Ai ? " (rule-based fallback — AI failed: " + result.ErrorMessage + ")" :
                 " (rule-based)."));
        }

        /// <summary>
        /// Overlays a folder-level .sqlformatter.json (if the user has that on and the
        /// document has a real path) onto the user's own settings, for this operation
        /// only. Returns <paramref name="general"/> unchanged when there's nothing to
        /// apply - a bad/missing repo config, or an unsaved document, never blocks formatting.
        /// </summary>
        private static Options.GeneralOptions ResolveEffectiveGeneralOptions(Options.GeneralOptions general, string filePath)
        {
            if (!general.UseFolderConfig || string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                return general;
            return (Options.GeneralOptions)Options.FormatterConfigDiscovery.ResolveEffectiveSettings(filePath, general);
        }

        private static void SetStatus(DTE2 dte, string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try { dte.StatusBar.Text = message; } catch { /* status bar is best-effort */ }
        }

        private static void ShowError(string message) =>
            MessageBox.Show(message, "SQL Formatter", MessageBoxButton.OK, MessageBoxImage.Error);

        private static void ShowInfo(string message) =>
            MessageBox.Show(message, "SQL Formatter", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
