using System.Collections.Generic;
using GlmSharp;
using LanternExtractor.EQ.Wld.DataTypes;
using LanternExtractor.EQ.Wld.Fragments;
using LanternExtractor.EQ.Wld.Helpers;

namespace LanternExtractor.EQ.Wld.Exporters
{
    public class MeshIntermediateExporter : TextAssetExporter
    {
        private bool _useGroups;
        private int _currentBaseIndex;

        public MeshIntermediateExporter(bool useGroups)
        {
            _useGroups = useGroups;
            _export.AppendLine("# Lantern Test Intermediate Format");
        }
        
        public override void AddFragmentData(WldFragment data)
        {
            Mesh mesh = data as Mesh;

            if (mesh == null)
            {
                return;
            }

            _export.Append("ml");
            _export.Append(",");
            _export.Append(FragmentNameCleaner.CleanName(mesh.MaterialList));
            _export.AppendLine();

            foreach (var vertex in mesh.Vertices)
            {
                _export.Append("v");
                _export.Append(",");
                _export.Append(vertex.x + mesh.Center.x);
                _export.Append(",");
                _export.Append(vertex.z + mesh.Center.z);
                _export.Append(",");
                _export.Append(vertex.y + mesh.Center.y);
                _export.AppendLine();
            }
            
            foreach (var textureUv in mesh.TextureUvCoordinates)
            {
                _export.Append("uv");
                _export.Append(",");
                _export.Append(textureUv.x);
                _export.Append(",");
                _export.Append(textureUv.y);
                _export.AppendLine();
            }
            
            foreach (var normal in mesh.Normals)
            {
                _export.Append("n");
                _export.Append(",");
                _export.Append(normal.x);
                _export.Append(",");
                _export.Append(normal.y);
                _export.Append(",");
                _export.Append(normal.z);
                _export.AppendLine();
            }

            foreach (var vertexColor in mesh.Colors)
            {
                _export.Append("c");
                _export.Append(",");
                _export.Append(vertexColor.B);
                _export.Append(",");
                _export.Append(vertexColor.G);
                _export.Append(",");
                _export.Append(vertexColor.R);
                _export.Append(",");
                _export.Append(vertexColor.A);
                _export.AppendLine();
            }

            int currentPolygon = 0;

            foreach (RenderGroup group in mesh.MaterialGroups)
            {
                string materialName = string.Empty;
                
                _export.Append("mg");
                _export.Append(",");
                _export.Append(group.MaterialIndex - mesh.StartTextureIndex);
                _export.Append(",");
                _export.Append(group.PolygonCount);
                _export.AppendLine();
                
                for (int i = 0; i < group.PolygonCount; ++i)
                {
                    int vertex1 = mesh.Indices[currentPolygon].Vertex1;
                    int vertex2 = mesh.Indices[currentPolygon].Vertex2;
                    int vertex3 = mesh.Indices[currentPolygon].Vertex3;

                    _export.Append("i");
                    _export.Append(",");
                    _export.Append(group.MaterialIndex);
                    _export.Append(",");
                    _export.Append(_currentBaseIndex + vertex1);
                    _export.Append(",");
                    _export.Append(_currentBaseIndex + vertex2);
                    _export.Append(",");
                    _export.Append(_currentBaseIndex + vertex3);
                    _export.AppendLine();
                    currentPolygon++;
                }
            }

            if (mesh.AnimatedVerticesReference != null)
            {
                _export.Append("ad");
                _export.Append(",");
                _export.Append(mesh.AnimatedVerticesReference.MeshAnimatedVertices.Delay);
                _export.AppendLine();

                for (var i = 0; i < mesh.AnimatedVerticesReference.MeshAnimatedVertices.Frames.Count; i++)
                {
                    List<vec3> frame = mesh.AnimatedVerticesReference.MeshAnimatedVertices.Frames[i];
                    foreach (vec3 position in frame)
                    {
                        _export.Append("av");
                        _export.Append(",");
                        _export.Append(i);
                        _export.Append(",");
                        _export.Append(position.x + mesh.Center.x);
                        _export.Append(",");
                        _export.Append(position.z + mesh.Center.z);
                        _export.Append(",");
                        _export.Append(position.y + mesh.Center.y);
                        _export.AppendLine();
                    }
                }
            }

            if (!_useGroups)
            {
                _currentBaseIndex += mesh.Vertices.Count;
            }
        }
    }
}