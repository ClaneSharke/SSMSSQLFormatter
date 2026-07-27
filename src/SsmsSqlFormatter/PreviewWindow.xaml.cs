using System.Windows;

namespace SsmsSqlFormatter
{
    public partial class PreviewWindow : Window
    {
        public PreviewWindow(string formattedSql)
        {
            InitializeComponent();
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
    }
}
