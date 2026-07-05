# Forma Sync — Autodesk Civil 3D Plugin

Synchronises custom file attribute values between **Autodesk Forma** and an open DWG file. Values can be pulled from Forma into block attributes or Civil 3D property sets, pushed from the drawing back to Forma, or kept in sync bidirectionally with interactive conflict resolution.

---

## Contents

1. [Prerequisites](#1-prerequisites)
2. [Installation](#2-installation)
3. [APS Application Setup](#3-aps-application-setup)
4. [First-time Authentication](#4-first-time-authentication)
5. [Quick Start](#5-quick-start)
6. [Config File Reference](#6-config-file-reference)
   - [File Location and Discovery](#file-location-and-discovery)
   - [Root Element](#root-element)
   - [DrawingItem Element](#drawingitem-element)
   - [Mapping Attributes — Common](#mapping-attributes--common)
   - [Mapping Attributes — BlockAttribute Target](#mapping-attributes--blockattribute-target)
   - [Mapping Attributes — PropertySet Target](#mapping-attributes--propertyset-target)
   - [Direction Values](#direction-values)
   - [ConflictStrategy Values](#conflictstrategy-values)
7. [Sample Config Files](#7-sample-config-files)
8. [Commands and Ribbon](#8-commands-and-ribbon)
9. [Conflict Resolution Dialog](#9-conflict-resolution-dialog)
10. [Troubleshooting](#10-troubleshooting)
11. [Building from Source](#11-building-from-source)

---

## 1. Prerequisites

| Requirement | Notes |
|---|---|
| AutoCAD Civil 3D 2025 | Earlier versions are not supported |
| Autodesk Desktop Connector | Required for automatic hub/project/item resolution from the local file path |
| Autodesk Forma account | Must have read access to the project; write access required for push operations |
| .NET 8 runtime or above | Bundled with Civil 3D 2025 |

---

## 2. Installation

The plugin ships as an AutoCAD Application Bundle (`AccC3DMetadata.bundle`). AutoCAD discovers bundles automatically on startup — no `NETLOAD` command or startup suite entry is required.

1. Copy the `AccC3DMetadata.bundle` folder into your AutoCAD ApplicationPlugins directory:
   ```
   %APPDATA%\Autodesk\ApplicationPlugins\
   ```
   The full path is typically:
   ```
   C:\Users\<you>\AppData\Roaming\Autodesk\ApplicationPlugins\AccC3DMetadata.bundle
   ```
2. Start (or restart) AutoCAD Civil 3D 2025.
3. The **Forma Sync** ribbon tab loads automatically. No further steps are needed.

> **Tip:** You can open `%APPDATA%\Autodesk\ApplicationPlugins\` directly by pasting that path into Windows Explorer's address bar.

---

## 3. APS Application Setup

The plugin authenticates users via Autodesk Platform Services (APS) OAuth. **Each organisation must create its own APS application** — do not share the developer's Client ID with end users. Using someone else's application consumes their API quota and means they can revoke your access at any time.

### Why you need your own application

- APS enforces per-application API rate limits. Sharing a Client ID pools all users into a single quota.
- The application owner can revoke the Client ID at any time, instantly breaking authentication for every user relying on it.
- Your Forma data is accessed under your credentials — the application ID should be under your organisation's control.

### Creating an APS application

1. Sign in to the [APS Developer Portal](https://aps.autodesk.com/) with an Autodesk account (this can be your personal or organisation account — it does not need Forma access itself).
2. Click **Create Application**.
3. Fill in a name and description for your organisation (e.g. *"Acme Corp — ACC Sync"*).
4. Under **Application type**, select **Desktop, Mobile, CLI** — this is the only type that supports the PKCE flow used by the plugin. Server-side types require a client secret, which is unsuitable for a locally installed tool.
5. Under **Callback URL**, add exactly:
   ```
   http://localhost:8080/
   ```
   The trailing slash is required and must match exactly.
6. Under **API access**, enable the following:
   | API | Why |
   |---|---|
   | Data Management | Hub, project, folder, and item navigation |
   | BIM 360 Document Management | Reading and writing custom file attribute values |
7. Click **Save** (or **Create**). The application is created immediately — no review process.
8. Copy the **Client ID** shown on the application overview page.

### Providing the Client ID to the plugin

The easiest way is through the **Settings** ribbon button:

1. Load the plugin (`NETLOAD`).
2. On the **Forma Sync** ribbon tab, click **Settings** (or type `AccSyncSettings` at the command line).
3. Paste your Client ID into the field and click **Save**.

The ID is stored in your Windows user profile (`%APPDATA%\AccC3DSync\accsync.clientid`) and persists across plugin updates and machine restarts. It is not shared with other Windows users on the same machine.

#### Manual file alternative (backward-compatible)

If you prefer, you can still create a plain-text file named `accsync.clientid` in the **same directory as the plugin DLL** (the folder that contains `AccC3DMetadata.dll`). The file should contain only the Client ID on a single line:

```
AaBbCcDdEeFfGgHh1234567890
```

The plugin checks the user-profile location first, then the plugin directory as a fallback. If both are present, the user-profile value wins.

> **Important:** `accsync.clientid` (in either location) must **never** be committed to source control. The user-profile file is not inside the repository, but the plugin-directory file is listed in `.gitignore` as a safeguard.

---

## 4. First-time Authentication

The plugin uses **3-legged OAuth (PKCE)** — no password is stored. On the first sync command:

1. A browser window opens showing the Autodesk sign-in page.
2. Sign in with the account that has access to the Forma project.
3. After authorisation the browser shows *"Authentication complete. You may close this window."*
4. The token is cached for the remainder of the AutoCAD session and silently refreshed as it approaches expiry. You will only be prompted again in a new session or after a network disruption.

---

## 5. Quick Start

1. Install the bundle as described in [Section 2](#2-installation) and configure your APS Client ID via the **Settings** button (see [Section 3](#3-aps-application-setup)).
2. Open a DWG that is synced to Forma via Desktop Connector (i.e. its path contains `Autodesk Docs\{hub}\{project}\…`).
3. Place an `accsync.xml` config file in the DWG's folder, or in any parent folder (see [Config File Discovery](#file-location-and-discovery)).
4. Click **Pull from Docs** on the ribbon (or type `AccSyncPull`).
5. A progress dialog shows each step; when complete, Forma attribute values have been written into the drawing's block attributes or property sets.

---

## 6. Config File Reference

### File Location and Discovery

The plugin searches for a config file starting in the drawing's own folder and walking **up** the directory tree until one is found. A config file closer to the drawing takes priority over one higher up. This lets you place a shared default config at a project root and override it with a folder-specific or per-drawing file lower down.

**Search order within each directory visited:**

| Priority | File name | Checked in |
|---|---|---|
| 1 | `{DrawingName}.accsync.xml` | Drawing's own folder only |
| 2 | `accsync.xml` | Drawing's folder, then each parent up to the drive root |

**Example folder layout:**

```
C:\Autodesk Docs\
└── My Hub\
    └── My Project\                 ← accsync.xml here acts as the project-wide default
        ├── accsync.xml
        ├── Civil\
        │   ├── accsync.xml         ← overrides the project-wide config for all Civil drawings
        │   └── Road Design\
        │       └── Sheet01.dwg
        └── Structural\
            └── Frame.accsync.xml   ← per-drawing override for Frame.dwg only
```

If no config file is found anywhere in the tree, the sync command fails with a message listing every path that was checked.

> **Tip:** For most projects, a single `accsync.xml` in the Desktop Connector project root is sufficient. Add folder-level or per-drawing files only when mappings differ between areas of the project.

---

### Root Element

```xml
<AccSync version="1.0"
         hubId="optional-hub-id"
         projectId="optional-project-id">
```

| Attribute | Required | Description |
|---|---|---|
| `version` | Yes | Must be `"1.0"` |
| `hubId` | No | ACC hub ID. Omit to resolve automatically from the Desktop Connector path. |
| `projectId` | No | Forma project ID (with or without the `b.` prefix). Omit to resolve automatically. |

> **Tip:** You almost never need `hubId` or `projectId`. They exist only for drawings that are *not* opened via Desktop Connector (e.g. copied to a local folder manually). For Desktop Connector drawings, leave them out.

---

### DrawingItem Element

```xml
<DrawingItem itemId="urn:adsk.wipprod:dm.lineage:XXXXXXXXXXXXXXXXXX" />
```

| Attribute | Required | Description |
|---|---|---|
| `itemId` | No | The Forma item lineage URN for this specific DWG. Omit entirely to auto-resolve by searching the Forma folder tree for a file whose name matches the open drawing. |

> **Tip:** Omit `<DrawingItem>` for drawings synced via Desktop Connector — the item is found automatically. Add it only if auto-resolution is slow or the drawing name is not unique within the project.

To find an item ID, navigate to the file in the Forma web interface, click **⋮ → Copy Link**, and extract the URN from the URL, or use the **AccSyncLoadConfig** command which shows the resolved item ID in the command line after a successful pull.

---

### Mapping Attributes — Common

Each `<Mapping>` element requires these attributes regardless of target type:

| Attribute | Required | Values | Description |
|---|---|---|---|
| `type` | Yes | `BlockAttribute` \| `PropertySet` | Selects the DWG data store to read from or write to |
| `accAttributeName` | Yes | Any string | Display name of the ACC custom attribute, exactly as it appears in the ACC project settings |
| `accAttributeId` | No | Numeric ID string | Pre-resolved attribute definition ID. Omit to resolve by name at runtime (recommended) |
| `direction` | No | `Read` \| `Write` \| `ReadWrite` | Controls which side can be written. Defaults to `ReadWrite` |
| `conflictStrategy` | No | `AccWins` \| `DwgWins` \| `Prompt` \| `Skip` | How to handle a conflict in bidirectional sync. Defaults to `Prompt`. Ignored for `Read` or `Write` direction mappings |

---

### Mapping Attributes — BlockAttribute Target

Used when `type="BlockAttribute"`. Reads or writes the `TextString` of a named attribute on a named block reference.

| Attribute | Required | Description |
|---|---|---|
| `blockName` | Yes | Name of the AutoCAD block definition (case-insensitive), e.g. `TITLE_BLOCK` |
| `blockAttributeTag` | Yes | Tag name of the attribute within the block (case-insensitive), e.g. `PROJ_NO` |

The mapping applies to **all instances** of the block found in the current space. In practice, title-block attributes exist as a single instance, so this matches exactly one block reference.

---

### Mapping Attributes — PropertySet Target

Used when `type="PropertySet"`. Reads or writes a value in a Civil 3D property set attached to matching entities.

| Attribute | Required | Description |
|---|---|---|
| `entityType` | Yes | AutoCAD entity type to search. Currently supported: `BlockReference` |
| `entityBlockName` | No | When `entityType="BlockReference"`, restricts the search to instances of this block definition. Omit to match all block references |
| `propertySetName` | Yes | Name of the Civil 3D property set definition (case-insensitive) |
| `propertyName` | Yes | Name of the property within the set (case-insensitive) |

> **Note:** If the property set is not yet attached to the entity, `EnsurePropertySetAttached` is called automatically before writing — you do not need to pre-attach it manually.

---

### Direction Values

| Value | Forma → DWG | DWG → Forma | Notes |
|---|---|---|---|
| `Read` | ✅ | ❌ | Pull only. The DWG value is never sent to Forma. |
| `Write` | ❌ | ✅ | Push only. The Forma value is never written into the DWG. |
| `ReadWrite` | ✅ | ✅ | Both directions. Conflicts are handled by `conflictStrategy`. Default. |

The **command-level** direction (Pull / Push / Both) and the **mapping-level** direction are combined: the more restrictive of the two wins. For example, a mapping set to `Read` will never push even when the **Sync Both** command is used.

---

### ConflictStrategy Values

A conflict occurs in a `ReadWrite` mapping when the Forma value and the DWG value differ at the start of a bidirectional sync.

| Value | Behaviour |
|---|---|
| `AccWins` | Overwrites the DWG value with the Forma value. No user prompt. |
| `DwgWins` | Overwrites the Forma value with the DWG value. No user prompt. |
| `Prompt` | Defers the decision and shows the [Conflict Resolution Dialog](#9-conflict-resolution-dialog) before committing. Default. |
| `Skip` | Leaves both values unchanged. |

---

## 7. Sample Config Files

### Minimal — Single Title Block (Desktop Connector drawing)

The simplest possible config: one block attribute mapped bidirectionally with a user prompt on conflict.

```xml
<?xml version="1.0" encoding="utf-8"?>
<AccSync version="1.0">
  <Mappings>
    <Mapping
      type="BlockAttribute"
      accAttributeName="Project Number"
      blockName="TITLE_BLOCK"
      blockAttributeTag="PROJ_NO"
      direction="ReadWrite"
      conflictStrategy="Prompt" />
  </Mappings>
</AccSync>
```

---

### Standard — Multiple Block Attributes with Mixed Strategies

```xml
<?xml version="1.0" encoding="utf-8"?>
<AccSync version="1.0">
  <Mappings>

    <!-- Project number: ACC is the master source — always overwrite the DWG value -->
    <Mapping
      type="BlockAttribute"
      accAttributeName="Project Number"
      blockName="TITLE_BLOCK"
      blockAttributeTag="PROJ_NO"
      direction="ReadWrite"
      conflictStrategy="AccWins" />

    <!-- Drawing title: editable in both places — prompt the user when they differ -->
    <Mapping
      type="BlockAttribute"
      accAttributeName="Drawing Title"
      blockName="TITLE_BLOCK"
      blockAttributeTag="DWG_TITLE"
      direction="ReadWrite"
      conflictStrategy="Prompt" />

    <!-- Revision: managed in the DWG only — push to ACC, never overwrite locally -->
    <Mapping
      type="BlockAttribute"
      accAttributeName="Revision"
      blockName="TITLE_BLOCK"
      blockAttributeTag="REVISION"
      direction="Write"
      conflictStrategy="DwgWins" />

    <!-- Status: pulled from ACC into the drawing, never pushed back -->
    <Mapping
      type="BlockAttribute"
      accAttributeName="Document Status"
      blockName="TITLE_BLOCK"
      blockAttributeTag="STATUS"
      direction="Read"
      conflictStrategy="AccWins" />

  </Mappings>
</AccSync>
```

---

### Civil 3D Property Sets

```xml
<?xml version="1.0" encoding="utf-8"?>
<AccSync version="1.0">
  <Mappings>

    <!-- Read the sheet title from ACC into a property set on the sheet border block -->
    <Mapping
      type="PropertySet"
      accAttributeName="Sheet Title"
      entityType="BlockReference"
      entityBlockName="SHEET_BORDER"
      propertySetName="SheetData"
      propertyName="Title"
      direction="Read"
      conflictStrategy="AccWins" />

    <!-- Push the design phase from a property set on any block reference to ACC -->
    <Mapping
      type="PropertySet"
      accAttributeName="Design Phase"
      entityType="BlockReference"
      propertySetName="ProjectInfo"
      propertyName="Phase"
      direction="Write"
      conflictStrategy="DwgWins" />

  </Mappings>
</AccSync>
```

---

### Drawing Not on Desktop Connector (Explicit IDs)

Use this pattern when the drawing is not synced via Desktop Connector — for example, a drawing that was manually copied to a local folder.

```xml
<?xml version="1.0" encoding="utf-8"?>
<AccSync version="1.0"
         hubId="b.xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
         projectId="b.yyyyyyyy-yyyy-yyyy-yyyy-yyyyyyyyyyyy">

  <DrawingItem itemId="urn:adsk.wipprod:dm.lineage:ZZZZZZZZZZZZZZZZZZZZ" />

  <Mappings>
    <Mapping
      type="BlockAttribute"
      accAttributeName="Project Number"
      blockName="TITLE_BLOCK"
      blockAttributeTag="PROJ_NO"
      direction="ReadWrite"
      conflictStrategy="Prompt" />
  </Mappings>
</AccSync>
```

---

## 8. Commands and Ribbon

All commands are available from the **Forma Sync** ribbon tab or by typing directly at the AutoCAD command line.

### Autodesk Forma panel

| Ribbon Button | Command | Description |
|---|---|---|
| Pull from Forma | `AccSyncPull` | Reads all Forma attribute values and writes them into the DWG. Mapping direction `Write` is ignored. |
| Push to Forma | `AccSyncPush` | Reads all DWG attribute values and writes them to Forma. Mapping direction `Read` is ignored. |
| Sync Both | `AccSyncBoth` | Bidirectional sync. Prompts for any conflicts where `conflictStrategy="Prompt"`. |

### Configuration panel

| Ribbon Button | Command | Description |
|---|---|---|
| Load Config | `AccSyncLoadConfig` | Locates and parses the config file, printing the resolved hub, project, item ID, and all mappings to the command line. Useful for verifying setup without performing a sync. |
| Settings | `AccSyncSettings` | Opens the Settings dialog to enter or update the APS Client ID. |

A progress dialog is shown during Pull, Push, and Sync Both operations, reporting each step (authentication, attribute fetching, applying changes). The command line reports a summary on completion, for example:

```
Sync complete — 4 mapping(s), 1 conflict(s) resolved, 0 conflict(s) cancelled, 0 error(s).
```

---

## 9. Conflict Resolution Dialog

The conflict dialog appears during **Sync Both** when one or more mappings have `conflictStrategy="Prompt"` and the Forma value differs from the DWG value.

Each row shows:

| Column | Description |
|---|---|
| **Attribute** | The Forma attribute display name from the mapping |
| **Autodesk Forma value** | The value currently stored in Forma |
| **Drawing value** | The value currently in the drawing |
| **Resolution** | Drop-down: choose `AccWins`, `DwgWins`, or `Skip` for each conflict individually |

- Click **Apply** to commit your selections and continue the sync.
- Click **Cancel** to abandon all deferred conflicts (no DWG or Forma changes are made for those mappings; non-conflicting mappings are still applied).

The footer of the dialog shows a quick reminder of what each resolution option means.

---

## 10. Troubleshooting

**`Drawing path does not appear to be inside an Autodesk Desktop Connector folder`**  
The DWG is not in a Desktop Connector sync folder. Either open the drawing from your local `Autodesk Docs` sync path, or add explicit `hubId`, `projectId`, and `<DrawingItem itemId="…">` to the config file.

**`ACC hub 'X' not found. Available hubs: [Y, Z]`**  
The hub name parsed from the local path does not match any hub in the authenticated account. Check that the folder name under `Autodesk Docs` exactly matches the hub name shown in the ACC web interface (spaces and capitalisation included).

**`Could not find 'filename.dwg' in ACC project 'X'`**  
The file was not found by folder navigation or recursive search. This can happen if the file name in ACC differs from the local name, or if the Desktop Connector has not finished syncing. Add an explicit `<DrawingItem itemId="…">` to bypass the search.

**`No .accsync.xml config file found`**  
No config file was found in the drawing's folder or any parent folder up to the drive root. The error message lists every path that was checked. Place an `accsync.xml` in the drawing's folder (or a parent folder to share it across drawings).

**`Could not determine the ACC version URN`**  
The `versions:batch-get` response did not include a version URN. This is unexpected for a valid ACC item — check that the authenticated account has at least **View** permission on the file.

**`Response status code does not indicate success: 403`**  
The token does not have the required scopes, or the authenticated user does not have write permission on the ACC project. Confirm permissions in ACC project admin settings.

**Authentication browser window does not open**  
AutoCAD may be blocking `Process.Start`. Try running AutoCAD as administrator once to complete the first authentication.

**`UNABLE TO OBTAIN CLIENT ID`**  
The APS Client ID has not been configured. Click **Settings** on the ACC Sync ribbon tab and paste your Client ID. Alternatively, create a file named `accsync.clientid` in the plugin DLL directory as described in [Section 3](#3-aps-application-setup).

---

## 11. Building from Source

> This section is for developers who want to modify or extend the plugin. End users should follow [Section 2](#2-installation) instead.


### Steps

```
dotnet build
```

After a successful build, the `AccC3DMetadata.bundle\Contents\` folder in the repository root is automatically populated with the plugin DLL and all runtime dependencies. The `PackageContents.xml` manifest already exists in the bundle folder and is not overwritten.

To test, copy or symlink `AccC3DMetadata.bundle` to `%APPDATA%\Autodesk\ApplicationPlugins\` and launch Civil 3D, or use the **C3D 2025** launch profile in `launchSettings.json` to start Civil 3D with the debugger attached.
