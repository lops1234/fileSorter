using System;
using System.Windows;

namespace FileTagger.Windows
{
    public partial class RenameTagWindow : Window
    {
        private readonly string _currentName;

        public string NewName { get; private set; } = string.Empty;

        public RenameTagWindow(string currentName)
        {
            InitializeComponent();
            _currentName = currentName;
            CurrentNameTextBlock.Text = $"Rename tag '{currentName}'";
            NewNameTextBox.Text = currentName;

            Loaded += (_, __) =>
            {
                NewNameTextBox.Focus();
                NewNameTextBox.SelectAll();
            };
        }

        private void Rename_Click(object sender, RoutedEventArgs e)
        {
            var newName = NewNameTextBox.Text?.Trim() ?? string.Empty;
            if (newName.Length == 0)
            {
                MessageBox.Show("Please enter a tag name.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.Equals(newName, _currentName, StringComparison.Ordinal))
            {
                DialogResult = false;
                return;
            }

            NewName = newName;
            DialogResult = true;
        }
    }
}
