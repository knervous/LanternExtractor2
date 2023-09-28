using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using System.Collections.Generic;
using LanternExtractor.EQ.Wld.Fragments;

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
            AddProperties(root, pruneNormalRegions);
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