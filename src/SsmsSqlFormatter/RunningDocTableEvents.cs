using System;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SsmsSqlFormatter.Formatting;
using SsmsSqlFormatter.Options;

namespace SsmsSqlFormatter
{
    /// <summary>
    /// Formats .sql documents just before they are written to disk, when the
    /// "Format on save" option is enabled. Hooked into the Running Document
    /// Table so it fires for every save regardless of what triggered it
    /// (Ctrl+S, Save All, closing a dirty document, etc.).
    /// </summary>
    public sealed class FormatOnSaveDocTableEvents : IVsRunningDocTableEvents, IVsRunningDocTableEvents3
    {
        private readonly SsmsSqlFormatterPackage _package;
        private readonly IVsRunningDocumentTable _rdt;

        public FormatOnSaveDocTableEvents(SsmsSqlFormatterPackage package, IVsRunningDocumentTable rdt)
        {
            _package = package;
            _rdt = rdt;
        }

        /// <summary>True for file names the rule-based formatter should attempt on save.</summary>
        public static bool IsSqlFile(string moniker) =>
            !string.IsNullOrEmpty(moniker) && moniker.EndsWith(".sql", StringComparison.OrdinalIgnoreCase);

        public int OnBeforeSave(uint docCookie)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // A formatting failure must never block the save, so every path out
            // of here (including exceptions) falls through to returning S_OK.
            try
            {
                FormatBeforeSave(docCookie);
            }
            catch
            {
                // Best effort - leave the document exactly as the user had it.
            }
            return VSConstants.S_OK;
        }

        private void FormatBeforeSave(uint docCookie)
        {
            var general = _package.GetGeneralOptions();
            if (!general.FormatOnSave) return;

            _rdt.GetDocumentInfo(docCookie, out _, out _, out _, out string moniker, out _, out _, out _);
            if (!IsSqlFile(moniker)) return;

            if (general.UseFolderConfig)
                general = (GeneralOptions)FormatterConfigDiscovery.ResolveEffectiveSettings(moniker, general);

            var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
            if (dte == null) return;

            Document doc = null;
            foreach (Document candidate in dte.Documents)
            {
                if (string.Equals(candidate.FullName, moniker, StringComparison.OrdinalIgnoreCase))
                {
                    doc = candidate;
                    break;
                }
            }
            if (doc == null) return;

            var textDoc = doc.Object("TextDocument") as TextDocument;
            if (textDoc == null) return;

            string original = textDoc.StartPoint.CreateEditPoint().GetText(textDoc.EndPoint);
            if (string.IsNullOrWhiteSpace(original)) return;

            // Format-on-save always uses the rule-based engine: it must be
            // synchronous and never prompt or make a network call while a save
            // is in progress. A script that fails to parse is left untouched.
            var result = ScriptDomFormatter.Format(original, general);
            if (!result.Success || result.FormattedSql == original) return;

            var start = textDoc.StartPoint.CreateEditPoint();
            start.ReplaceText(textDoc.EndPoint, result.FormattedSql,
                (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
        }

        // IVsRunningDocTableEvents / 2 / 3 members this sink does not act on.
        public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining) => VSConstants.S_OK;
        public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining) => VSConstants.S_OK;
        public int OnAfterSave(uint docCookie) => VSConstants.S_OK;
        public int OnAfterAttributeChange(uint docCookie, uint grfAttribs) => VSConstants.S_OK;
        public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame) => VSConstants.S_OK;
        public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame) => VSConstants.S_OK;
        public int OnAfterAttributeChangeEx(uint docCookie, uint grfAttribs, IVsHierarchy pHierOld, uint itemidOld,
            string pszMkDocumentOld, IVsHierarchy pHierNew, uint itemidNew, string pszMkDocumentNew) => VSConstants.S_OK;
    }
}
