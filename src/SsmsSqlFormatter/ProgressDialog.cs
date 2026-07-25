using System;
using System.Drawing;
using System.Windows.Forms;

namespace SsmsSqlFormatter
{
    /// <summary>
    /// A small always-on-top status window shown while results are captured and
    /// written. It is deliberately NOT modal: the export runs on the UI thread and
    /// awaits internally (the capture keystroke needs the message pump running), so
    /// a modal loop would interfere. Input to SSMS is instead blocked by the caller
    /// disabling the main window for the brief duration - which also stops a stray
    /// click from stealing focus mid-capture.
    /// </summary>
    internal sealed class ProgressDialog : Form
    {
        private readonly Label _label;

        public ProgressDialog(string initialText)
        {
            var bar = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Dock = DockStyle.Bottom,
                Height = 18
            };

            _label = new Label
            {
                Text = initialText,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(12)
            };

            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Format T-SQL Script";
            Width = 340;
            Height = 110;
            TopMost = true;

            Controls.Add(_label);
            Controls.Add(bar);
        }

        public void SetStatus(string text)
        {
            _label.Text = text;
            _label.Refresh();
        }
    }
}
