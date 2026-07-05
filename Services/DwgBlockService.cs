using Autodesk.AutoCAD.DatabaseServices;
using System;
using System.Collections.Generic;

namespace AccC3DMetadata.Services
{
    /// <summary>
    /// Provides read and write access to AutoCAD block attribute values within the current space
    /// of the active drawing database.
    /// </summary>
    /// <remarks>
    /// All methods require an active <see cref="Transaction"/> opened on the supplied
    /// <see cref="Database"/>. Callers are responsible for committing or aborting the transaction.
    /// </remarks>
    internal static class DwgBlockService
    {
        /// <summary>
        /// Yields the value of the specified attribute tag from every instance of the named block
        /// found in the current space (model or paper space).
        /// </summary>
        /// <param name="tr">The active AutoCAD transaction.</param>
        /// <param name="db">The active AutoCAD database.</param>
        /// <param name="blockName">
        /// Name of the block definition to match (case-insensitive).
        /// </param>
        /// <param name="tag">
        /// Attribute tag to read from each matching block reference (case-insensitive).
        /// </param>
        /// <returns>
        /// A sequence of <c>(blockRefId, value)</c> pairs — one entry per matching block instance.
        /// The sequence is empty when the block or tag is not found.
        /// </returns>
        public static IEnumerable<(ObjectId blockRefId, string value)> ReadBlockAttributeValues(
            Transaction tr, Database db, string blockName, string tag)
        {
            // Open the current space (model or active paper-space layout) for reading.
            var cs = tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;
            if (cs == null) yield break;

            foreach (ObjectId entId in cs)
            {
                // Skip anything that is not a block reference.
                if (tr.GetObject(entId, OpenMode.ForRead) is not BlockReference br) continue;
                if (!string.Equals(br.Name, blockName, StringComparison.OrdinalIgnoreCase)) continue;

                foreach (ObjectId arId in br.AttributeCollection)
                {
                    var ar = tr.GetObject(arId, OpenMode.ForRead) as AttributeReference;
                    if (ar != null && string.Equals(ar.Tag, tag, StringComparison.OrdinalIgnoreCase))
                        yield return (entId, ar.TextString);
                }
            }
        }

        /// <summary>
        /// Writes <paramref name="value"/> to the specified attribute tag on every instance of
        /// the named block found in the current space.
        /// </summary>
        /// <param name="tr">The active AutoCAD transaction — must be writable.</param>
        /// <param name="db">The active AutoCAD database.</param>
        /// <param name="blockName">Name of the block definition (case-insensitive).</param>
        /// <param name="tag">Attribute tag to write (case-insensitive).</param>
        /// <param name="value">
        /// Value to assign. A <c>null</c> value is written as an empty string to avoid
        /// leaving the attribute in an invalid state.
        /// </param>
        public static void WriteBlockAttributeValues(
            Transaction tr, Database db, string blockName, string tag, string value)
        {
            var cs = tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;
            if (cs == null) return;

            foreach (ObjectId entId in cs)
            {
                // Open directly for write — we know we will modify any matching block reference,
                // so a two-step read-then-upgrade would just add overhead.
                if (tr.GetObject(entId, OpenMode.ForWrite) is not BlockReference br) continue;
                if (!string.Equals(br.Name, blockName, StringComparison.OrdinalIgnoreCase)) continue;

                foreach (ObjectId arId in br.AttributeCollection)
                {
                    var ar = tr.GetObject(arId, OpenMode.ForWrite) as AttributeReference;
                    if (ar != null && string.Equals(ar.Tag, tag, StringComparison.OrdinalIgnoreCase))
                        ar.TextString = value ?? string.Empty;
                }
            }
        }
    }
}
