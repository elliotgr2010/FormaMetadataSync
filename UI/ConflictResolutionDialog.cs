using AccC3DMetadata.Models;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace AccC3DMetadata.UI
{
    /// <summary>
    /// Modal dialog shown during Sync Both when one or more mappings have
    /// <see cref="ConflictStrategy.Prompt"/> and ACC and DWG values differ.
    /// The user picks a resolution for each row and clicks Apply or Cancel.
    /// </summary>
    public class ConflictResolutionDialog : Window
    {
        public List<SyncConflict> Conflicts { get; }

        private static readonly SolidColorBrush AccBlue = Freeze(new SolidColorBrush(Color.FromRgb(0, 120, 212)));
        private static readonly SolidColorBrush TextDark = Freeze(new SolidColorBrush(Color.FromRgb(50, 49, 48)));
        private static readonly SolidColorBrush TextMuted = Freeze(new SolidColorBrush(Color.FromRgb(96, 94, 92)));
        private static readonly SolidColorBrush BorderGray = Freeze(new SolidColorBrush(Color.FromRgb(200, 198, 196)));
        private static readonly SolidColorBrush FooterBg = Freeze(new SolidColorBrush(Color.FromRgb(243, 242, 241)));
        private static readonly SolidColorBrush RowAlt = Freeze(new SolidColorBrush(Color.FromRgb(248, 247, 246)));

        public ConflictResolutionDialog(List<SyncConflict> conflicts)
        {
            Conflicts = conflicts;

            foreach (var c in conflicts)
                if (c.Resolution == ConflictStrategy.Prompt)
                    c.Resolution = ConflictStrategy.AccWins;

            Title = "ACC Sync — Resolve Conflicts";
            Width = 800;
            MinHeight = 300;
            MaxHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            SizeToContent = SizeToContent.Height;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });  // Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });      // Instruction
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Grid
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });      // Footer

            // ── Header ──────────────────────────────────────────────────────────────
            var header = new Border { Background = AccBlue, Padding = new Thickness(16, 0, 16, 0) };
            var headerPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            headerPanel.Children.Add(new TextBlock
            {
                Text = "Sync Conflicts",
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold
            });
            string countLabel = conflicts.Count == 1
                ? "1 attribute has a different value in Autodesk Forma and the open drawing"
                : $"{conflicts.Count} attributes have different values in Autodesk Forma and the open drawing";
            headerPanel.Children.Add(new TextBlock
            {
                Text = countLabel,
                Foreground = Freeze(new SolidColorBrush(Color.FromRgb(180, 215, 245))),
                FontSize = 11
            });
            header.Child = headerPanel;
            Grid.SetRow(header, 0);

            // ── Instruction ──────────────────────────────────────────────────────────
            var instruction = new TextBlock
            {
                Text = "Choose how to resolve each conflict, then click Apply. " +
                       "Click Cancel to skip all deferred conflicts — other mappings are unaffected.",
                Foreground = TextMuted,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(16, 10, 16, 6)
            };
            Grid.SetRow(instruction, 1);

            // ── Data grid ────────────────────────────────────────────────────────────
            var grid = new DataGrid
            {
                Margin = new Thickness(16, 4, 16, 4),
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = false,
                SelectionMode = DataGridSelectionMode.Single,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = BorderGray,
                AlternatingRowBackground = RowAlt,
                RowBackground = Brushes.White,
                BorderBrush = BorderGray,
                BorderThickness = new Thickness(1),
                ColumnHeaderHeight = 32,
                RowHeight = 30,
                ItemsSource = Conflicts
            };

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Attribute",
                Binding = new Binding("Mapping.AccAttributeName"),
                IsReadOnly = true,
                Width = new DataGridLength(180)
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Autodesk Forma value",
                Binding = new Binding("AccValue"),
                IsReadOnly = true,
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Drawing value",
                Binding = new Binding("DwgValue"),
                IsReadOnly = true,
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
            grid.Columns.Add(new DataGridComboBoxColumn
            {
                Header = "Resolution",
                SelectedItemBinding = new Binding("Resolution")
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                },
                Width = new DataGridLength(160),
                ItemsSource = new[] { ConflictStrategy.AccWins, ConflictStrategy.DwgWins, ConflictStrategy.Skip }
            });

            Grid.SetRow(grid, 2);

            // ── Footer ───────────────────────────────────────────────────────────────
            var footer = new Border
            {
                Background = FooterBg,
                BorderBrush = BorderGray,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 10, 16, 10)
            };
            var footerRow = new Grid();
            footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var hint = new TextBlock
            {
                Text = "AccWins — use the Autodesk Forma value   ·   DwgWins — keep the drawing value   ·   Skip — leave both unchanged",
                Foreground = TextMuted,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(hint, 0);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var applyBtn = new Button
            {
                Content = "Apply",
                Width = 90,
                Height = 28,
                IsDefault = true,
                Margin = new Thickness(0, 0, 8, 0)
            };
            applyBtn.Click += (_, _) => DialogResult = true;

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Width = 90,
                Height = 28,
                IsCancel = true
            };
            cancelBtn.Click += (_, _) => DialogResult = false;

            btnPanel.Children.Add(applyBtn);
            btnPanel.Children.Add(cancelBtn);
            Grid.SetColumn(btnPanel, 1);

            footerRow.Children.Add(hint);
            footerRow.Children.Add(btnPanel);
            footer.Child = footerRow;
            Grid.SetRow(footer, 3);

            root.Children.Add(header);
            root.Children.Add(instruction);
            root.Children.Add(grid);
            root.Children.Add(footer);
            Content = root;
        }

        private static T Freeze<T>(T freezable) where T : System.Windows.Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
