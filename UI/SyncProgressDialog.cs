using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AccC3DMetadata.UI
{
    /// <summary>
    /// Non-modal progress dialog shown during a sync operation. Display it with
    /// <c>progressDlg.Show()</c> before starting the async work, update it via
    /// <see cref="UpdateStatus"/>, and close it with <c>progressDlg.Close()</c> in a
    /// <c>finally</c> block to guarantee it is dismissed even on failure.
    /// </summary>
    public class SyncProgressDialog : Window
    {
        private readonly TextBlock _statusText;

        private static readonly SolidColorBrush AccBlue = Freeze(new SolidColorBrush(Color.FromRgb(0, 120, 212)));
        private static readonly SolidColorBrush TextDark = Freeze(new SolidColorBrush(Color.FromRgb(50, 49, 48)));
        private static readonly SolidColorBrush TextMuted = Freeze(new SolidColorBrush(Color.FromRgb(96, 94, 92)));

        public SyncProgressDialog(string operationTitle)
        {
            Title = "ACC Sync";
            Width = 400;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.ToolWindow;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Blue header bar
            var header = new Border
            {
                Background = AccBlue,
                Padding = new Thickness(16, 0, 16, 0)
            };
            var headerText = new TextBlock
            {
                Text = operationTitle,
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            header.Child = headerText;
            Grid.SetRow(header, 0);

            // Body: status message + indeterminate progress bar
            var body = new StackPanel { Margin = new Thickness(16, 14, 16, 16) };

            _statusText = new TextBlock
            {
                Text = "Initialising…",
                Foreground = TextDark,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var progressBar = new ProgressBar
            {
                IsIndeterminate = true,
                Height = 4,
                Foreground = AccBlue,
                Background = Freeze(new SolidColorBrush(Color.FromRgb(225, 223, 221))),
                BorderThickness = new Thickness(0)
            };

            body.Children.Add(_statusText);
            body.Children.Add(progressBar);
            Grid.SetRow(body, 1);

            root.Children.Add(header);
            root.Children.Add(body);
            Content = root;
        }

        /// <summary>Updates the status message shown below the progress bar.</summary>
        public void UpdateStatus(string message) => _statusText.Text = message;

        private static T Freeze<T>(T freezable) where T : System.Windows.Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
