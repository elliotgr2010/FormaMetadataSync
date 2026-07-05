namespace AccC3DMetadata.Models
{
    /// <summary>Specifies the type of DWG data store the mapping targets.</summary>
    public enum MappingTarget
    {
        /// <summary>An attribute on a named block reference in the drawing.</summary>
        BlockAttribute,

        /// <summary>A property within a Civil 3D property set attached to an entity.</summary>
        PropertySet
    }

    /// <summary>Controls the allowed direction of data flow for a mapping or sync operation.</summary>
    public enum SyncDirection
    {
        /// <summary>ACC → DWG only; the DWG value is never written back to ACC.</summary>
        Read,

        /// <summary>DWG → ACC only; the ACC value is never written into the DWG.</summary>
        Write,

        /// <summary>Both directions; conflicts are resolved by the mapping's <see cref="ConflictStrategy"/>.</summary>
        ReadWrite
    }

    /// <summary>Specifies how to resolve a value conflict in a bidirectional mapping.</summary>
    public enum ConflictStrategy
    {
        /// <summary>Always use the value from ACC, overwriting the DWG value.</summary>
        AccWins,

        /// <summary>Always use the value from the DWG, overwriting the ACC value.</summary>
        DwgWins,

        /// <summary>Show the <see cref="UI.ConflictResolutionDialog"/> and let the user decide.</summary>
        Prompt,

        /// <summary>Leave both values unchanged when they differ.</summary>
        Skip
    }

    /// <summary>
    /// Represents a single configured mapping between one ACC custom attribute and one DWG data value.
    /// Populated by <see cref="Config.SyncConfigParser"/> from the <c>.accsync.xml</c> file.
    /// </summary>
    public class SyncMapping
    {
        /// <summary>
        /// Whether this mapping targets a block attribute or a Civil 3D property set.
        /// Corresponds to the <c>type</c> XML attribute.
        /// </summary>
        public MappingTarget Target { get; set; }

        // ── ACC side ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Optional pre-resolved ACC attribute definition ID.
        /// When present, skips the name-based definition lookup at runtime.
        /// Corresponds to the optional <c>accAttributeId</c> XML attribute.
        /// </summary>
        public string AccAttributeId { get; set; }

        /// <summary>
        /// Display name of the ACC custom attribute.  Used to resolve the definition ID
        /// at runtime via the attribute definitions endpoint.
        /// Corresponds to the <c>accAttributeName</c> XML attribute.
        /// </summary>
        public string AccAttributeName { get; set; }

        // ── BlockAttribute target fields ───────────────────────────────────────────

        /// <summary>
        /// Name of the AutoCAD block definition whose attribute is to be read or written.
        /// Required when <see cref="Target"/> is <see cref="MappingTarget.BlockAttribute"/>.
        /// </summary>
        public string BlockName { get; set; }

        /// <summary>
        /// Tag name of the attribute on the block reference.
        /// Required when <see cref="Target"/> is <see cref="MappingTarget.BlockAttribute"/>.
        /// </summary>
        public string BlockAttributeTag { get; set; }

        // ── PropertySet target fields ──────────────────────────────────────────────

        /// <summary>
        /// Entity type filter for property set targets (e.g. <c>"BlockReference"</c>).
        /// Required when <see cref="Target"/> is <see cref="MappingTarget.PropertySet"/>.
        /// </summary>
        public string EntityType { get; set; }

        /// <summary>
        /// When <see cref="EntityType"/> is <c>"BlockReference"</c>, restricts the search to
        /// instances of the named block definition.  Leave <c>null</c> to match all block references.
        /// </summary>
        public string EntityBlockName { get; set; }

        /// <summary>
        /// Name of the Civil 3D property set definition.
        /// Required when <see cref="Target"/> is <see cref="MappingTarget.PropertySet"/>.
        /// </summary>
        public string PropertySetName { get; set; }

        /// <summary>
        /// Name of the property within <see cref="PropertySetName"/>.
        /// Required when <see cref="Target"/> is <see cref="MappingTarget.PropertySet"/>.
        /// </summary>
        public string PropertyName { get; set; }

        // ── Sync behaviour ─────────────────────────────────────────────────────────

        /// <summary>
        /// Permitted data-flow direction for this mapping.  Defaults to
        /// <see cref="SyncDirection.ReadWrite"/>.  Combined with the command-level direction
        /// in <see cref="Services.SyncOrchestrator.RunAsync"/> — the more restrictive of the two wins.
        /// </summary>
        public SyncDirection Direction { get; set; } = SyncDirection.ReadWrite;

        /// <summary>
        /// How to resolve a value conflict in a bidirectional sync.  Defaults to
        /// <see cref="ConflictStrategy.Prompt"/>.  Ignored for read-only or write-only mappings.
        /// </summary>
        public ConflictStrategy ConflictStrategy { get; set; } = ConflictStrategy.Prompt;
    }
}
