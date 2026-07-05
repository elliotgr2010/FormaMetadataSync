using System;
using System.IO;
using System.Reflection;

namespace AccC3DMetadata
{
    /// <summary>
    /// Provides the APS OAuth client configuration used by <see cref="AuthService"/>
    /// and <see cref="Services.TokenCache"/>.
    /// </summary>
    /// <remarks>
    /// The Client ID lookup order:
    /// <list type="number">
    ///   <item><description>
    ///     <c>%APPDATA%\AccC3DSync\accsync.clientid</c> — user-profile location set via the
    ///     ribbon Settings dialog. This location survives plugin reinstalls and rebuilds.
    ///   </description></item>
    ///   <item><description>
    ///     <c>accsync.clientid</c> in the plugin DLL directory — backward-compatible fallback
    ///     for installations that already use the old file-based approach.
    ///   </description></item>
    /// </list>
    /// Use the ribbon <b>Settings</b> button (or <c>AccSyncSettings</c> command) to enter the
    /// ID through the UI — no manual file editing required.
    /// </remarks>
    internal static class ClientConfig
    {
        private const string ClientIdFileName = "accsync.clientid";
        private const string AppDataFolderName = "AccC3DSync";

        /// <summary>
        /// The loopback redirect URI registered in the APS application.
        /// Must match exactly what was entered in the APS Developer Portal.
        /// </summary>
        public const string RedirectUri = "http://localhost:8080/";

        /// <summary>
        /// Full path to the user-profile client ID file. Exposed so the Settings dialog
        /// can show the user exactly where the value is stored.
        /// </summary>
        public static string AppDataClientIdPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppDataFolderName, ClientIdFileName);

        /// <summary>
        /// The APS application Client ID. Checks the user-profile location first, then
        /// falls back to the plugin directory. Returns <c>null</c> if neither file exists
        /// or both are empty, which causes authentication to fail with a clear error.
        /// </summary>
        public static string ClientId => ReadClientId();

        /// <summary>
        /// Saves <paramref name="clientId"/> to the user-profile location so that it
        /// persists across plugin updates and rebuilds. Creates the directory if needed.
        /// </summary>
        public static void SaveClientId(string clientId)
        {
            string dir = Path.GetDirectoryName(AppDataClientIdPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(AppDataClientIdPath, clientId.Trim());
        }

        private static string ReadClientId()
        {
            // 1. User-profile location (written by the Settings dialog).
            if (TryReadFile(AppDataClientIdPath, out string id1)) return id1;

            // 2. Plugin directory (backward-compatible fallback).
            string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                ?? string.Empty;
            if (TryReadFile(Path.Combine(pluginDir, ClientIdFileName), out string id2)) return id2;

            return null; // Caller (TokenCache / Functions) will produce a meaningful error.
        }

        private static bool TryReadFile(string path, out string value)
        {
            value = null;
            if (!File.Exists(path)) return false;
            string text = File.ReadAllText(path).Trim();
            if (string.IsNullOrEmpty(text)) return false;
            value = text;
            return true;
        }
    }
}
