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
                    // Ask SSMS itself to copy - more reliable than synthesising
                    // keystrokes, and works regardless of which shortcut scheme
                    // the user has configured.
                    bool copied = false;
                    try
                    {
                        var dteCopy = (DTE2)Package.GetGlobalService(typeof(DTE));
                        dteCopy.ExecuteCommand("Edit.CopyWithHeaders");
                        System.Threading.Thread.Sleep(250);
                        copied = true;
                    }
                    catch { /* command not available in this SSMS build */ }

                    if (!copied)
                    {
                        try
                        {
                            System.Windows.Forms.SendKeys.SendWait("^+c");
                            System.Threading.Thread.Sleep(350);
                        }
                        catch { /* fall through to whatever is on the clipboard */ }
                    }
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

                var style = new Formatting.ExcelStyle
                {
                    FirstRowIsHeader = general.ExcelFirstRowIsHeader,
                    ForceTextCells = general.ExcelForceTextCells,
                    NullsAsEmpty = general.ExcelNullsAsEmpty,
                    FontName = general.ExcelFontName,
                    FontSize = general.ExcelFontSize,
                    HeaderBold = general.ExcelHeaderBold,
                    HeaderBackColor = general.ExcelHeaderBackColor,
                    HeaderTextColor = general.ExcelHeaderTextColor,
                    ShowBorders = general.ExcelShowBorders,
                    BorderColor = general.ExcelBorderColor,
                    BandedRows = general.ExcelBandedRows,
                    BandColor = general.ExcelBandColor
                };

                string cfHtml = Formatting.ExcelClipboard.BuildCfHtml(
                    text, style, out int rows, out int cols);

                if (!TrySetClipboard(cfHtml, text, out string clipError))
                {
                    ShowError(
                        "Could not write the Excel table to the clipboard.\r\n\r\n" +
                        (clipError ?? "Unknown clipboard error.") + "\r\n\r\n" +
                        "Try closing or pausing any clipboard manager, then run the " +
                        "command again. Your plain-text copy is unaffected.");
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
        /// Writes both the Excel (CF_HTML) and plain-text flavours to the clipboard.
        /// Uses the WinForms clipboard, whose SetDataObject overload retries
        /// internally - the WPF equivalent has no retry and fails whenever another
        /// application (clipboard manager, remote desktop, antivirus) momentarily
        /// holds the clipboard. Verifies the result rather than trusting exceptions.
        /// </summary>
        private static bool TrySetClipboard(string cfHtml, string plainText, out string error)
        {
            error = null;

            // Managed path first - fine on most machines.
            try
            {
                var data = new System.Windows.Forms.DataObject();
                data.SetData(System.Windows.Forms.DataFormats.Html, cfHtml);
                data.SetData(System.Windows.Forms.DataFormats.UnicodeText, plainText);
                data.SetData(System.Windows.Forms.DataFormats.Text, plainText);
                System.Windows.Forms.Clipboard.SetDataObject(data, true, 6, 120);
                if (ClipboardHasHtml()) return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            // Win32 fallback - bypasses OLE, which is what usually fails.
            if (Formatting.ClipboardHelper.SetHtmlAndText(cfHtml, plainText, out string win32Error))
                return true;

            error = win32Error ?? error;
            return false;
        }

        private static bool ClipboardHasHtml()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    return System.Windows.Forms.Clipboard.ContainsData(
                        System.Windows.Forms.DataFormats.Html);
                }
                catch
                {
                    System.Threading.Thread.Sleep(80);
                }
            }
            return false;
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
