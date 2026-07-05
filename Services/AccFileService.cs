using Autodesk.DataManagement;
using Autodesk.DataManagement.Model;
using Autodesk.SDKManager;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AccC3DMetadata.Services
{
    /// <summary>
    /// Provides all ACC / BIM 360 API operations used by the sync pipeline:
    /// hub and project discovery via the Data Management SDK, item-path resolution,
    /// custom attribute definition lookup, attribute value read, and attribute value write.
    /// </summary>
    /// <remarks>
    /// Two distinct API surfaces are used:
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>Autodesk.DataManagement SDK</b> — hub, project, folder, and item navigation.
    ///   </description></item>
    ///   <item><description>
    ///     <b>BIM 360 Document Management REST API (<c>bim360/docs/v1</c>)</b> — custom attribute
    ///     definitions, reading attribute values via <c>versions:batch-get</c>, and writing via
    ///     <c>custom-attributes:batch-update</c>.
    ///   </description></item>
    /// </list>
    /// All project IDs passed to the Document Management API must have the <c>"b."</c> prefix
    /// stripped — see <see cref="StripBPrefix"/>.
    /// </remarks>
    internal class AccFileService
    {
        // ── API base URLs ──────────────────────────────────────────────────────────

        /// <summary>BIM 360 / ACC Document Management API base URL.</summary>
        private const string Bim360DocsBase = "https://developer.api.autodesk.com/bim360/docs/v1";

        /// <summary>Autodesk Data Management API base URL — used for item-parent lookup.</summary>
        private const string DataManagementBase = "https://developer.api.autodesk.com/data/v1";

        // ── Fields ─────────────────────────────────────────────────────────────────

        /// <summary>Lazily created SDK client; recreated whenever the token changes.</summary>
        private DataManagementClient _dm;

        /// <summary>Token string that was used to create <see cref="_dm"/>.</summary>
        private string _dmToken;

        /// <summary>
        /// Shared <see cref="HttpClient"/> for all raw REST calls.
        /// A single instance is reused across calls to avoid socket exhaustion.
        /// </summary>
        private readonly HttpClient _http = new();

        /// <summary>
        /// Session-scoped cache mapping a local DWG file path to its resolved ACC identifiers.
        /// Avoids repeating the expensive folder-tree walk within a single AutoCAD session.
        /// Static so the cache survives across multiple <see cref="AccFileService"/> instances
        /// created within the same AutoCAD session.
        /// </summary>
        private static readonly Dictionary<string, (string hub, string project, string item)> _itemCache = new();

        // ── SDK client helper ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns a <see cref="DataManagementClient"/> authenticated with the supplied token,
        /// reusing the existing instance when the token has not changed.
        /// </summary>
        /// <param name="accessToken">A valid 3-legged Bearer access token.</param>
        private DataManagementClient Dm(string accessToken)
        {
            // Recreate the client only when the token changes — the SDK client is not thread-safe
            // across token boundaries, but within a single sync operation the token is constant.
            if (_dm == null || accessToken != _dmToken)
            {
                _dmToken = accessToken;
                _dm = new DataManagementClient(
                    authenticationProvider: new StaticAuthenticationProvider(accessToken));
            }
            return _dm;
        }

        // ── Item discovery ─────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves the ACC hub ID and project ID from the local path of a DWG that was opened
        /// via the Autodesk Desktop Connector, without performing a full item search.
        /// Use this overload when the item ID is already known from the config file.
        /// </summary>
        /// <param name="localPath">
        /// Absolute path to the locally-synced DWG file, e.g.
        /// <c>C:\Users\…\Autodesk Docs\MyHub\MyProject\Drawings\Sheet.dwg</c>.
        /// </param>
        /// <param name="accessToken">A valid 3-legged Bearer access token.</param>
        /// <returns>A tuple of <c>(hubId, projectId)</c> in their raw SDK form.</returns>
        /// <exception cref="InvalidOperationException">
        /// The path does not match the Desktop Connector folder structure, or the hub/project
        /// cannot be found in the authenticated account.
        /// </exception>
        public async Task<(string hubId, string projectId)> ResolveHubAndProjectAsync(
            string localPath, string accessToken)
        {
            var (hubName, projectName, _, _) = ParseDesktopConnectorPath(localPath);
            if (hubName == null)
                throw new InvalidOperationException(
                    $"Drawing path does not appear to be inside an Autodesk Desktop Connector folder.\nPath: {localPath}");

            return await LookupHubAndProjectAsync(hubName, projectName, accessToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Derives the ACC hub ID, project ID, and item ID from a DWG opened via the
        /// Autodesk Desktop Connector by navigating the ACC folder tree.
        /// </summary>
        /// <param name="localPath">
        /// Absolute path to the locally-synced DWG file. The Desktop Connector embeds the hub
        /// and project names as path segments:
        /// <c>%USERPROFILE%\Autodesk Docs\{hub}\{project}\{…}\{file}.dwg</c>.
        /// </param>
        /// <param name="accessToken">A valid 3-legged Bearer access token.</param>
        /// <returns>A tuple of <c>(hubId, projectId, itemId)</c>.</returns>
        /// <exception cref="InvalidOperationException">
        /// The path does not match the Desktop Connector structure, the hub or project is not
        /// found, or the file cannot be located in any top-level folder of the project.
        /// </exception>
        public async Task<(string hubId, string projectId, string itemId)> ResolveItemFromDrawingPathAsync(
            string localPath, string accessToken)
        {
            // Return early if we have already resolved this path in the current session.
            if (_itemCache.TryGetValue(localPath, out var cached))
                return cached;

            var (hubName, projectName, folderPath, fileName) = ParseDesktopConnectorPath(localPath);
            if (hubName == null)
                throw new InvalidOperationException(
                    $"Drawing path does not appear to be inside an Autodesk Desktop Connector folder.\n" +
                    $"Path: {localPath}\n" +
                    "Add an explicit <DrawingItem itemId=\"...\"> to your .accsync.xml config to override.");

            var (hubId, projectId) = await LookupHubAndProjectAsync(hubName, projectName, accessToken)
                .ConfigureAwait(false);

            var topFolders = await Dm(accessToken).GetProjectTopFoldersAsync(hubId, projectId).ConfigureAwait(false);
            var topFolderList = topFolders?.Data?.ToList() ?? new List<TopFolderData>();

            string itemId = null;

            // Primary strategy: navigate folder-by-folder using the path extracted from the DC local path.
            // This is significantly faster than a full recursive search when the ACC folder structure
            // mirrors the Desktop Connector sync path.
            if (folderPath.Length > 0)
            {
                foreach (TopFolderData folder in topFolderList)
                {
                    itemId = await TryNavigateAndFindAsync(
                        projectId, folder.Id, folderPath, 0, fileName, accessToken)
                        .ConfigureAwait(false);
                    if (itemId != null) break;
                }
            }

            // Fallback: collect every name match across the entire project, then pick the
            // candidate whose folder path most closely matches the path derived from the DC
            // local path. This prevents the wrong file being selected when multiple files
            // share the same name in different folders.
            if (itemId == null)
            {
                var allMatches = new List<(string itemId, string[] path)>();
                foreach (TopFolderData folder in topFolderList)
                {
                    var found = await SearchFolderForAllMatchesAsync(
                        projectId, folder.Id, fileName, accessToken, Array.Empty<string>())
                        .ConfigureAwait(false);
                    allMatches.AddRange(found);
                }

                if (allMatches.Count > 0)
                {
                    itemId = allMatches
                        .OrderByDescending(m => CountMatchingTrailingSegments(m.path, folderPath))
                        .First().itemId;
                }
            }

            if (itemId == null)
                throw new InvalidOperationException(
                    $"Could not find '{fileName}' in ACC project '{projectName}'. " +
                    "Add an explicit <DrawingItem itemId=\"...\"> to your .accsync.xml config.");

            var result = (hubId, projectId, itemId);
            _itemCache[localPath] = result; // Cache so repeated sync commands within a session are fast.
            return result;
        }

        /// <summary>
        /// Shared hub-and-project resolution used by both public discovery methods.
        /// Looks up the hub and project by display name using the Data Management SDK and
        /// returns their raw IDs (including any <c>b.</c> prefix).
        /// </summary>
        /// <param name="hubName">Display name of the ACC hub (typically the Autodesk account/team name).</param>
        /// <param name="projectName">Display name of the ACC project within that hub.</param>
        /// <param name="accessToken">A valid 3-legged Bearer access token.</param>
        /// <returns>A tuple of <c>(hubId, projectId)</c>.</returns>
        /// <exception cref="InvalidOperationException">
        /// The hub or project cannot be found; the exception message lists the available names
        /// to assist with diagnosis.
        /// </exception>
        private async Task<(string hubId, string projectId)> LookupHubAndProjectAsync(
            string hubName, string projectName, string accessToken)
        {
            var hubs = await Dm(accessToken).GetHubsAsync().ConfigureAwait(false);
            var hub = FindByName(hubs?.Data, h => h.Attributes?.Name, hubName);
            if (hub == null)
            {
                var available = string.Join(", ", hubs?.Data?.Select(h => h.Attributes?.Name) ?? Enumerable.Empty<string>());
                throw new InvalidOperationException(
                    $"ACC hub '{hubName}' not found. Available hubs: [{available}]");
            }

            var projects = await Dm(accessToken).GetHubProjectsAsync(hub.Id).ConfigureAwait(false);
            var project = FindByName(projects?.Data, p => p.Attributes?.Name, projectName);
            if (project == null)
            {
                var available = string.Join(", ", projects?.Data?.Select(p => p.Attributes?.Name) ?? Enumerable.Empty<string>());
                throw new InvalidOperationException(
                    $"ACC project '{projectName}' not found in hub '{hub.Attributes?.Name}'. Available projects: [{available}]");
            }

            return (hub.Id, project.Id);
        }

        /// <summary>
        /// Walks the ACC folder tree step-by-step using the display-name segments in
        /// <paramref name="folderPath"/>, then searches the terminal folder for
        /// <paramref name="fileName"/>. Returns the item ID on success, or <c>null</c> if
        /// any segment of the path is not found.
        /// </summary>
        /// <param name="projectId">ACC project ID (may include the <c>b.</c> prefix).</param>
        /// <param name="folderId">ID of the folder to examine at this recursion level.</param>
        /// <param name="folderPath">Ordered array of intermediate folder display names.</param>
        /// <param name="depth">Current index into <paramref name="folderPath"/>.</param>
        /// <param name="fileName">Display name of the target file.</param>
        /// <param name="accessToken">A valid 3-legged Bearer access token.</param>
        /// <returns>The item ID string, or <c>null</c> if the file is not found.</returns>
        private async Task<string> TryNavigateAndFindAsync(
            string projectId, string folderId, string[] folderPath, int depth,
            string fileName, string accessToken)
        {
            var contents = await Dm(accessToken).GetFolderContentsAsync(projectId, folderId).ConfigureAwait(false);
            if (contents?.Data == null) return null;

            if (depth >= folderPath.Length)
            {
                // We have reached the target folder — look for the file by display name.
                foreach (dynamic entry in contents.Data)
                {
                    try
                    {
                        // FolderContents.Data is a polymorphic collection of items and sub-folders;
                        // we use dynamic and check Type to skip sub-folders at this level.
                        string type = entry.Type?.ToString();
                        if (string.Equals(type, "folders", StringComparison.OrdinalIgnoreCase)) continue;

                        string displayName = (string)entry.Attributes?.DisplayName;
                        if (string.Equals(displayName, fileName, StringComparison.OrdinalIgnoreCase))
                            return (string)entry.Id;
                    }
                    catch { /* Malformed entry from the SDK — skip and continue. */ }
                }
                return null;
            }

            // We are still descending — find the sub-folder whose name matches folderPath[depth].
            string targetName = folderPath[depth];
            foreach (dynamic entry in contents.Data)
            {
                try
                {
                    string type = entry.Type?.ToString();
                    if (!string.Equals(type, "folders", StringComparison.OrdinalIgnoreCase)) continue;

                    string displayName = (string)entry.Attributes?.DisplayName;
                    if (string.Equals(displayName, targetName, StringComparison.OrdinalIgnoreCase))
                        return await TryNavigateAndFindAsync(
                            projectId, (string)entry.Id, folderPath, depth + 1, fileName, accessToken)
                            .ConfigureAwait(false);
                }
                catch { /* Malformed entry — skip. */ }
            }
            return null;
        }

        /// <summary>
        /// Recursively searches all folders under <paramref name="folderId"/> for files whose
        /// display name matches <paramref name="fileName"/> (case-insensitive), collecting every
        /// match together with the folder path from the search root down to the containing folder.
        /// The caller scores the returned matches against the expected DC path and picks the best.
        /// </summary>
        /// <param name="projectId">ACC project ID.</param>
        /// <param name="folderId">Root folder ID for this search pass.</param>
        /// <param name="fileName">Display name of the target file.</param>
        /// <param name="accessToken">A valid 3-legged Bearer access token.</param>
        /// <param name="currentPath">Folder names accumulated from the search root to this folder.</param>
        /// <returns>All matching <c>(itemId, folderPath)</c> pairs found anywhere in the subtree.</returns>
        private async Task<List<(string itemId, string[] path)>> SearchFolderForAllMatchesAsync(
            string projectId, string folderId, string fileName, string accessToken, string[] currentPath)
        {
            var matches = new List<(string, string[])>();
            var contents = await Dm(accessToken).GetFolderContentsAsync(projectId, folderId).ConfigureAwait(false);
            if (contents?.Data == null) return matches;

            var subfolders = new List<(string id, string name)>();

            foreach (dynamic entry in contents.Data)
            {
                try
                {
                    string type = entry.Type?.ToString();
                    string displayName = (string)entry.Attributes?.DisplayName;

                    if (string.Equals(type, "folders", StringComparison.OrdinalIgnoreCase))
                        subfolders.Add(((string)entry.Id, displayName));
                    else if (string.Equals(displayName, fileName, StringComparison.OrdinalIgnoreCase))
                        matches.Add(((string)entry.Id, currentPath));
                }
                catch { /* Malformed entry — skip. */ }
            }

            foreach (var (subId, subName) in subfolders)
            {
                var subMatches = await SearchFolderForAllMatchesAsync(
                    projectId, subId, fileName, accessToken,
                    [.. currentPath, subName])
                    .ConfigureAwait(false);
                matches.AddRange(subMatches);
            }

            return matches;
        }

        /// <summary>
        /// Counts how many trailing segments of <paramref name="candidatePath"/> match the
        /// trailing segments of <paramref name="expectedPath"/> (case-insensitive).
        /// Used to score fallback search results against the folder path derived from the DC
        /// local path so the closest match is selected when duplicate filenames exist.
        /// </summary>
        private static int CountMatchingTrailingSegments(string[] candidatePath, string[] expectedPath)
        {
            int count = 0;
            int ci = candidatePath.Length - 1;
            int ei = expectedPath.Length - 1;
            while (ci >= 0 && ei >= 0 &&
                   string.Equals(candidatePath[ci], expectedPath[ei], StringComparison.OrdinalIgnoreCase))
            {
                count++;
                ci--;
                ei--;
            }
            return count;
        }

        // ── ACC custom attribute definitions ───────────────────────────────────────

        /// <summary>
        /// Fetches all custom attribute definitions for the folder that contains the target DWG
        /// and returns two bidirectional lookup maps.
        /// </summary>
        /// <remarks>
        /// The Document Management API scopes definitions to a folder, not to a project, so the
        /// parent folder ID of the DWG item must be supplied. The folder ID is obtained via
        /// <see cref="GetItemParentFolderIdAsync"/>.
        /// <para>
        /// The API may return definition IDs as either JSON numbers or JSON strings depending on
        /// the account configuration. <see cref="JsonScalarToString"/> normalises both to C# strings.
        /// </para>
        /// </remarks>
        /// <param name="projectId">
        /// ACC project ID — the <c>b.</c> prefix is stripped internally before use in the URL.
        /// </param>
        /// <param name="folderId">
        /// URN of the folder that contains the DWG item. Must be URL-encoded in the request;
        /// this method handles encoding internally.
        /// </param>
        /// <param name="accessToken">A valid 3-legged Bearer access token.</param>
        /// <returns>
        /// A tuple of two dictionaries (both case-insensitive):
        /// <list type="bullet">
        ///   <item><description><c>nameToId</c> — maps attribute display name → definition ID.</description></item>
        ///   <item><description><c>idToName</c> — maps definition ID → attribute display name.</description></item>
        /// </list>
        /// </returns>
        public async Task<(Dictionary<string, string> nameToId, Dictionary<string, string> idToName)>
            GetAttributeDefinitionMapsAsync(string projectId, string folderId, string accessToken)
        {
            string cleanProjectId = StripBPrefix(projectId); // Document Management API rejects the "b." prefix.
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{Bim360DocsBase}/projects/{cleanProjectId}/folders/{Uri.EscapeDataString(folderId)}/custom-attribute-definitions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _http.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            var root = doc.RootElement;

            var nameToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var idToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // The response may be a bare array or wrapped under "data" or "results" depending on
            // the API version and account tier — probe all three shapes defensively.
            var defsArray = root.ValueKind == JsonValueKind.Array ? root
                : root.TryGetProperty("data", out var d) ? d
                : root.TryGetProperty("results", out var r) ? r
                : default;

            if (defsArray.ValueKind != JsonValueKind.Array)
                return (nameToId, idToName); // No definitions found; return empty maps rather than throwing.

            foreach (var def in defsArray.EnumerateArray())
            {
                string id = def.TryGetProperty("id", out var idProp) ? JsonScalarToString(idProp) : null;
                string name = def.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                if (id != null && name != null)
                {
                    nameToId[name] = id;
                    idToName[id] = name;
                }
            }

            return (nameToId, idToName);
        }

        /// <summary>
        /// Fetches the current custom attribute values for a single ACC item using the
        /// <c>versions:batch-get</c> endpoint and returns them keyed by attribute name.
        /// Also captures the resolved version URN required for subsequent write operations.
        /// </summary>
        /// <remarks>
        /// The batch-get endpoint accepts item lineage URNs and returns the tip version's data,
        /// including its version-specific URN which is different from the lineage URN and is
        /// required by the <c>custom-attributes:batch-update</c> write endpoint.
        /// <para>
        /// Attribute names are resolved from the response's inline <c>name</c> field where
        /// available; when absent, the numeric <c>id</c> field is resolved against
        /// <paramref name="idToName"/>.
        /// </para>
        /// </remarks>
        /// <param name="projectId">
        /// ACC project ID — the <c>b.</c> prefix is stripped internally.
        /// </param>
        /// <param name="itemId">
        /// The item lineage URN (e.g. <c>urn:adsk.wipprod:dm.lineage:…</c>), as returned by
        /// the folder contents endpoint.
        /// </param>
        /// <param name="idToName">
        /// Definition-ID-to-name map from <see cref="GetAttributeDefinitionMapsAsync"/>, used
        /// as a fallback when the response omits the inline name.
        /// </param>
        /// <param name="accessToken">A valid 3-legged Bearer access token.</param>
        /// <returns>
        /// A tuple of:
        /// <list type="bullet">
        ///   <item><description>
        ///     <c>values</c> — case-insensitive dictionary of attribute name → current value
        ///     (value is <c>null</c> for attributes that exist but have no value set).
        ///   </description></item>
        ///   <item><description>
        ///     <c>versionUrn</c> — the tip version URN needed by
        ///     <see cref="PatchCustomAttributesAsync"/>, or <c>null</c> if the response did not
        ///     include it.
        ///   </description></item>
        /// </list>
        /// </returns>
        public async Task<(Dictionary<string, string> values, string versionUrn)> GetCustomAttributesAsync(
            string projectId, string itemId, Dictionary<string, string> idToName, string accessToken)
        {
            string cleanProjectId = StripBPrefix(projectId);

            // The batch-get endpoint accepts an array of URNs so multiple items can be fetched
            // in one round-trip; we always send exactly one because the orchestrator works item-by-item.
            string body = JsonSerializer.Serialize(new { urns = new[] { itemId } });
            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{Bim360DocsBase}/projects/{cleanProjectId}/versions:batch-get")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _http.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            var root = doc.RootElement;

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string versionUrn = null;

            // Response shape: { "results": [ { "urn": "…", "customAttributes": [ { "id": 123, "name": "…", "value": "…" } ] } ] }
            if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                return (values, versionUrn);

            // We sent one URN so there is at most one result — process the first element only.
            foreach (var version in results.EnumerateArray())
            {
                // Capture the version-specific URN — this differs from the item lineage URN we sent
                // and is required as the path parameter for the batch-update write call.
                if (version.TryGetProperty("urn", out var urnProp))
                    versionUrn = urnProp.GetString();
                else if (version.TryGetProperty("id", out var idProp))
                    versionUrn = idProp.GetString(); // Some API versions use "id" instead of "urn".

                if (!version.TryGetProperty("customAttributes", out var customAttrs)) break;

                foreach (var attr in customAttrs.EnumerateArray())
                {
                    // Prefer the inline "name" field; fall back to resolving the numeric "id" via the map.
                    string name = attr.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                    if (name == null)
                    {
                        string defId = attr.TryGetProperty("id", out var defIdProp) ? JsonScalarToString(defIdProp) : null;
                        if (defId != null) idToName.TryGetValue(defId, out name);
                    }

                    // A JSON null value means the attribute exists but has no value assigned.
                    string val = attr.TryGetProperty("value", out var valProp) && valProp.ValueKind != JsonValueKind.Null
                        ? valProp.GetString() : null;

                    if (name != null)
                        values[name] = val;
                }
                break; // Only one result expected.
            }

            return (values, versionUrn);
        }

        /// <summary>
        /// Returns the parent folder ID of a given ACC item by inspecting the item's
        /// <c>relationships.parent</c> link in the Data Management API response.
        /// This folder ID is required by <see cref="GetAttributeDefinitionMapsAsync"/> because
        /// the Document Management API scopes attribute definitions to folders, not projects.
        /// </summary>
        /// <param name="projectId">ACC project ID (may include the <c>b.</c> prefix).</param>
        /// <param name="itemId">Item lineage URN — URL-encoded before use in the request path.</param>
        /// <param name="accessToken">A valid 3-legged Bearer access token.</param>
        /// <returns>
        /// The folder ID string, or <c>null</c> if the parent relationship is absent from
        /// the response (which should not occur for a valid item).
        /// </returns>
        public async Task<string> GetItemParentFolderIdAsync(string projectId, string itemId, string accessToken)
        {
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{DataManagementBase}/projects/{projectId}/items/{Uri.EscapeDataString(itemId)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _http.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            var root = doc.RootElement;

            // Navigate: data → relationships → parent → data → id
            if (!root.TryGetProperty("data", out var data)) return null;
            if (!data.TryGetProperty("relationships", out var rels)) return null;
            if (!rels.TryGetProperty("parent", out var parent)) return null;
            if (!parent.TryGetProperty("data", out var parentData)) return null;
            return parentData.TryGetProperty("id", out var id) ? id.GetString() : null;
        }

        /// <summary>
        /// Writes updated custom attribute values to an ACC item version via a single
        /// <c>custom-attributes:batch-update</c> POST call.
        /// </summary>
        /// <remarks>
        /// The API endpoint is:
        /// <c>POST /bim360/docs/v1/projects/{projectId}/versions/{versionUrn}/custom-attributes:batch-update</c>
        /// <para>
        /// The request body is a flat JSON array: <c>[ { "id": defId, "value": "…" }, … ]</c>.
        /// Attribute definition IDs returned by the definitions endpoint are numeric integers;
        /// the API expects them as JSON numbers, not strings, so IDs that parse as <c>long</c>
        /// are serialised as numbers rather than as quoted strings.
        /// </para>
        /// </remarks>
        /// <param name="projectId">
        /// ACC project ID — the <c>b.</c> prefix is stripped internally before use in the URL.
        /// </param>
        /// <param name="versionUrn">
        /// The version-specific URN captured from the <see cref="GetCustomAttributesAsync"/>
        /// response (distinct from the item lineage URN).
        /// </param>
        /// <param name="updates">
        /// Collection of <c>(definitionId, value)</c> pairs to write. The definition IDs come
        /// from the <c>nameToId</c> map built by <see cref="GetAttributeDefinitionMapsAsync"/>.
        /// </param>
        /// <param name="accessToken">A valid 3-legged Bearer access token.</param>
        public async Task PatchCustomAttributesAsync(
            string projectId, string versionUrn,
            IEnumerable<(string definitionId, string value)> updates,
            string accessToken)
        {
            string cleanProjectId = StripBPrefix(projectId);

            // Build the flat array body. Definition IDs from the API are often integers serialised
            // as strings in our nameToId map; the batch-update endpoint requires them as JSON numbers,
            // so we parse back to long where possible.
            var entries = new List<object>();
            foreach (var (defId, value) in updates)
            {
                if (long.TryParse(defId, out long numId))
                    entries.Add(new { id = numId, value }); // Numeric ID — serialise as JSON number.
                else
                    entries.Add(new { id = defId, value }); // String/GUID ID — serialise as JSON string.
            }

            string json = JsonSerializer.Serialize(entries);
            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{Bim360DocsBase}/projects/{cleanProjectId}/versions/{Uri.EscapeDataString(versionUrn)}/custom-attributes:batch-update")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _http.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }

        // ── Private helpers ────────────────────────────────────────────────────────

        /// <summary>
        /// Converts a <see cref="JsonElement"/> scalar to a C# string regardless of whether
        /// the underlying JSON value is a quoted string or an unquoted number.
        /// </summary>
        /// <remarks>
        /// The ACC Document Management API returns attribute definition IDs as JSON numbers in
        /// some account configurations and as JSON strings in others. Using this helper ensures
        /// the <c>nameToId</c> / <c>idToName</c> maps handle both forms consistently.
        /// </remarks>
        private static string JsonScalarToString(JsonElement el) => el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetRawText(), // GetRawText() preserves the exact numeric representation.
            _ => null
        };

        /// <summary>
        /// Parses a Desktop Connector local file path into its ACC-meaningful components.
        /// </summary>
        /// <remarks>
        /// The Desktop Connector mirrors the ACC folder hierarchy under a fixed root:
        /// <code>
        /// C:\Users\{user}\Autodesk Docs\{hub}\{project}\{folder…}\{file}.dwg
        ///                               [5]    [6]        [7..^1]   [^1]
        /// </code>
        /// Segment indices are zero-based after splitting on directory separators. The hub is
        /// always at index 5 because the root is always five segments deep
        /// (<c>C:\Users\{user}\Autodesk Docs</c>).
        /// </remarks>
        /// <param name="localPath">Absolute local file path to parse.</param>
        /// <returns>
        /// A tuple of <c>(hubName, projectName, folderPath, fileName)</c>. All four values are
        /// <c>null</c> if <paramref name="localPath"/> does not match the expected structure.
        /// </returns>
        private static (string hubName, string projectName, string[] folderPath, string fileName)
            ParseDesktopConnectorPath(string localPath)
        {
            if (string.IsNullOrEmpty(localPath))
                return (null, null, null, null);

            var segments = localPath.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            // Minimum required: drive + Users + username + "Autodesk Docs" + hub + project + file = 7 segments.
            if (segments.Length < 7)
                return (null, null, null, null);

            string hubName = segments[5];      // Fixed index — always the 6th path segment.
            string projectName = segments[6];  // Fixed index — always the 7th path segment.
            string fileName = segments[^1];    // Last segment is always the file name.

            // Segments between the project name and the file name represent ACC sub-folder names.
            // If there are fewer than 9 segments there are no intermediate folders.
            string[] folderPath = segments.Length >= 9 ? segments[7..^1] : Array.Empty<string>();

            return (hubName, projectName, folderPath, fileName);
        }

        /// <summary>
        /// Strips the <c>"b."</c> prefix from an ACC project ID if present.
        /// </summary>
        /// <remarks>
        /// The Data Management SDK and the Data Management REST API return project IDs with a
        /// <c>"b."</c> prefix (e.g. <c>b.7f3a…</c>). The BIM 360 Document Management API
        /// rejects this prefix and requires the bare GUID — always call this method before
        /// using a project ID in a Document Management URL.
        /// </remarks>
        private static string StripBPrefix(string id) =>
            id?.StartsWith("b.", StringComparison.OrdinalIgnoreCase) == true ? id[2..] : id;

        /// <summary>
        /// Finds the first item in <paramref name="items"/> whose name, as returned by
        /// <paramref name="nameSelector"/>, matches <paramref name="target"/>.
        /// </summary>
        /// <remarks>
        /// Matching is performed in two passes:
        /// <list type="number">
        ///   <item><description>Exact case-insensitive match.</description></item>
        ///   <item><description>
        ///     Normalised match — both strings are trimmed and internal whitespace is collapsed
        ///     to a single space. This handles ACC project names that contain non-breaking spaces
        ///     or extra spaces as copied from the web UI.
        ///   </description></item>
        /// </list>
        /// </remarks>
        /// <typeparam name="T">The item type.</typeparam>
        /// <param name="items">The collection to search.</param>
        /// <param name="nameSelector">Function that extracts the display name from an item.</param>
        /// <param name="target">The name to search for.</param>
        /// <returns>The first matching item, or <c>null</c> if not found.</returns>
        private static T FindByName<T>(IEnumerable<T> items, Func<T, string> nameSelector, string target)
            where T : class
        {
            if (items == null || target == null) return null;
            var list = items.ToList();

            // Pass 1 — exact case-insensitive match.
            var hit = list.FirstOrDefault(x =>
                string.Equals(nameSelector(x), target, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;

            // Pass 2 — normalised match: trim outer whitespace and collapse internal runs.
            static string Normalise(string s) =>
                s == null ? null : System.Text.RegularExpressions.Regex.Replace(s.Trim(), @"\s+", " ");

            string normTarget = Normalise(target);
            return list.FirstOrDefault(x =>
                string.Equals(Normalise(nameSelector(x)), normTarget, StringComparison.OrdinalIgnoreCase));
        }
    }
}
