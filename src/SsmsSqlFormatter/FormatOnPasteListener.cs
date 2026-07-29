using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SsmsSqlFormatter.Formatting;
using SsmsSqlFormatter.Options;

namespace SsmsSqlFormatter
{
    /// <summary>
    /// Attaches a <see cref="FormatOnPasteHandler"/> to every .sql text view. Filters on
    /// the file's own extension (via the view's <see cref="ITextDocument"/>) rather than
    /// a specific editor content type, since that's the same check format-on-save
    /// already uses and needs no assumption about SSMS/VS's internal content-type names.
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class FormatOnPasteTextViewCreationListener : IWpfTextViewCreationListener
    {
        public void TextViewCreated(IWpfTextView textView)
        {
            if (!textView.TextBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document))
                return;
            if (!FormatOnSaveDocTableEvents.IsSqlFile(document.FilePath))
                return;

            var handler = new FormatOnPasteHandler(textView, document);
            textView.Closed += (s, e) => handler.Detach();
        }
    }

    /// <summary>
    /// Reformats the whole document immediately after a detected paste (a single edit
    /// that inserts multi-line text - distinct from ordinary character-by-character
    /// typing). Opt-in via "Format on paste"; always uses the rule-based engine, for the
    /// same reasons as format-on-save: must be synchronous, with no confirmation prompt
    /// or network call. A script that fails to parse is left completely untouched.
    /// </summary>
    internal sealed class FormatOnPasteHandler
    {
        private readonly IWpfTextView _textView;
        private readonly ITextDocument _document;
        private bool _attached = true;
        private bool _isApplyingFormat;

        public FormatOnPasteHandler(IWpfTextView textView, ITextDocument document)
        {
            _textView = textView;
            _document = document;
            _textView.TextBuffer.Changed += OnTextBufferChanged;
        }

        public void Detach()
        {
            if (!_attached) return;
            _attached = false;
            _textView.TextBuffer.Changed -= OnTextBufferChanged;
        }

        private void OnTextBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            if (_isApplyingFormat) return; // don't react to our own reformatting edit
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (!LooksLikePaste(e)) return;

                var package = GetOrLoadPackage();
                var general = package?.GetGeneralOptions();
                if (general == null || !general.FormatOnPaste) return;

                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                string original = snapshot.GetText();
                if (string.IsNullOrWhiteSpace(original)) return;

                IFormatterOptions effective = general;
                if (general.UseFolderConfig && !string.IsNullOrEmpty(_document.FilePath))
                    effective = FormatterConfigDiscovery.ResolveEffectiveSettings(_document.FilePath, general);

                var result = ScriptDomFormatter.Format(original, effective);
                if (!result.Success || result.FormattedSql == original) return;

                _isApplyingFormat = true;
                try
                {
                    using (var edit = _textView.TextBuffer.CreateEdit())
                    {
                        edit.Replace(new Span(0, snapshot.Length), result.FormattedSql);
                        edit.Apply();
                    }
                }
                finally
                {
                    _isApplyingFormat = false;
                }
            }
            catch
            {
                // Best effort - never disrupt the paste itself.
            }
        }

        /// <summary>A paste looks like a single edit inserting multi-character, multi-line text - distinct from ordinary typing (one character per change).</summary>
        private static bool LooksLikePaste(TextContentChangedEventArgs e)
        {
            if (e.Changes.Count != 1) return false;
            return LooksLikePasteText(e.Changes[0].NewText);
        }

        /// <summary>The per-text half of the paste heuristic, factored out so it's testable without a live ITextView.</summary>
        internal static bool LooksLikePasteText(string newText) =>
            !string.IsNullOrEmpty(newText) && newText.Length > 1 && newText.IndexOf('\n') >= 0;

        /// <summary>
        /// Gets the already-loaded package, or forces it to load. The package normally
        /// loads on demand (first command use) to keep SSMS startup fast; a detected
        /// paste in a .sql file is exactly the kind of narrow, on-demand trigger that
        /// justifies loading it early, without changing that default for everyone else.
        /// </summary>
        private static SsmsSqlFormatterPackage GetOrLoadPackage()
        {
            if (SsmsSqlFormatterPackage.Instance != null) return SsmsSqlFormatterPackage.Instance;

            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (!(Package.GetGlobalService(typeof(SVsShell)) is IVsShell shell)) return null;
                var guid = new Guid(SsmsSqlFormatterPackage.PackageGuidString);
                shell.LoadPackage(ref guid, out IVsPackage _);
                return SsmsSqlFormatterPackage.Instance;
            }
            catch
            {
                return null;
            }
        }
    }
}
