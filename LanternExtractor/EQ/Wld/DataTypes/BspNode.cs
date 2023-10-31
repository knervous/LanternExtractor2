using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using System.Collections.Generic;
using LanternExtractor.EQ.Wld.Fragments;
using System;

namespace LanternExtractor.EQ.Wld.DataTypes
{
    public class BspNode
    {
        public float NormalX { get; set; }
        public float NormalY { get; set; }
        public float NormalZ { get; set; }
        public float SplitDistance { get; set; }
        public int RegionId { get; set; }
        public int LeftNode { get; set; }
        public int RightNode { get; set; }
        public bool IsRightChild { get; set; } = false;
        public bool IsLeftChild { get; set; } = false;

        public bool ContainsNonnormalRegion = false;
        public BspNode LeftChild { get; set; }
        public BspNode RightChild { get; set; }
        public Vector3 BoundingBoxMin { get; set; }
        public Vector3 BoundingBoxMax { get; set; }
        public BspNode Parent { get; set; }
        public BspRegion Region { get; set; }

        public string Serialize(bool pruneNormalRegions = false)
        {
            JsonSerializerOptions options = new JsonSerializerOptions()
            {
                MaxDepth = 1000
            };
            return JsonSerializer.Serialize(SerializeRoot(pruneNormalRegions), options);
        }

        public IDictionary<string, object> SerializeRoot(bool pruneNormalRegions)
        {
            var root = new System.Dynamic.ExpandoObject() as IDictionary<string, object>;
             root.Add("min", new
            {
                x = BoundingBoxMin.X,
                y = BoundingBoxMin.Y,
                z = BoundingBoxMin.Z,
            });
            root.Add("max", new
            {
                x = BoundingBoxMax.X,
                y = BoundingBoxMax.Y,
                z = BoundingBoxMax.Z,
            });
            var leafNodes = new List<IDictionary<string, object>>();
            Action<BspNode> traverse = null;
            traverse = (BspNode node) => { 
                if (node.LeftChild != null) { 
                    traverse(node.LeftChild);
                }
                if (node.RightChild != null) { 
                    traverse(node.RightChild);
                }
                if (node.LeftChild == null && node.RightChild == null
                && node.Region?.RegionType?.RegionTypes != null) { 

                var props = new System.Dynamic.ExpandoObject() as IDictionary<string, object>;
                props.Add("regions", (node.Region?.RegionType?.RegionTypes ?? new List<RegionType>()).Select(a => (int)a));
                props.Add("min", new
                {
                    x = node.BoundingBoxMin.X,
                    y = node.BoundingBoxMin.Y,
                    z = node.BoundingBoxMin.Z,
                });
                props.Add("max", new
                {
                    x = node.BoundingBoxMax.X,
                    y = node.BoundingBoxMax.Y,
                    z = node.BoundingBoxMax.Z,
                });
                if (Region?.RegionType?.Zoneline != null)
                {
                    props.Add("zone", new
                    {
                        type = (int)node.Region.RegionType.Zoneline.Type,
                        index = node.Region.RegionType.Zoneline.Index,
                        zoneIndex = node.Region.RegionType.Zoneline.ZoneIndex,
                        heading = node.Region.RegionType.Zoneline.Heading,
                        position = new
                        {
                            x = node.Region.RegionType.Zoneline.Position.x,
                            y = node.Region.RegionType.Zoneline.Position.y,
                            z = node.Region.RegionType.Zoneline.Position.z,
                        }
                    });
                }
                leafNodes.Add(props);
                }
                  
            };
            traverse(this);
            root.Add("leafNodes", leafNodes);
            //AddProperties(root, pruneNormalRegions);
            return root;
        }

        private void AddProperties(IDictionary<string, object> properties, bool pruneNormalRegions)
        {
            if (pruneNormalRegions && !ContainsNonnormalRegion)
            {
                return;
            }
            properties.Add("min", new
            {
                x = BoundingBoxMin.X,
                y = BoundingBoxMin.Y,
                z = BoundingBoxMin.Z,
            });
            properties.Add("max", new
            {
                x = BoundingBoxMax.X,
                y = BoundingBoxMax.Y,
                z = BoundingBoxMax.Z,
            });

            properties.Add("regions", (Region?.RegionType?.RegionTypes ?? new List<RegionType>()).Select(a => (int)a));

            if (Region?.RegionType?.Zoneline != null)
            {
                properties.Add("zone", new
                {
                    type = (int)Region.RegionType.Zoneline.Type,
                    index = Region.RegionType.Zoneline.Index,
                    zoneIndex = Region.RegionType.Zoneline.ZoneIndex,
                    heading = Region.RegionType.Zoneline.Heading,
                    position = new
                    {
                        x = Region.RegionType.Zoneline.Position.x,
                        y = Region.RegionType.Zoneline.Position.y,
                        z = Region.RegionType.Zoneline.Position.z,
                    }
                });
            }
            properties.Add("left", LeftChild?.SerializeRoot(pruneNormalRegions));
            properties.Add("right", RightChild?.SerializeRoot(pruneNormalRegions));
        }
    }
}