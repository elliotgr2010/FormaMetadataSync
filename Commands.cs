using AccC3DMetadata.Models;
using AccC3DMetadata.Services;
using AccC3DMetadata.UI;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using System;

namespace AccC3DMetadata
{
    /// <summary>
    /// Registers the ACC Sync typed commands with AutoCAD.
    /// Each sync command is <see cref="CommandFlags.Modal"/> so AutoCAD blocks other input
    /// while the sync operation is in progress.
    /// </summary>
    /// <remarks>
    /// Commands are <c>async void</c> because AutoCAD's command infrastructure does not
    /// support <c>Task</c>-returning methods. Exceptions are caught and reported via the
    /// command line — they must not propagate to the AutoCAD runtime.
    /// </remarks>
    public class Commands : Functions
    {
        /// <summary>
        /// Pulls attribute values from ACC into the open DWG (ACC → DWG, read-only direction).
        /// Invoked via ribbon button or by typing <c>AccSyncPull</c> at the command line.
        /// </summary>
        [CommandMethod("C3DCMD", "AccSyncPull", CommandFlags.Modal)]
        public async void AccSyncPull()
        {
            SyncProgressDialog progressDlg = null;
            try
            {
                progressDlg = new SyncProgressDialog("Pull from Autodesk Forma");
                progressDlg.Show();

                var orchestrator = new SyncOrchestrator(AcadDoc);
                progressDlg.UpdateStatus("Loading configuration…");
                var config = await orchestrator.LoadConfigAsync();

                var result = await orchestrator.RunAsync(config, SyncDirection.Read,
                    new Progress<string>(msg => progressDlg.UpdateStatus(msg)));

                Ed.WriteMessage($"\nPull complete — {result.MappingsApplied} mapping(s) applied, {result.Errors} error(s).");
            }
            catch (System.Exception ex)
            {
                Ed.WriteMessage($"\nAccSyncPull failed: {ex.Message}");
            }
            finally
            {
                progressDlg?.Close();
            }
        }

        /// <summary>
        /// Pushes attribute values from the open DWG to ACC (DWG → ACC, write-only direction).
        /// Invoked via ribbon button or by typing <c>AccSyncPush</c> at the command line.
        /// </summary>
        [CommandMethod("C3DCMD", "AccSyncPush", CommandFlags.Modal)]
        public async void AccSyncPush()
        {
            SyncProgressDialog progressDlg = null;
            try
            {
                progressDlg = new SyncProgressDialog("Push to Autodesk Forma");
                progressDlg.Show();

                var orchestrator = new SyncOrchestrator(AcadDoc);
                progressDlg.UpdateStatus("Loading configuration…");
                var config = await orchestrator.LoadConfigAsync();

                var result = await orchestrator.RunAsync(config, SyncDirection.Write,
                    new Progress<string>(msg => progressDlg.UpdateStatus(msg)));

                Ed.WriteMessage($"\nPush complete — {result.MappingsApplied} mapping(s) applied, {result.Errors} error(s).");
            }
            catch (System.Exception ex)
            {
                Ed.WriteMessage($"\nAccSyncPush failed: {ex.Message}");
            }
            finally
            {
                progressDlg?.Close();
            }
        }

        /// <summary>
        /// Performs a bidirectional sync between the open DWG and ACC, showing the
        /// <see cref="ConflictResolutionDialog"/> for any mappings with conflicting values
        /// and a <see cref="ConflictStrategy.Prompt"/> strategy.
        /// Invoked via ribbon button or by typing <c>AccSyncBoth</c> at the command line.
        /// </summary>
        [CommandMethod("C3DCMD", "AccSyncBoth", CommandFlags.Modal)]
        public async void AccSyncBoth()
        {
            SyncProgressDialog progressDlg = null;
            try
            {
                progressDlg = new SyncProgressDialog("Sync with Autodesk Forma");
                progressDlg.Show();

                var orchestrator = new SyncOrchestrator(AcadDoc);
                progressDlg.UpdateStatus("Loading configuration…");
                var config = await orchestrator.LoadConfigAsync();

                var result = await orchestrator.RunAsync(config, SyncDirection.ReadWrite,
                    new Progress<string>(msg => progressDlg.UpdateStatus(msg)));

                Ed.WriteMessage(
                    $"\nSync complete — {result.MappingsApplied} mapping(s), " +
                    $"{result.ConflictsResolved} conflict(s) resolved, " +
                    $"{result.ConflictsCancelled} conflict(s) cancelled, " +
                    $"{result.Errors} error(s).");
            }
            catch (System.Exception ex)
            {
                Ed.WriteMessage($"\nAccSyncBoth failed: {ex.Message}");
            }
            finally
            {
                progressDlg?.Close();
            }
        }

        /// <summary>
        /// Loads and displays the mapping configuration for the open drawing without performing
        /// any sync. Useful for verifying that the config file is correctly placed and parsed.
        /// Invoked via ribbon button or by typing <c>AccSyncLoadConfig</c> at the command line.
        /// </summary>
        [CommandMethod("C3DCMD", "AccSyncLoadConfig", CommandFlags.Modal)]
        public async void AccSyncLoadConfig()
        {
            try
            {
                var orchestrator = new SyncOrchestrator(AcadDoc);
                var config = await orchestrator.LoadConfigAsync();

                Ed.WriteMessage($"\nConfig loaded successfully.");
                Ed.WriteMessage($"\n  Hub:      {config.HubId ?? "(resolved from Desktop Connector path)"}");
                Ed.WriteMessage($"\n  Project:  {config.ProjectId ?? "(resolved from Desktop Connector path)"}");
                Ed.WriteMessage($"\n  Item:     {config.DrawingItemId ?? "(resolved from Desktop Connector path)"}");
                Ed.WriteMessage($"\n  Mappings: {config.Mappings.Count}");

                foreach (var m in config.Mappings)
                {
                    string target = m.Target == MappingTarget.BlockAttribute
                        ? $"{m.BlockName}.{m.BlockAttributeTag}"
                        : $"{m.PropertySetName}.{m.PropertyName}";
                    Ed.WriteMessage($"\n    [{m.Direction}] {m.AccAttributeName} → {m.Target}:{target}");
                }
            }
            catch (System.Exception ex)
            {
                Ed.WriteMessage($"\nAccSyncLoadConfig failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Opens the Settings dialog where the user can enter and save their APS Client ID.
        /// The ID is stored in the user profile and persists across plugin updates.
        /// Invoked via ribbon button or by typing <c>AccSyncSettings</c> at the command line.
        /// </summary>
        [CommandMethod("C3DCMD", "AccSyncSettings", CommandFlags.Modal)]
        public void AccSyncSettings()
        {
            try
            {
                var dlg = new ClientIdSettingsDialog();
                bool saved = Application.ShowModalWindow(dlg) == true;
                if (saved)
                    Ed.WriteMessage("\nAPS Client ID saved. It will be used for the next authentication.");
            }
            catch (System.Exception ex)
            {
                Ed.WriteMessage($"\nAccSyncSettings failed: {ex.Message}");
            }
        }
    }
}
