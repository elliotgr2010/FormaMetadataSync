using AccC3DMetadata.Config;
using AccC3DMetadata.Models;
using AccC3DMetadata.UI;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AccC3DMetadata.Services
{
    /// <summary>
    /// Carries the outcome counters from a completed sync operation, used to build the
    /// summary message written to the AutoCAD command line.
    /// </summary>
    public class SyncResult
    {
        /// <summary>Total number of mappings that were processed (including those with errors).</summary>
        public int MappingsApplied { get; set; }

        /// <summary>Number of Prompt-strategy conflicts that the user resolved via the dialog.</summary>
        public int ConflictsResolved { get; set; }

        /// <summary>Number of Prompt-strategy conflicts that the user cancelled in the dialog.</summary>
        public int ConflictsCancelled { get; set; }

        /// <summary>Number of mappings that failed with an exception.</summary>
        public int Errors { get; set; }
    }

    /// <summary>
    /// Coordinates the full sync pipeline between ACC (Autodesk Construction Cloud) and
    /// the currently open DWG document.
    /// </summary>
    /// <remarks>
    /// The pipeline runs as follows:
    /// <list type="number">
    ///   <item><description>Obtain a valid OAuth access token via <see cref="TokenCache"/>.</description></item>
    ///   <item><description>Resolve the ACC hub, project, and item IDs for the open drawing.</description></item>
    ///   <item><description>Fetch the attribute definition maps (name ↔ ID) and current ACC values.</description></item>
    ///   <item><description>Open an AutoCAD transaction and apply each mapping according to direction and conflict strategy.</description></item>
    ///   <item><description>Show the <see cref="ConflictResolutionDialog"/> for any Prompt-strategy conflicts.</description></item>
    ///   <item><description>Commit the DWG transaction and batch-write any ACC updates.</description></item>
    /// </list>
    /// <para>
    /// <b>Threading:</b> all awaits in <see cref="RunAsync"/> intentionally capture AutoCAD's
    /// STA <see cref="System.Threading.SynchronizationContext"/> (i.e. no
    /// <c>ConfigureAwait(false)</c>) so that DWG database operations and the WPF conflict dialog
    /// always execute on the main STA thread. HTTP calls in <see cref="AccFileService"/> do use
    /// <c>ConfigureAwait(false)</c> internally, so the actual network I/O still runs off-thread.
    /// </para>
    /// </remarks>
    public class SyncOrchestrator
    {
        private readonly Document _doc;
        private readonly Editor _ed;
        private readonly AccFileService _acc = new();

        /// <summary>
        /// Initialises the orchestrator for the specified AutoCAD document.
        /// </summary>
        /// <param name="doc">The active AutoCAD document to sync.</param>
        public SyncOrchestrator(Document doc)
        {
            _doc = doc;
            _ed = doc.Editor;
        }

        /// <summary>
        /// Executes the sync pipeline for all mappings in <paramref name="config"/> in the
        /// specified <paramref name="direction"/>.
        /// </summary>
        /// <param name="config">Parsed mapping configuration loaded via <see cref="LoadConfigAsync"/>.</param>
        /// <param name="direction">
        /// Controls the overall data-flow direction. Individual mapping directions may further
        /// restrict this — a mapping marked <see cref="SyncDirection.Read"/> will never write
        /// even if the command direction is <see cref="SyncDirection.ReadWrite"/>.
        /// </param>
        /// <returns>A <see cref="SyncResult"/> describing the outcome of the operation.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when authentication fails, the ACC item cannot be resolved, or the parent
        /// folder ID or version URN cannot be determined.
        /// </exception>
        /// <param name="progress">
        /// Optional progress sink. Each step of the pipeline reports a short human-readable
        /// status string so callers can surface it in a UI (e.g. <see cref="SyncProgressDialog"/>).
        /// </param>
        public async Task<SyncResult> RunAsync(SyncConfig config, SyncDirection direction,
            IProgress<string> progress = null)
        {
            progress?.Report("Authenticating with Autodesk Platform Services…");
            string token = await TokenCache.GetAccessTokenAsync();
            if (token == null)
                throw new InvalidOperationException("Could not obtain an access token.");

            // Resolve the ACC project and item IDs for the open drawing.
            // Hub ID is needed internally by the resolution methods but is not used afterwards;
            // it is discarded with the _ pattern.
            progress?.Report("Resolving ACC project and item…");
            string projectId, itemId;
            string dwgPath = _doc.Database.Filename;

            if (!string.IsNullOrEmpty(config.DrawingItemId))
            {
                // The config file explicitly specifies the item ID — derive project from the path.
                itemId = config.DrawingItemId;
                try
                {
                    (_, projectId) = await _acc.ResolveHubAndProjectAsync(dwgPath, token);
                }
                catch (InvalidOperationException) when (
                    !string.IsNullOrEmpty(config.HubId) && !string.IsNullOrEmpty(config.ProjectId))
                {
                    // Drawing is not under a Desktop Connector folder; use the IDs from the config file.
                    projectId = config.ProjectId;
                }
            }
            else
            {
                // No explicit item ID in config — derive all three IDs from the Desktop Connector path.
                (_, projectId, itemId) = await _acc.ResolveItemFromDrawingPathAsync(dwgPath, token);
            }

            // The attribute definitions endpoint is folder-scoped, so we need the item's parent folder.
            progress?.Report("Fetching attribute definitions…");
            string folderId = await _acc.GetItemParentFolderIdAsync(projectId, itemId, token);
            if (folderId == null)
                throw new InvalidOperationException(
                    $"Could not resolve the parent folder for item '{itemId}'. Cannot fetch attribute definitions.");

            // Fetch the definition maps (name ↔ ID) and the current ACC attribute values in parallel context.
            var (nameToId, idToName) = await _acc.GetAttributeDefinitionMapsAsync(projectId, folderId, token);

            // GetCustomAttributesAsync also returns the version URN needed by the write endpoint.
            // The version URN is different from the item lineage URN — it identifies a specific version.
            progress?.Report("Reading current attribute values from Autodesk Docs…");
            var (accValues, versionUrn) = await _acc.GetCustomAttributesAsync(projectId, itemId, idToName, token);
            if (versionUrn == null)
                throw new InvalidOperationException(
                    $"Could not determine the ACC version URN for item '{itemId}'. Cannot write attribute updates.");

            // Report any mappings that were skipped during XML parsing (e.g. unrecognised enum values).
            foreach (var warning in config.ParseWarnings)
                _ed.WriteMessage($"\n  Config warning: {warning}");

            var result = new SyncResult { MappingsApplied = config.Mappings.Count };
            var accUpdates = new List<(string id, string val)>(); // ACC attribute updates to batch at the end.
            var promptConflicts = new List<SyncConflict>();       // Conflicts deferred to the dialog.

            // Lock the document and open a transaction for all DWG reads and writes.
            // The lock is required by AutoCAD before any database modification from a command context.
            progress?.Report($"Applying {config.Mappings.Count} mapping(s) to the drawing…");
            _doc.LockDocument();
            using var tr = _doc.Database.TransactionManager.StartTransaction();
            try
            {
                var db = _doc.Database;

                foreach (var map in config.Mappings)
                {
                    try
                    {
                        // Determine effective read/write flags by combining the command-level direction
                        // with the mapping-level direction. A mapping set to Read-only will never write
                        // even when the command is bidirectional.
                        bool shouldRead = direction != SyncDirection.Write && map.Direction != SyncDirection.Write;
                        bool shouldWrite = direction != SyncDirection.Read && map.Direction != SyncDirection.Read;

                        string accVal = accValues.GetValueOrDefault(map.AccAttributeName);
                        string dwgVal = ReadDwgValue(tr, db, map);

                        // Defer bidirectional conflicts with Prompt strategy — they require user input
                        // via the dialog before the transaction can commit.
                        // Normalise null to "" so a missing ACC/DWG value is treated the same as blank.
                        if (shouldRead && shouldWrite
                            && (accVal ?? "") != (dwgVal ?? "")
                            && map.ConflictStrategy == ConflictStrategy.Prompt)
                        {
                            promptConflicts.Add(new SyncConflict
                            {
                                Mapping = map,
                                AccValue = accVal,
                                DwgValue = dwgVal,
                                Resolution = ConflictStrategy.AccWins // Default selection shown in the dialog.
                            });
                            continue;
                        }

                        ApplyResolution(tr, db, map, accVal, dwgVal,
                            map.ConflictStrategy, shouldRead, shouldWrite, accUpdates, nameToId);
                    }
                    catch (Exception ex)
                    {
                        result.Errors++;
                        _ed.WriteMessage($"\n  Error on mapping '{map.AccAttributeName}': {ex.Message}");
                    }
                }

                // Show the conflict resolution dialog if any deferred conflicts exist.
                // This must run on AutoCAD's STA thread (guaranteed because we do not use
                // ConfigureAwait(false) in RunAsync) — WPF and Application.ShowModalWindow both
                // require STA.
                if (promptConflicts.Any())
                {
                    var dlg = new ConflictResolutionDialog(promptConflicts);
                    bool accepted = Application.ShowModalWindow(dlg) == true;

                    if (accepted)
                    {
                        foreach (var conflict in promptConflicts)
                            ApplyResolution(tr, db, conflict.Mapping,
                                conflict.AccValue, conflict.DwgValue,
                                conflict.Resolution, true, true, accUpdates, nameToId);

                        result.ConflictsResolved = promptConflicts.Count;
                    }
                    else
                    {
                        result.ConflictsCancelled = promptConflicts.Count;
                    }
                }

                tr.Commit();
            }
            catch
            {
                tr.Abort(); // Abort rolls back all DWG changes on any unhandled exception.
                throw;
            }

            // Write ACC attribute updates as a single batched POST after the DWG transaction commits.
            // Batching avoids multiple round-trips and ensures all ACC updates are atomic.
            if (accUpdates.Any())
            {
                progress?.Report($"Writing {accUpdates.Count} change(s) to Autodesk Docs…");
                await _acc.PatchCustomAttributesAsync(projectId, versionUrn, accUpdates, token);
            }

            return result;
        }

        // ── Config loading ──────────────────────────────────────────────────────────

        /// <summary>
        /// Locates and parses the <c>.accsync.xml</c> config file for the open drawing by
        /// walking up the folder tree from the drawing's directory.
        /// </summary>
        /// <remarks>
        /// Search order — the nearest config to the drawing takes priority:
        /// <list type="number">
        ///   <item><description>
        ///     <c>{DrawingName}.accsync.xml</c> in the drawing's own folder (per-drawing override,
        ///     checked only in the drawing's directory — not in parent folders).
        ///   </description></item>
        ///   <item><description>
        ///     <c>accsync.xml</c> in the drawing's folder, then each parent folder up to the drive
        ///     root. A config placed in a parent folder acts as a shared default for all drawings
        ///     beneath it; one placed closer to the drawing overrides it.
        ///   </description></item>
        /// </list>
        /// </remarks>
        /// <returns>The parsed <see cref="SyncConfig"/> object.</returns>
        /// <exception cref="FileNotFoundException">
        /// No config file was found anywhere in the folder tree.
        /// </exception>
        public async Task<SyncConfig> LoadConfigAsync()
        {
            string dwgPath = _doc.Database.Filename;
            string drawingName = Path.GetFileNameWithoutExtension(dwgPath);
            string dwgDir = Path.GetDirectoryName(dwgPath) ?? string.Empty;

            var searched = new List<string>();
            bool isDrawingDir = true;
            string dir = dwgDir;

            while (!string.IsNullOrEmpty(dir))
            {
                // Per-drawing config is only meaningful alongside the drawing itself.
                if (isDrawingDir)
                {
                    string perDrawing = Path.Combine(dir, drawingName + ".accsync.xml");
                    searched.Add(perDrawing);
                    if (File.Exists(perDrawing))
                        return SyncConfigParser.Parse(await File.ReadAllTextAsync(perDrawing));
                }

                string shared = Path.Combine(dir, "accsync.xml");
                searched.Add(shared);
                if (File.Exists(shared))
                    return SyncConfigParser.Parse(await File.ReadAllTextAsync(shared));

                // Move up one level; stop if we have already reached the root.
                string parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
                isDrawingDir = false;
            }

            var msg = new StringBuilder();
            msg.AppendLine("No .accsync.xml config file found for this drawing.");
            msg.AppendLine("Searched locations (nearest first):");
            foreach (string p in searched)
                msg.AppendLine($"  {p}");
            msg.Append("Place an accsync.xml in the drawing's folder or any parent folder.");
            throw new FileNotFoundException(msg.ToString());
        }

        // ── Private helpers ─────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the current DWG value for the given mapping, dispatching to either
        /// <see cref="DwgBlockService"/> or <see cref="DwgPropertySetService"/> depending
        /// on the mapping's <see cref="SyncMapping.Target"/>.
        /// </summary>
        /// <param name="tr">The active AutoCAD transaction.</param>
        /// <param name="db">The active AutoCAD database.</param>
        /// <param name="map">The mapping that describes what to read.</param>
        /// <returns>
        /// The attribute value as a string, or <c>null</c> if the block or property set
        /// is not present in the drawing.
        /// </returns>
        private string ReadDwgValue(Transaction tr, Database db, SyncMapping map)
        {
            if (map.Target == MappingTarget.BlockAttribute)
            {
                // FirstOrDefault returns the default tuple (null, null) if no matching block is found.
                return DwgBlockService
                    .ReadBlockAttributeValues(tr, db, map.BlockName, map.BlockAttributeTag)
                    .FirstOrDefault().value;
            }

            // For property sets, iterate matching entities and return the first non-null value found.
            foreach (DBObject entity in DwgPropertySetService.GetEntitiesForMapping(tr, db, map))
            {
                string val = DwgPropertySetService.ReadPropertyValue(tr, entity, map.PropertySetName, map.PropertyName);
                if (val != null) return val;
            }
            return null;
        }

        /// <summary>
        /// Writes a value to the DWG for the given mapping, dispatching to either
        /// <see cref="DwgBlockService"/> or <see cref="DwgPropertySetService"/>.
        /// </summary>
        /// <param name="tr">The active AutoCAD transaction.</param>
        /// <param name="db">The active AutoCAD database.</param>
        /// <param name="map">The mapping that describes what to write.</param>
        /// <param name="value">The value to write.</param>
        private void WriteDwgValue(Transaction tr, Database db, SyncMapping map, string value)
        {
            if (map.Target == MappingTarget.BlockAttribute)
            {
                DwgBlockService.WriteBlockAttributeValues(tr, db, map.BlockName, map.BlockAttributeTag, value);
                return;
            }

            foreach (DBObject entity in DwgPropertySetService.GetEntitiesForMapping(tr, db, map))
            {
                // GetEntitiesForMapping opens entities ForRead. Upgrading to ForWrite is required
                // here because AddPropertySet (inside EnsurePropertySetAttached) modifies the
                // entity's extension dictionary, which AutoCAD rejects on a read-only object.
                entity.UpgradeOpen();
                DwgPropertySetService.EnsurePropertySetAttached(tr, entity, db, map.PropertySetName);
                DwgPropertySetService.WritePropertyValue(tr, entity, map.PropertySetName, map.PropertyName, value);
            }
        }

        /// <summary>
        /// Applies the conflict resolution strategy for a single mapping, writing to the DWG
        /// and/or queuing an ACC update according to the effective read/write flags and
        /// <paramref name="strategy"/>.
        /// </summary>
        /// <param name="tr">The active AutoCAD transaction.</param>
        /// <param name="db">The active AutoCAD database.</param>
        /// <param name="map">The mapping being processed.</param>
        /// <param name="accVal">Current value in ACC (may be <c>null</c> if not set).</param>
        /// <param name="dwgVal">Current value in the DWG (may be <c>null</c> if not found).</param>
        /// <param name="strategy">Conflict strategy to apply when both sides differ.</param>
        /// <param name="shouldRead">Whether ACC → DWG writes are permitted for this mapping.</param>
        /// <param name="shouldWrite">Whether DWG → ACC writes are permitted for this mapping.</param>
        /// <param name="accUpdates">List that accumulates pending ACC attribute updates.</param>
        /// <param name="nameToId">Attribute name → definition ID map for queuing ACC updates.</param>
        private void ApplyResolution(
            Transaction tr, Database db, SyncMapping map,
            string accVal, string dwgVal, ConflictStrategy strategy,
            bool shouldRead, bool shouldWrite,
            List<(string id, string val)> accUpdates,
            Dictionary<string, string> nameToId)
        {
            if (!shouldRead && !shouldWrite) return;

            // Read-only mapping: copy ACC value into the DWG.
            // A null/missing ACC value is written as empty string so blank ACC attributes clear the DWG field.
            if (shouldRead && !shouldWrite)
            {
                WriteDwgValue(tr, db, map, accVal ?? string.Empty);
                return;
            }

            // Write-only mapping: push DWG value to ACC.
            // A null/missing DWG value is sent as empty string so blank DWG attributes clear the ACC field.
            if (!shouldRead && shouldWrite)
            {
                QueueAccUpdate(accUpdates, nameToId, map.AccAttributeName, dwgVal ?? string.Empty);
                return;
            }

            // Bidirectional mapping — treat null and "" as equivalent (both mean "blank").
            if ((accVal ?? "") == (dwgVal ?? "")) return;

            // Bidirectional mapping with a conflict: apply the chosen strategy.
            switch (strategy)
            {
                case ConflictStrategy.AccWins:
                    WriteDwgValue(tr, db, map, accVal ?? string.Empty);
                    break;

                case ConflictStrategy.DwgWins:
                    QueueAccUpdate(accUpdates, nameToId, map.AccAttributeName, dwgVal ?? string.Empty);
                    break;

                case ConflictStrategy.Skip:
                    break; // Leave both sides unchanged.
            }
        }

        /// <summary>
        /// Adds an ACC attribute update to the pending updates list, looking up the definition ID
        /// from <paramref name="nameToId"/>. Silently skips if the attribute name is unknown or
        /// the value is null.
        /// </summary>
        /// <param name="accUpdates">The list to append to.</param>
        /// <param name="nameToId">Attribute name → definition ID lookup.</param>
        /// <param name="attributeName">Display name of the attribute to update.</param>
        /// <param name="value">New value to write; a null value is not queued.</param>
        private static void QueueAccUpdate(
            List<(string id, string val)> accUpdates,
            Dictionary<string, string> nameToId,
            string attributeName, string value)
        {
            if (attributeName == null) return;

            // If the attribute name is not in the definitions map, it does not exist in this
            // project's configuration and cannot be written — silently skip rather than throwing.
            if (!nameToId.TryGetValue(attributeName, out var defId)) return;

            accUpdates.Add((defId, value ?? string.Empty));
        }
    }
}
