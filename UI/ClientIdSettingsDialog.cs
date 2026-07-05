using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AccC3DMetadata.UI
{
    /// <summary>
    /// Modal dialog that allows the user to enter and save their APS Client ID.
    /// The value is persisted to the user-profile location managed by <see cref="ClientConfig"/>.
    /// </summary>
    public class ClientIdSettingsDialog : Window
    {
        private readonly TextBox _clientIdBox;

        private static readonly SolidColorBrush AccBlue = Freeze(new SolidColorBrush(Color.FromRgb(0, 120, 212)));
        private static readonly SolidColorBrush TextDark = Freeze(new SolidColorBrush(Color.FromRgb(50, 49, 48)));
        private static readonly SolidColorBrush TextMuted = Freeze(new SolidColorBrush(Color.FromRgb(96, 94, 92)));
        private static readonly SolidColorBrush TextSubtle = Freeze(new SolidColorBrush(Color.FromRgb(130, 128, 126)));
        private static readonly SolidColorBrush BorderGray = Freeze(new SolidColorBrush(Color.FromRgb(200, 198, 196)));
        private static readonly SolidColorBrush FooterBg = Freeze(new SolidColorBrush(Color.FromRgb(243, 242, 241)));

        public ClientIdSettingsDialog()
        {
            Title = "ACC Sync — APS Client ID";
            Width = 500;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            var header = new Border { Background = AccBlue, Padding = new Thickness(16, 0, 16, 0) };
            header.Child = new TextBlock
            {
                Text = "APS Application — Client ID",
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(header, 0);

            // Form body
            var form = new StackPanel { Margin = new Thickness(16, 14, 16, 16) };

            form.Children.Add(new TextBlock
            {
                Text = "The Client ID identifies your APS application for OAuth authentication. " +
                       "It is stored in your Windows user profile and is not shared with other users on this machine.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = TextMuted,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 14)
            });

            form.Children.Add(new TextBlock
            {
                Text = "Client ID",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextDark,
                Margin = new Thickness(0, 0, 0, 4)
            });

            _clientIdBox = new TextBox
            {
                Text = ClientConfig.ClientId ?? string.Empty,
                Height = 30,
                Padding = new Thickness(8, 5, 8, 5),
                FontSize = 12,
                FontFamily = new FontFamily("Consolas"),
                BorderBrush = BorderGray,
                BorderThickness = new Thickness(1)
            };
            form.Children.Add(_clientIdBox);

            string storedAt = ClientConfig.AppDataClientIdPath;
            form.Children.Add(new TextBlock
            {
                Text = $"Stored at: {storedAt}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = TextSubtle,
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 0)
            });

            Grid.SetRow(form, 1);

            // Footer with buttons
            var footer = new Border
            {
                Background = FooterBg,
                BorderBrush = BorderGray,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 10, 16, 10)
            };
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var saveBtn = new Button
            {
                Content = "Save",
                Width = 88,
                Height = 28,
                IsDefault = true,
                Margin = new Thickness(0, 0, 8, 0)
            };
            saveBtn.Click += OnSave;

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Width = 88,
                Height = 28,
                IsCancel = true
            };
            cancelBtn.Click += (_, _) => DialogResult = false;

            btnRow.Children.Add(saveBtn);
            btnRow.Children.Add(cancelBtn);
            footer.Child = btnRow;
            Grid.SetRow(footer, 2);

            root.Children.Add(header);
            root.Children.Add(form);
            root.Children.Add(footer);
            Content = root;
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            string id = _clientIdBox.Text.Trim();
            if (string.IsNullOrEmpty(id))
            {
                MessageBox.Show("Please enter a Client ID before saving.", "ACC Sync",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ClientConfig.SaveClientId(id);
            DialogResult = true;
        }

        private static T Freeze<T>(T freezable) where T : System.Windows.Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
