using AccC3DMetadata.Services;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;

// Registers this class as the AutoCAD extension application entry point.
// AutoCAD discovers it via reflection on plugin load.
[assembly: ExtensionApplication(typeof(AccC3DMetadata.PluginEntry))]

namespace AccC3DMetadata
{
    public class PluginEntry : IExtensionApplication
    {
        public void Initialize()
        {
            if (ComponentManager.Ribbon != null)
                RibbonBuilder.BuildRibbon();
            else
                ComponentManager.ItemInitialized += OnItemInitialized;
        }

        private static void OnItemInitialized(object sender, RibbonItemEventArgs e)
        {
            if (ComponentManager.Ribbon == null) return;
            ComponentManager.ItemInitialized -= OnItemInitialized;
            RibbonBuilder.BuildRibbon();
        }

        /// <summary>
        /// Called by AutoCAD when the plugin is unloaded (e.g. on application exit).
        /// Clears the cached OAuth token so it is not left in memory after the session ends.
        /// </summary>
        public void Terminate()
        {
            TokenCache.Invalidate();
        }
    }
}
