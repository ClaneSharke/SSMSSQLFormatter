using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using SsmsSqlFormatter.Formatting;

namespace SsmsSqlFormatter
{
    public partial class PreviewWindow : Window
    {
        private readonly string _originalSql;

        public PreviewWindow(string originalSql, string formattedSql)
        {
            InitializeComponent();
            _originalSql = originalSql ?? string.Empty;
            PreviewText.Text = formattedSql ?? string.Empty;
        }

        public string FormattedText => PreviewText.Text;

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            // Update the formatted SQL from the editor in case the user made tweaks
            this.DialogResult = true;
            this.Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void ShowDiffCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (ShowDiffCheckBox.IsChecked == true)
            {
                DiffText.Document = BuildDiffDocument(_originalSql, PreviewText.Text);
                PreviewText.Visibility = Visibility.Collapsed;
                DiffText.Visibility = Visibility.Visible;
            }
            else
            {
                DiffText.Visibility = Visibility.Collapsed;
                PreviewText.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Renders a unified diff: unchanged lines in the default color, removed
        /// lines (from the original script) in red with a "- " prefix, added lines
        /// (from the formatted result) in green with a "+ " prefix.
        /// </summary>
        private static FlowDocument BuildDiffDocument(string original, string formatted)
        {
            var doc = new FlowDocument { FontFamily = new FontFamily("Consolas"), FontSize = 12 };
            var paragraph = new Paragraph { Margin = new Thickness(0) };

            foreach (var line in LineDiff.Compute(original, formatted))
            {
                string prefix;
                Brush brush;
                switch (line.Op)
                {
                    case DiffOp.Delete:
                        prefix = "- ";
                        brush = Brushes.Firebrick;
                        break;
                    case DiffOp.Insert:
                        prefix = "+ ";
                        brush = Brushes.SeaGreen;
                        break;
                    default:
                        prefix = "  ";
                        brush = Brushes.Gray;
                        break;
                }

                var run = new Run(prefix + line.Text) { Foreground = brush };
                paragraph.Inlines.Add(run);
                paragraph.Inlines.Add(new LineBreak());
            }

            doc.Blocks.Add(paragraph);
            return doc;
        }
    }
}
