namespace AccC3DMetadata.Models
{
    /// <summary>
    /// Represents a single attribute value conflict detected during a bidirectional sync where
    /// the ACC value and the DWG value differ and the mapping's strategy is
    /// <see cref="ConflictStrategy.Prompt"/>.
    /// </summary>
    /// <remarks>
    /// Conflict instances are collected during the first pass over all mappings in
    /// <see cref="Services.SyncOrchestrator.RunAsync"/>, presented to the user via the
    /// <see cref="UI.ConflictResolutionDialog"/>, and then resolved in a second pass after
    /// the user has made their selections.
    /// </remarks>
    public class SyncConflict
    {
        /// <summary>The mapping configuration that produced this conflict.</summary>
        public SyncMapping Mapping { get; set; }

        /// <summary>The current value stored in ACC (may be <c>null</c> if no value has been set).</summary>
        public string AccValue { get; set; }

        /// <summary>The current value in the DWG (may be <c>null</c> if the block/property set is absent).</summary>
        public string DwgValue { get; set; }

        /// <summary>
        /// The user's chosen resolution strategy, populated by the
        /// <see cref="UI.ConflictResolutionDialog"/> before the second apply pass.
        /// Defaults to <see cref="ConflictStrategy.AccWins"/> as a safe starting selection
        /// in the dialog grid.
        /// </summary>
        public ConflictStrategy Resolution { get; set; } = ConflictStrategy.AccWins;
    }
}
