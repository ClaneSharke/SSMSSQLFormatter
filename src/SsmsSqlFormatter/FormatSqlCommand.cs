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

        private readonly SsmsSqlFormatterPackage _package;

        private FormatSqlCommand(SsmsSqlFormatterPackage package, OleMenuCommandService commandService)
        {
            _package = package;
            commandService.AddCommand(new MenuCommand(Execute, new CommandID(CommandSet, CommandId)));
            commandService.AddCommand(new MenuCommand(Execute, new CommandID(CommandSet, ContextCommandId)));
            commandService.AddCommand(new MenuCommand(ExecuteHelp, new CommandID(CommandSet, HelpCommandId)));
            commandService.AddCommand(new MenuCommand(ExecuteCopyExcel, new CommandID(CommandSet, CopyExcelCommandId)));
            commandService.AddCommand(new MenuCommand(ExecuteCopyExcel, new CommandID(CommandSet, CopyExcelContextCommandId)));
        }

        /// <summary>
        /// Copies the SSMS results grid to the clipboard in Excel table format
        /// (CF_HTML), with headers, so a plain Ctrl+V in Excel pastes a real table.
        /// Works by transforming the grid's own Copy-with-Headers output rather
        /// than reaching into SSMS internals.
        /// </summary>
        private void ExecuteCopyExcel(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var general = _package.GetGeneralOptions();

                string before = ReadClipboardText();

                if (general.ExcelSimulateCopyFirst)
                {
                    // Ask the focused control (the results grid) to Copy with Headers.
                    // This only reaches the grid when the grid still has focus - i.e.
                    // when invoked by keyboard shortcut. Toolbar and menu clicks move
                    // focus away, so we fall back to whatever is already copied.
                    try
                    {
                        System.Windows.Forms.SendKeys.SendWait("^+c");
                        System.Threading.Thread.Sleep(350);
                    }
                    catch { /* SendKeys can fail in some hosts; fall through */ }
                }

                string text = ReadClipboardText();

                // If the simulated copy did nothing, use whatever the user copied
                // themselves - that is the normal case for toolbar/menu invocation.
                if (string.IsNullOrEmpty(text)) text = before;

                if (string.IsNullOrWhiteSpace(text))
                {
                    ShowInfo(
                        "Nothing to convert - the clipboard is empty.\r\n\r\n" +
                        "Copy your results first:\r\n" +
                        "  1. Click in the results grid (Ctrl+A selects everything).\r\n" +
                        "  2. Right-click > Copy with Headers, or press Ctrl+Shift+C.\r\n" +
                        "  3. Run this command again.\r\n\r\n" +
                        "Tip: pressing Ctrl+Shift+Alt+X while the grid has focus does " +
                        "the copy for you. A toolbar or menu click cannot, because " +
                        "clicking moves focus out of the grid.");
                    return;
                }

                string cfHtml = Formatting.ExcelClipboard.BuildCfHtml(
                    text, general.ExcelForceTextCells, general.ExcelNullsAsEmpty,
                    general.ExcelFirstRowIsHeader,
                    out int rows, out int cols);

                var data = new System.Windows.DataObject();
                data.SetData(System.Windows.DataFormats.Html, cfHtml);
                data.SetData(System.Windows.DataFormats.UnicodeText, text);

                if (!TrySetClipboard(data))
                {
                    ShowError(
                        "Could not confirm the clipboard was written - another application " +
                        "may be holding it (clipboard managers, remote desktop sessions and " +
                        "antivirus tools are common causes).\r\n\r\n" +
                        "Try pasting into Excel anyway; if nothing arrives, run the command again.");
                    return;
                }

                var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
                SetStatus(dte, $"Copied as Excel table: {rows} row(s) x {cols} column(s). Paste into Excel with Ctrl+V.");
            }
            catch (Exception ex)
            {
                ShowError("Copy as Excel table failed: " + ex.Message);
            }
        }

        /// <summary>Reads clipboard text, retrying briefly - the clipboard is often locked momentarily by other apps.</summary>
        private static string ReadClipboardText()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (System.Windows.Clipboard.ContainsText())
                        return System.Windows.Clipboard.GetText();
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
        /// Writes the data object to the clipboard. SetDataObject frequently throws
        /// even though the data has in fact landed - clipboard managers, remote
        /// desktop sessions and antivirus tools commonly make the flush step fail.
        /// So rather than trusting the exception, verify what's actually on the
        /// clipboard before reporting a failure.
        /// </summary>
        private static bool TrySetClipboard(System.Windows.DataObject data)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    // copy:true persists the data after SSMS exits; it is also the
                    // step most likely to throw spuriously.
                    System.Windows.Clipboard.SetDataObject(data, attempt == 0);
                    return true;
                }
                catch
                {
                    if (ClipboardHasHtml()) return true;   // it worked despite the throw
                    System.Threading.Thread.Sleep(60);
                }
            }
            return ClipboardHasHtml();
        }

        private static bool ClipboardHasHtml()
        {
            try
            {
                return System.Windows.Clipboard.ContainsData(System.Windows.DataFormats.Html);
            }
            catch
            {
                return false;
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
