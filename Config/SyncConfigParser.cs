using AccC3DMetadata.Models;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace AccC3DMetadata.Config
{
    /// <summary>
    /// Holds the parsed contents of an <c>.accsync.xml</c> configuration file.
    /// </summary>
    public class SyncConfig
    {
        /// <summary>
        /// Optional ACC hub ID override.  When <c>null</c>, the hub is resolved at runtime
        /// from the Desktop Connector local file path.
        /// </summary>
        public string HubId { get; set; }

        /// <summary>
        /// Optional ACC project ID override.  When <c>null</c>, the project is resolved at
        /// runtime from the Desktop Connector local file path.
        /// </summary>
        public string ProjectId { get; set; }

        /// <summary>
        /// Optional ACC item lineage URN for the drawing.  When <c>null</c>, the item is located
        /// by walking the ACC folder tree to find a file whose name matches the open DWG.
        /// </summary>
        public string DrawingItemId { get; set; }

        /// <summary>The ordered list of attribute mappings defined in this config file.</summary>
        public List<SyncMapping> Mappings { get; set; } = new();

        /// <summary>
        /// Non-fatal warnings produced while parsing the config file (e.g. an individual
        /// mapping with an unrecognised attribute value that was skipped).
        /// </summary>
        public List<string> ParseWarnings { get; set; } = new();
    }

    /// <summary>
    /// Parses and serialises <see cref="SyncConfig"/> objects to and from the
    /// <c>.accsync.xml</c> file format.
    /// </summary>
    public static class SyncConfigParser
    {
        /// <summary>
        /// Parses an XML string into a <see cref="SyncConfig"/> object.
        /// </summary>
        /// <param name="xmlContent">The full text of an <c>.accsync.xml</c> file.</param>
        /// <returns>The populated <see cref="SyncConfig"/>.</returns>
        /// <exception cref="System.Xml.XmlException">
        /// The XML is malformed.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// A required enum value (e.g. <c>type</c>, <c>direction</c>, or <c>conflictStrategy</c>)
        /// contains an unrecognised string.
        /// </exception>
        public static SyncConfig Parse(string xmlContent)
        {
            var doc = XDocument.Parse(xmlContent);
            var root = doc.Root;

            var config = new SyncConfig
            {
                HubId = (string)root.Attribute("hubId"),
                ProjectId = (string)root.Attribute("projectId"),
                DrawingItemId = (string)root.Element("DrawingItem")?.Attribute("itemId")
            };

            // Iterate the <Mapping> elements under <Mappings>; treat a missing <Mappings>
            // element as an empty list rather than throwing.
            foreach (var m in root.Element("Mappings")?.Elements("Mapping") ?? Array.Empty<XElement>())
            {
                try
                {
                    config.Mappings.Add(new SyncMapping
                    {
                        Target = Enum.Parse<MappingTarget>((string)m.Attribute("type") ?? "BlockAttribute"),
                        AccAttributeId = (string)m.Attribute("accAttributeId"),
                        AccAttributeName = (string)m.Attribute("accAttributeName"),
                        BlockName = (string)m.Attribute("blockName"),
                        BlockAttributeTag = (string)m.Attribute("blockAttributeTag"),
                        EntityType = (string)m.Attribute("entityType"),
                        EntityBlockName = (string)m.Attribute("entityBlockName"),
                        PropertySetName = (string)m.Attribute("propertySetName"),
                        PropertyName = (string)m.Attribute("propertyName"),
                        Direction = Enum.Parse<SyncDirection>((string)m.Attribute("direction") ?? "ReadWrite"),
                        ConflictStrategy = Enum.Parse<ConflictStrategy>((string)m.Attribute("conflictStrategy") ?? "Prompt")
                    });
                }
                catch (Exception ex)
                {
                    string name = (string)m.Attribute("accAttributeName") ?? "(unknown)";
                    config.ParseWarnings.Add($"Skipped mapping '{name}': {ex.Message}");
                }
            }

            return config;
        }

        /// <summary>
        /// Serialises a <see cref="SyncConfig"/> to an XML string in the <c>.accsync.xml</c> format.
        /// </summary>
        /// <param name="config">The configuration to serialise.</param>
        /// <returns>A UTF-8 XML string with an XML declaration.</returns>
        public static string Serialize(SyncConfig config)
        {
            var mappingsEl = new XElement("Mappings");

            foreach (var m in config.Mappings)
            {
                var el = new XElement("Mapping",
                    new XAttribute("type", m.Target.ToString()),
                    new XAttribute("accAttributeId", m.AccAttributeId ?? ""),
                    new XAttribute("accAttributeName", m.AccAttributeName ?? ""),
                    new XAttribute("direction", m.Direction.ToString()),
                    new XAttribute("conflictStrategy", m.ConflictStrategy.ToString()));

                // Target-specific attributes are written only for the relevant target type
                // to keep the XML clean and avoid misleading empty attributes.
                if (m.Target == MappingTarget.BlockAttribute)
                {
                    el.Add(new XAttribute("blockName", m.BlockName ?? ""));
                    el.Add(new XAttribute("blockAttributeTag", m.BlockAttributeTag ?? ""));
                }
                else
                {
                    el.Add(new XAttribute("entityType", m.EntityType ?? ""));
                    if (!string.IsNullOrEmpty(m.EntityBlockName))
                        el.Add(new XAttribute("entityBlockName", m.EntityBlockName));
                    el.Add(new XAttribute("propertySetName", m.PropertySetName ?? ""));
                    el.Add(new XAttribute("propertyName", m.PropertyName ?? ""));
                }

                mappingsEl.Add(el);
            }

            var root = new XElement("AccSync",
                new XAttribute("version", "1.0"),
                new XAttribute("hubId", config.HubId ?? ""),
                new XAttribute("projectId", config.ProjectId ?? ""));

            // Only emit the DrawingItem element when an explicit item ID is configured.
            if (!string.IsNullOrEmpty(config.DrawingItemId))
                root.Add(new XElement("DrawingItem", new XAttribute("itemId", config.DrawingItemId)));

            root.Add(mappingsEl);

            return new XDocument(new XDeclaration("1.0", "utf-8", null), root).ToString();
        }
    }
}
