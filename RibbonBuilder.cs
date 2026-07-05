using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.Windows;
using System;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;   // brings in Freezable via WindowsBase
using AcadApplication = Autodesk.AutoCAD.ApplicationServices.Application;
using Freezable = System.Windows.Freezable;

namespace AccC3DMetadata
{
    internal static class RibbonBuilder
    {
        private const string TabId = "ACCSYNC_TAB";

        public static void BuildRibbon()
        {
#pragma warning disable CA1416 // Validate platform compatibility
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;

            var existing = ribbon.FindTab(TabId);
            if (existing != null)
                ribbon.Tabs.Remove(existing);

            var tab = new RibbonTab { Title = "ACC Sync", Id = TabId };

            // ── Sync operations panel ─────────────────────────────────────────────────
            var syncPanelSrc = new RibbonPanelSource { Title = "Autodesk Forma" };
            var syncPanel = new RibbonPanel { Source = syncPanelSrc };
            tab.Panels.Add(syncPanel);

            syncPanelSrc.Items.Add(MakeButton("Pull from Forma",
                "Pull attribute values from Autodesk Forma into the open drawing.",
                "AccSyncPull ",
                Icons.Pull32, Icons.Pull16));

            syncPanelSrc.Items.Add(MakeButton("Push to Forma",
                "Push attribute values from the open drawing to Autodesk Forma.",
                "AccSyncPush ",
                Icons.Push32, Icons.Push16));

            syncPanelSrc.Items.Add(MakeButton("Sync Both",
                "Bidirectional sync — pull and push, resolving conflicts interactively.",
                "AccSyncBoth ",
                Icons.Sync32, Icons.Sync16));

            // ── Configuration panel ───────────────────────────────────────────────────
            var cfgPanelSrc = new RibbonPanelSource { Title = "Configuration" };
            var cfgPanel = new RibbonPanel { Source = cfgPanelSrc };
            tab.Panels.Add(cfgPanel);

            cfgPanelSrc.Items.Add(MakeButton("Load Config",
                "Locate and validate the .accsync.xml config file for the current drawing.",
                "AccSyncLoadConfig ",
                Icons.Config32, Icons.Config16));

            cfgPanelSrc.Items.Add(MakeButton("Settings",
                "Enter or update the APS Client ID used for Autodesk authentication.",
                "AccSyncSettings ",
                Icons.Settings32, Icons.Settings16));

            ribbon.Tabs.Add(tab);
            tab.IsActive = true;
        }

        private static RibbonButton MakeButton(
            string label, string description, string command,
            ImageSource largeImage, ImageSource smallImage)
        {
            return new RibbonButton
            {
                Text = label,
                Description = description,
                ShowText = true,
                ShowImage = true,
                LargeImage = largeImage,
                Image = smallImage,
                Size = RibbonItemSize.Large,
                Orientation = Orientation.Vertical,
                CommandParameter = command,
                CommandHandler = new RelayCommandHandler()
            };
        }

