using System;
using System.ComponentModel.Design;
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
        private bool TryAcquireResults(Options.GeneralOptions general, out string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            text = null;

            string before = ReadClipboardText();

            if (general.ExcelSimulateCopyFirst)
            {
                try { System.Windows.Forms.Clipboard.Clear(); } catch { /* best effort */ }

                // Send the grid's Copy-with-Headers keystroke to whatever control
                // actually has focus. (The automation command Edit.CopyWithHeaders
                // is deliberately NOT used: on some SSMS builds it routes to the
                // query editor regardless of focus and copies the query text.)
                try
                {
                    ReleaseModifierKeys();
                    System.Windows.Forms.SendKeys.SendWait("^+c");
                    System.Threading.Thread.Sleep(400);
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
                    System.Windows.Forms.SendKeys.SendWait("^c");
                    System.Threading.Thread.Sleep(400);
                    after = ReadClipboardText();
                }
                catch { /* keep what we have */ }
            }

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
                return false;
            }

            if (LooksLikeActiveQueryText(text))
            {
                text = null;
                ShowInfo(
                    "That looks like the query text, not the results - the query " +
                    "editor had focus when the copy ran.\r\n\r\n" +
                    "Click anywhere inside the results grid first (Ctrl+A selects all " +
                    "cells), then run this command again.");
                return false;
            }

            return true;
        }

        /// <summary>Tab-separated or multi-line content - the shape of a grid copy.</summary>
        private static bool LooksLikeGridData(string s)
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
        private void RunDeferred(Action action)
        {
            _package.JoinableTaskFactory.RunAsync(async () =>
            {
                await System.Threading.Tasks.Task.Delay(300);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                try { action(); }
                catch (Exception ex) { ShowError("Export failed: " + ex.Message); }
            });
        }

        private void ExecuteCopyExcel(object sender, EventArgs e) => RunDeferred(ExecuteCopyExcelCore);
        private void ExecuteOpenExcel(object sender, EventArgs e) => RunDeferred(ExecuteOpenExcelCore);
        private void ExecuteAddSheet(object sender, EventArgs e) => RunDeferred(ExecuteAddSheetCore);

        /// <summary>
        /// Queues the current result set as a worksheet. Copy each result set in
        /// turn and run this for each; then Copy Results as Excel Table opens one
        /// workbook containing every queued set on its own sheet.
        /// </summary>
        private void ExecuteAddSheetCore()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var general = _package.GetGeneralOptions();
                if (!TryAcquireResults(general, out string text)) return;

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
        private void ExecuteCopyExcelCore()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var general = _package.GetGeneralOptions();
                if (!TryAcquireResults(general, out string text)) return;

                var sheets = new System.Collections.Generic.List<string>(PendingSheets);
                if (sheets.Count == 0 || sheets[sheets.Count - 1] != text) sheets.Add(text);
                PendingSheets.Clear();

                OpenWorkbook(sheets, BuildStyle(general));
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
        private static void ReleaseModifierKeys()
        {
            byte[] keys = { 0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 }; // Shift, Ctrl, Alt + L/R variants
            foreach (var k in keys)
                keybd_event(k, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            System.Threading.Thread.Sleep(60);
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
        private void ExecuteOpenExcelCore()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var general = _package.GetGeneralOptions();
                if (!TryAcquireResults(general, out string text)) return;

                var sheets = new System.Collections.Generic.List<string>(PendingSheets);
                if (sheets.Count == 0 || sheets[sheets.Count - 1] != text) sheets.Add(text);
                PendingSheets.Clear();

                OpenWorkbook(sheets, BuildStyle(general));
                var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
                SetStatus(dte, $"Workbook opened with {sheets.Count} sheet(s).");
            }
            catch (Exception ex)
            {
                ShowError("Could not open the results in Excel: " + ex.Message);
            }
        }


        private static string Hex(System.Drawing.Color c) =>
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
        /// Writes the result sets to a temporary genuine .xlsx workbook (one sheet
        /// per set) and launches it. Dependable where clipboard access is
        /// restricted (elevated SSMS, remote desktop, locked-down policies).
        /// </summary>
        private static void OpenWorkbook(System.Collections.Generic.IList<string> tsvs,
                                         Formatting.ExcelStyle style)
        {
            try
            {
                string path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "SsmsResults_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx");

                Formatting.XlsxWriter.Write(path, tsvs, style);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowError("Could not open the results in Excel: " + ex.Message);
            }
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
                "Settings:  Tools > Options > Format T-SQL Script\r\n" +
                "  • General — engine, Classic/Modern/Custom preset, casing, indentation,\r\n" +
                "    comma placement, subquery re-indent, blank lines around GO,\r\n" +
                "    comment preservation.\r\n" +
                "  • AI Engine — optional Anthropic API key and custom style instructions.\r\n" +
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
            _package.JoinableTaskFactory.RunAsync(async () =>
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

            var general = _package.GetGeneralOptions();
            var ai = _package.GetAiOptions();

            FormatResult result;

            if (general.Engine == FormatterEngine.Ai)
            {
                if (ai.ConfirmBeforeSending)
                {
                    var confirm = MessageBox.Show(
                        "Send this script to the Anthropic API for formatting?\r\n\r\n" +
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
                result = ScriptDomFormatter.Format(original, general);

                if (result.Success && result.CommentCount > 0 && general.WarnOnComments && !general.PreserveComments)
                {
                    var proceed = MessageBox.Show(
                        $"This script contains {result.CommentCount} comment(s). The rule-based engine may drop " +
                        "or move comments when reformatting.\r\n\r\n" +
                        "Continue anyway? (Tip: the AI engine preserves comments — switch under " +
                        "Tools > Options > Format T-SQL Script.)",
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
