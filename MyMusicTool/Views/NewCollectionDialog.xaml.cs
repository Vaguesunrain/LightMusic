using System.Windows;
using System.Windows.Input;

namespace MyMusicTool.Views
{
    public partial class NewCollectionDialog : Window
    {
        public string CollectionName { get; private set; } = string.Empty;

        public NewCollectionDialog()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                NameInputBox.Focus();
                Keyboard.Focus(NameInputBox);
            };
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            string text = NameInputBox.Text.Trim();
            if (string.IsNullOrEmpty(text))
            {
                NameInputBox.Focus();
                return;
            }
            CollectionName = text;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void NameInputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Confirm_Click(sender, e);
            else if (e.Key == Key.Escape) Cancel_Click(sender, e);
        }
    }
}