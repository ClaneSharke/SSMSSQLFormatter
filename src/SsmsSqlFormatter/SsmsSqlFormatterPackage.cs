using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SsmsSqlFormatter.Options;
using Task = System.Threading.Tasks.Task;

namespace SsmsSqlFormatter
{
    /// <summary>
    /// Root package for the SSMS SQL Formatter extension.
    /// Loads on demand (when a command is invoked) to keep SSMS startup fast.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideOptionPage(typeof(GeneralOptions), "Format T-SQL Script", "General", 0, 0, true)]
    [ProvideOptionPage(typeof(AiOptions), "Format T-SQL Script", "AI Engine", 0, 0, true)]
    [ProvideOptionPage(typeof(HelpOptions), "Format T-SQL Script", "Help", 0, 0, true)]
    public sealed class SsmsSqlFormatterPackage : AsyncPackage
    {
        public const string PackageGuidString = "a1e5c9f2-3b74-4d68-9e0a-6f2c8d5b1e47";

        private IVsRunningDocumentTable _rdt;
        private uint _rdtCookie;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await base.InitializeAsync(cancellationToken, progress);
            await FormatSqlCommand.InitializeAsync(this);

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            _rdt = await GetServiceAsync(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
            if (_rdt != null)
            {
                _rdt.AdviseRunningDocTableEvents(new FormatOnSaveDocTableEvents(this, _rdt), out _rdtCookie);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _rdt != null && _rdtCookie != 0)
            {
                try { _rdt.UnadviseRunningDocTableEvents(_rdtCookie); } catch { /* best effort */ }
                _rdt = null;
                _rdtCookie = 0;
            }
            base.Dispose(disposing);
        }

        public GeneralOptions GetGeneralOptions() => (GeneralOptions)GetDialogPage(typeof(GeneralOptions));
        public AiOptions GetAiOptions() => (AiOptions)GetDialogPage(typeof(AiOptions));
    }
}