        private class RelayCommandHandler : ICommand
        {
            public event EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object parameter) => true;
            public void Execute(object parameter)
            {
                if (parameter is RibbonButton btn)
                    AcadApplication.DocumentManager.MdiActiveDocument
                        ?.SendStringToExecute((string)btn.CommandParameter, true, false, false);
            }
        }
    }

    // ─── Vector icon factory ──────────────────────────────────────────────────────
    // All icons use a 32×32 logical coordinate space and are frozen for thread safety.
    // Two sizes are created for each icon: 32×32 (LargeImage) and 16×16 (Image).
    // Colors follow the Autodesk Platform Services blue palette used in ACC.

    internal static class Icons
    {
        // Autodesk/ACC blue palette
        private static readonly SolidColorBrush S_Blue = Freeze(new SolidColorBrush(Color.FromRgb(0, 120, 212)));
        private static readonly SolidColorBrush S_LightBlue = Freeze(new SolidColorBrush(Color.FromRgb(143, 203, 243)));
        private static readonly SolidColorBrush S_Green = Freeze(new SolidColorBrush(Color.FromRgb(16, 124, 65)));
        private static readonly SolidColorBrush S_LightGreen = Freeze(new SolidColorBrush(Color.FromRgb(168, 220, 176)));
        private static readonly SolidColorBrush S_Gray = Freeze(new SolidColorBrush(Color.FromRgb(96, 94, 92)));
        private static readonly SolidColorBrush S_LightGray = Freeze(new SolidColorBrush(Color.FromRgb(200, 198, 196)));
        private static readonly SolidColorBrush S_White = Freeze(new SolidColorBrush(Colors.White));

        public static readonly ImageSource Pull32 = MakePull();
        public static readonly ImageSource Pull16 = Scale(MakePull(), 16);
        public static readonly ImageSource Push32 = MakePush();
        public static readonly ImageSource Push16 = Scale(MakePush(), 16);
        public static readonly ImageSource Sync32 = MakeSync();
        public static readonly ImageSource Sync16 = Scale(MakeSync(), 16);
        public static readonly ImageSource Config32 = MakeConfig();
        public static readonly ImageSource Config16 = Scale(MakeConfig(), 16);
        public static readonly ImageSource Settings32 = MakeSettings();
        public static readonly ImageSource Settings16 = Scale(MakeSettings(), 16);

        // ── Pull (Docs → DWG): cloud above, filled down-arrow, DWG bar below ─────

        private static ImageSource MakePull()
        {
            var g = new DrawingGroup();

            // Cloud shape representing Autodesk Docs (top)
            g.Children.Add(Draw(S_LightBlue,
                "M 6,12 C 6,7 10,5 14,6 C 15,3 20,1 23,4 C 28,4 29,9 26,11 L 8,11 C 7,11 6,12 6,12 Z"));

            // Arrow shaft + arrowhead pointing down
            g.Children.Add(Draw(S_Blue,
                "M 13,10 L 19,10 L 19,20 L 23,20 L 16,29 L 9,20 L 13,20 Z"));

            // DWG bar at bottom
            g.Children.Add(Draw(S_Gray,
                "M 3,30 L 29,30 L 29,32 L 3,32 Z"));

            return Freeze(new DrawingImage(g));
        }

        // ── Push (DWG → Docs): DWG bar above, filled up-arrow, cloud below ────────

        private static ImageSource MakePush()
        {
            var g = new DrawingGroup();

            // DWG bar at top
            g.Children.Add(Draw(S_Gray,
                "M 3,0 L 29,0 L 29,2 L 3,2 Z"));

            // Arrow shaft + arrowhead pointing up
            g.Children.Add(Draw(S_Green,
                "M 13,22 L 19,22 L 19,12 L 23,12 L 16,3 L 9,12 L 13,12 Z"));

            // Cloud shape representing Autodesk Docs (bottom destination)
            g.Children.Add(Draw(S_LightGreen,
                "M 6,30 C 6,25 10,23 14,24 C 15,21 20,19 23,22 C 28,22 29,27 26,29 L 8,29 C 7,29 6,30 6,30 Z"));

            return Freeze(new DrawingImage(g));
        }

        // ── Sync Both (bidirectional): up-left arrow + down-right arrow ───────────

        private static ImageSource MakeSync()
        {
            var g = new DrawingGroup();

            // Left column: up arrow (DWG → Docs)
            g.Children.Add(Draw(S_Green,
                "M 4,22 L 8,22 L 8,13 L 11,13 L 7,5 L 3,13 L 6,13 L 6,22 Z"));

            // Right column: down arrow (Docs → DWG)
            g.Children.Add(Draw(S_Blue,
                "M 21,10 L 25,10 L 25,19 L 28,19 L 24,27 L 20,19 L 23,19 L 23,10 Z"));

            // Connecting arc (top: green to blue)
            g.Children.Add(DrawStroke(S_Blue, 1.5,
                "M 7,6 C 12,1 20,1 24,8"));

            // Connecting arc (bottom: blue to green)
            g.Children.Add(DrawStroke(S_Green, 1.5,
                "M 24,26 C 19,31 11,31 7,24"));

            return Freeze(new DrawingImage(g));
        }

        // ── Load Config (document with settings lines) ────────────────────────────

        private static ImageSource MakeConfig()
        {
            var g = new DrawingGroup();

            // Document body
            g.Children.Add(Draw(S_LightGray,
                "M 5,1 L 21,1 L 27,7 L 27,31 L 5,31 Z"));

            // Folded corner (dog-ear)
            g.Children.Add(Draw(S_Gray,
                "M 21,1 L 21,7 L 27,7 Z"));

            // Horizontal content lines
            var linePen = new Pen(S_Gray, 1.5);
            linePen.Freeze();
            g.Children.Add(new GeometryDrawing(null, linePen, Geometry.Parse("M 9,13 L 23,13")));
            g.Children.Add(new GeometryDrawing(null, linePen, Geometry.Parse("M 9,17 L 23,17")));
            g.Children.Add(new GeometryDrawing(null, linePen, Geometry.Parse("M 9,21 L 18,21")));

            // Small gear/settings indicator (circle with tick marks) at bottom-right
            g.Children.Add(Draw(S_Blue,
                "M 22,24 C 22,21 27,21 27,24 C 27,27 22,27 22,24 Z"));
            g.Children.Add(Draw(S_White,
                "M 23.5,24 C 23.5,22.9 26.5,22.9 26.5,24 C 26.5,25.1 23.5,25.1 23.5,24 Z"));

            return Freeze(new DrawingImage(g));
        }

        // ── Settings (three-line slider / adjustment icon) ────────────────────────

        private static ImageSource MakeSettings()
        {
            var g = new DrawingGroup();

            // Three horizontal track lines
            g.Children.Add(Draw(S_LightGray, "M 3,9  L 29,9  L 29,11 L 3,11  Z"));
            g.Children.Add(Draw(S_LightGray, "M 3,17 L 29,17 L 29,19 L 3,19  Z"));
            g.Children.Add(Draw(S_LightGray, "M 3,25 L 29,25 L 29,27 L 3,27  Z"));

            // Slider knobs at staggered positions on each track
            var k1 = new System.Windows.Media.EllipseGeometry(new System.Windows.Point(10, 10), 4, 4);
            k1.Freeze();
            g.Children.Add(new GeometryDrawing(S_Blue, null, k1));

            var k2 = new System.Windows.Media.EllipseGeometry(new System.Windows.Point(21, 18), 4, 4);
            k2.Freeze();
            g.Children.Add(new GeometryDrawing(S_Blue, null, k2));

            var k3 = new System.Windows.Media.EllipseGeometry(new System.Windows.Point(13, 26), 4, 4);
            k3.Freeze();
            g.Children.Add(new GeometryDrawing(S_Blue, null, k3));

            return Freeze(new DrawingImage(g));
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static GeometryDrawing Draw(Brush fill, string miniLanguagePath)
        {
            var geo = Geometry.Parse(miniLanguagePath);
            geo.Freeze();
            return new GeometryDrawing(fill, null, geo);
        }

        private static GeometryDrawing DrawStroke(Brush stroke, double thickness, string miniLanguagePath)
        {
            var pen = new Pen(stroke, thickness);
            pen.Freeze();
            var geo = Geometry.Parse(miniLanguagePath);
            geo.Freeze();
            return new GeometryDrawing(null, pen, geo);
        }

        // Wraps a DrawingImage in a DrawingGroup scaled to the requested pixel size.
        private static ImageSource Scale(ImageSource source, double size)
        {
            if (source is not DrawingImage di) return source;

            var scaleGroup = new DrawingGroup();
            var bounds = di.Drawing.Bounds;
            if (bounds.IsEmpty || bounds.Width == 0 || bounds.Height == 0)
                return source;

            double sx = size / bounds.Width;
            double sy = size / bounds.Height;
            var xform = new DrawingGroup { Transform = new ScaleTransform(sx, sy) };
            xform.Children.Add(di.Drawing);
            scaleGroup.Children.Add(xform);

            return Freeze(new DrawingImage(scaleGroup));
        }

        private static T Freeze<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
