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
        /// Renders a unified, syntax-colored diff: each line gets a pale red/green
        /// background for removed/added lines (unchanged lines get none), a "-"/"+"/" "
        /// marker in the matching strong color, and its T-SQL tokens colored by kind
        /// (keyword, string, number, comment) within that.
        /// </summary>
        private static FlowDocument BuildDiffDocument(string original, string formatted)
        {
            var doc = new FlowDocument { FontFamily = new FontFamily("Consolas"), FontSize = 12 };
            var paragraph = new Paragraph { Margin = new Thickness(0) };

            foreach (var line in LineDiff.Compute(original, formatted))
            {
                string prefix;
                Brush markerBrush;
                Brush lineBackground;
                switch (line.Op)
                {
                    case DiffOp.Delete:
                        prefix = "- ";
                        markerBrush = Brushes.Firebrick;
                        lineBackground = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xEE));
                        break;
                    case DiffOp.Insert:
                        prefix = "+ ";
                        markerBrush = Brushes.SeaGreen;
                        lineBackground = new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9));
                        break;
                    default:
                        prefix = "  ";
                        markerBrush = Brushes.Gray;
                        lineBackground = null;
                        break;
                }

                var markerRun = new Run(prefix) { Foreground = markerBrush, Background = lineBackground };
                paragraph.Inlines.Add(markerRun);

                foreach (var (text, category) in SqlSyntaxHighlighter.TokenizeLine(line.Text))
                {
                    paragraph.Inlines.Add(new Run(text)
                    {
                        Foreground = BrushForCategory(category),
                        Background = lineBackground
                    });
                }

                paragraph.Inlines.Add(new LineBreak());
            }

            doc.Blocks.Add(paragraph);
            return doc;
        }

        private static Brush BrushForCategory(SqlTokenCategory category)
        {
            switch (category)
            {
                case SqlTokenCategory.Keyword: return new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0xFF));
                case SqlTokenCategory.String: return new SolidColorBrush(Color.FromRgb(0xA3, 0x15, 0x15));
                case SqlTokenCategory.Number: return new SolidColorBrush(Color.FromRgb(0x09, 0x86, 0x58));
                case SqlTokenCategory.Comment: return Brushes.Gray;
                case SqlTokenCategory.Identifier: return Brushes.Black;
                default: return Brushes.Black;
            }
        }
    }
}
