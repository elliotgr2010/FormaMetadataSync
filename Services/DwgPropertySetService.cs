using AccC3DMetadata.Models;
using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.DatabaseServices;
using System;
using System.Collections.Generic;

namespace AccC3DMetadata.Services
{
    /// <summary>
    /// Provides read, write, and attachment operations for Civil 3D property sets on drawing entities.
    /// </summary>
    /// <remarks>
    /// All methods require an active <see cref="Transaction"/> opened on the supplied
    /// <see cref="Database"/>. Callers are responsible for committing or aborting the transaction.
    /// </remarks>
    internal static class DwgPropertySetService
    {
        /// <summary>
        /// Reads the value of a named property from a named property set attached to
        /// <paramref name="entity"/>.
        /// </summary>
        /// <param name="tr">The active AutoCAD transaction.</param>
        /// <param name="entity">The entity whose property sets will be inspected.</param>
        /// <param name="setName">
        /// Name of the property set definition to match (case-insensitive).
        /// </param>
        /// <param name="propName">Name of the property within the set (case-insensitive).</param>
        /// <returns>
        /// The property value converted to a string, or <c>null</c> if the property set or
        /// property is not attached to this entity.
        /// </returns>
        public static string ReadPropertyValue(
            Transaction tr, DBObject entity, string setName, string propName)
        {
            foreach (ObjectId psId in PropertyDataServices.GetPropertySets(entity))
            {
                var ps = tr.GetObject(psId, OpenMode.ForRead) as PropertySet;
                if (ps == null) continue;
                if (!string.Equals(ps.PropertySetDefinitionName, setName, StringComparison.OrdinalIgnoreCase))
                    continue;

                int id = ps.PropertyNameToId(propName); // Returns -1 if the property name is not found.
                if (id < 0) continue;

                return ps.GetAt(id)?.ToString();
            }
            return null;
        }

        /// <summary>
        /// Writes <paramref name="value"/> to a named property in a named property set attached
        /// to <paramref name="entity"/>.
        /// </summary>
        /// <remarks>
        /// The method opens the matching property set for read first to locate the property ID,
        /// then upgrades to write — this avoids needlessly upgrading sets that do not match
        /// the target definition name.
        /// <para>
        /// <see cref="EnsurePropertySetAttached"/> must be called before this method if the
        /// property set may not yet be attached to the entity.
        /// </para>
        /// </remarks>
        /// <param name="tr">The active AutoCAD transaction — must be writable.</param>
        /// <param name="entity">The entity to write the property value to.</param>
        /// <param name="setName">Name of the property set definition (case-insensitive).</param>
        /// <param name="propName">Name of the property within the set (case-insensitive).</param>
        /// <param name="value">The value to assign.</param>
        public static void WritePropertyValue(
            Transaction tr, DBObject entity, string setName, string propName, string value)
        {
            foreach (ObjectId psId in PropertyDataServices.GetPropertySets(entity))
            {
                // Open for read to check the set name — most sets will not match, so we avoid
                // the cost of upgrading objects we will never write to.
                var ps = tr.GetObject(psId, OpenMode.ForRead) as PropertySet;
                if (ps == null) continue;
                if (!string.Equals(ps.PropertySetDefinitionName, setName, StringComparison.OrdinalIgnoreCase))
                    continue;

                int id = ps.PropertyNameToId(propName);
                if (id < 0) continue;

                // Upgrade to write only for the matching set.
                var psWrite = tr.GetObject(psId, OpenMode.ForWrite) as PropertySet;
                psWrite?.SetAt(id, value);
                return;
            }
        }

        /// <summary>
        /// Attaches the named property set to <paramref name="entity"/> if it is not already
        /// attached and the property set definition exists in the drawing database.
        /// </summary>
        /// <remarks>
        /// A property set definition that is defined in the drawing but not yet attached to a
        /// specific entity will cause <see cref="WritePropertyValue"/> to silently do nothing.
        /// Call this method before writing whenever the entity may be new or the set may not
        /// have been propagated automatically.
        /// </remarks>
        /// <param name="tr">The active AutoCAD transaction.</param>
        /// <param name="entity">The entity to attach the property set to.</param>
        /// <param name="db">The drawing database that contains the property set definition.</param>
        /// <param name="setName">Name of the property set definition to attach (case-insensitive).</param>
        public static void EnsurePropertySetAttached(
            Transaction tr, DBObject entity, Database db, string setName)
        {
            // Check that the definition exists in this drawing before attempting to attach it.
            var dict = new DictionaryPropertySetDefinitions(db);
            if (!dict.Has(setName, tr)) return;

            // Check if the property set is already attached — attaching twice would throw.
            foreach (ObjectId psId in PropertyDataServices.GetPropertySets(entity))
            {
                var ps = tr.GetObject(psId, OpenMode.ForRead) as PropertySet;
                if (string.Equals(ps?.PropertySetDefinitionName, setName, StringComparison.OrdinalIgnoreCase))
                    return; // Already attached — nothing to do.
            }

            PropertyDataServices.AddPropertySet(entity, dict.GetAt(setName));
        }

        /// <summary>
        /// Enumerates the entities in the current space that are eligible to carry the property
        /// set described by <paramref name="map"/>, filtered by entity type and optional block name.
        /// </summary>
        /// <param name="tr">The active AutoCAD transaction.</param>
        /// <param name="db">The active AutoCAD database.</param>
        /// <param name="map">
        /// The mapping whose <see cref="SyncMapping.EntityType"/> and
        /// <see cref="SyncMapping.EntityBlockName"/> fields define the filter criteria.
        /// </param>
        /// <returns>
        /// A sequence of <see cref="DBObject"/> instances opened <see cref="OpenMode.ForRead"/>.
        /// </returns>
        public static IEnumerable<DBObject> GetEntitiesForMapping(
            Transaction tr, Database db, SyncMapping map)
        {
            var cs = tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;
            if (cs == null) yield break;

            foreach (ObjectId entId in cs)
            {
                var obj = tr.GetObject(entId, OpenMode.ForRead);

                if (string.Equals(map.EntityType, "BlockReference", StringComparison.OrdinalIgnoreCase))
                {
                    if (obj is not BlockReference br) continue;

                    // When EntityBlockName is specified, restrict to instances of that block definition.
                    if (!string.IsNullOrEmpty(map.EntityBlockName) &&
                        !string.Equals(br.Name, map.EntityBlockName, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                yield return obj;
            }
        }
    }
}
