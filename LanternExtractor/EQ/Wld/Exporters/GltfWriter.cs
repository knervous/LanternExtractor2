﻿using GlmSharp;
using LanternExtractor.EQ.Wld.Fragments;
using LanternExtractor.EQ.Wld.Helpers;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using SharpGLTF.Scenes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;

using WldColor = LanternExtractor.EQ.Wld.DataTypes.Color;
using Animation = LanternExtractor.EQ.Wld.DataTypes.Animation;
using System.Drawing.Imaging;
using LanternExtractor.Infrastructure.Logger;
using LanternExtractor.Infrastructure;

namespace LanternExtractor.EQ.Wld.Exporters
{
    public class GltfWriter : TextAssetWriter
    {
        public enum GltfExportFormat
        {
            /// <summary>
            /// Separate .gltf json file, .bin binary, and images externally referenced
            /// </summary>
            GlTF = 0,
            /// <summary>
            /// One binary file with json metadata and images packaged within
            /// </summary>
            Glb = 1
        }

        public enum ModelGenerationMode
        {
            /// <summary>
            /// Combine all meshes
            /// </summary>
            Combine = 0,
            /// <summary>
            /// Every mesh remains separated
            /// </summary>
            Separate = 1
        }

        public IDictionary<string, MaterialBuilder> Materials { get; private set; }

        private readonly bool _exportVertexColors;
        private readonly GltfExportFormat _exportFormat = GltfExportFormat.GlTF;
        private readonly ILogger _logger;

        #region Constants
        private static readonly float MaterialRoughness = 0.9f;
        private static readonly string MaterialInvisName = "Invis";
        private static readonly string MaterialBlankName = "Blank";
        private static readonly string DefaultModelPoseAnimationKey = "pos";
        private static readonly ISet<ShaderType> ShaderTypesThatNeedAlphaAddedToImage =
            new HashSet<ShaderType>()
            {
                ShaderType.Transparent25,
                ShaderType.Transparent50,
                ShaderType.Transparent75,
                ShaderType.TransparentAdditive,
                ShaderType.TransparentAdditiveUnlit,
                ShaderType.TransparentAdditiveUnlitSkydome,
                ShaderType.TransparentSkydome
            };
        private static readonly ISet<string> ImagesInZoneFoundInObj = new HashSet<string>()
        {
            "canwall",
            "maywall",
            "kruphse3"
        };
        private static readonly ISet<string> LoopedAnimationKeys = new HashSet<string>()
        {
             "pos", // name is used for animated objects
             "p01", // Stand
             "l01", // Walk
             "l02", // Run
             "l05", // falling
             "l06", // crouch walk
             "l07", // climbing
             "l09", // swim treading
             "p03", // rotating
             "p06", // swim
             "p07", // sitting
             "p08", // stand (arms at sides) 
             "sky"
        };

        private static readonly IDictionary<string, string> AnimationDescriptions = new Dictionary<string, string>()
        {
            {"c01", "Combat Kick"},
            {"c02", "Combat Piercing"},
            {"c03", "Combat 2H Slash"},
            {"c04", "Combat 2H Blunt"},
            {"c05", "Combat Throwing"},
            {"c06", "Combat 1H Slash Left"},
            {"c07", "Combat Bash"},
            {"c08", "Combat Hand to Hand"},
            {"c09", "Combat Archery"},
            {"c10", "Combat Swim Attack"},
            {"c11", "Combat Round Kick"},
            {"d01", "Damage 1"},
            {"d02", "Damage 2"},
            {"d03", "Damage from Trap"},
            {"d04", "Drowning_Burning"},
            {"d05", "Dying"},
            {"l01", "Walk"},
            {"l02", "Run"},
            {"l03", "Jump (Running)"},
            {"l04", "Jump (Standing)"},
            {"l05", "Falling"},
            {"l06", "Crouch Walk"},
            {"l07", "Climbing"},
            {"l08", "Crouching"},
            {"l09", "Swim Treading"},
            {"o01", "Idle"},
            {"s01", "Cheer"},
            {"s02", "Mourn"},
            {"s03", "Wave"},
            {"s04", "Rude"},
            {"s05", "Yawn"},
            {"p01", "Stand"},
            {"p02", "Sit_Stand"},
            {"p03", "Shuffle Feet"},
            {"p04", "Float_Walk_Strafe"},
            {"p05", "Kneel"},
            {"p06", "Swim"},
            {"p07", "Sitting"},
            {"t01", "UNUSED????"},
            {"t02", "Stringed Instrument"},
            {"t03", "Wind Instrument"},
            {"t04", "Cast Pull Back"},
            {"t05", "Raise and Loop Arms"},
            {"t06", "Cast Push Forward"},
            {"t07", "Flying Kick"},
            {"t08", "Rapid Punches"},
            {"t09", "Large Punch"},
            {"s06", "Nod"},
            {"s07", "Amazed"},
            {"s08", "Plead"},
            {"s09", "Clap"},
            {"s10", "Distress"},
            {"s11", "Blush"},
            {"s12", "Chuckle"},
            {"s13", "Burp"},
            {"s14", "Duck"},
            {"s15", "Look Around"},
            {"s16", "Dance"},
            {"s17", "Blink"},
            {"s18", "Glare"},
            {"s19", "Drool"},
            {"s20", "Kneel"},
            {"s21", "Laugh"},
            {"s22", "Point"},
            {"s23", "Ponder"},
            {"s24", "Ready"},
            {"s25", "Salute"},
            {"s26", "Shiver"},
            {"s27", "Tap Foot"},
            {"s28", "Bow"},
            {"p08", "Stand (Arms at Sides)"},
            {"o02", "Idle (Arms at Sides)"},
            {"o03", "Idle (Sitting)"},
            {"pos", "Default"},
            {"drf", "Pose"}
        };
        private static readonly float ZoneScaleMultiplier = 0.1f;
        private static readonly Matrix4x4 MirrorXAxisMatrix = Matrix4x4.CreateReflection(new Plane(1, 0, 0, 0));
        private static readonly Matrix4x4 CorrectedWorldMatrix = MirrorXAxisMatrix * Matrix4x4.CreateScale(ZoneScaleMultiplier);
        private static readonly Matrix4x4 CorrectedSingularActorMatrix = Matrix4x4.CreateReflection(new Plane(0, 0, 1, 0));
		#endregion

		private SceneBuilder _scene;
        private IMeshBuilder<MaterialBuilder> _combinedMeshBuilder;
        private ISet<string> _meshMaterialsToSkip;
        private IDictionary<string, IMeshBuilder<MaterialBuilder>> _sharedMeshes;
        private IDictionary<string, List<NodeBuilder>> _skeletons;
        private IDictionary<string, List<(string, string)>> _skeletonChildrenAttachBones;
        private IEnumerable<IGrouping<UniqueLight, LightInstance>> _lightGroups;
        private bool _separateTwoFacedTriangles;
        private PlayerCharacterModel _playerCharacterModel;
        private float _lightIntensityMultiplier;

        public GltfWriter(bool exportVertexColors, GltfExportFormat exportFormat, ILogger logger, bool separateTwoFacedTriangles = false, PlayerCharacterModel playerCharacterModel = null)
        {
            _exportVertexColors = exportVertexColors;
            _exportFormat = exportFormat;
            _logger = logger;

            Materials = new Dictionary<string, MaterialBuilder>();
            _meshMaterialsToSkip = new HashSet<string>();
            _skeletons = new Dictionary<string, List<NodeBuilder>>();
            _skeletonChildrenAttachBones = new Dictionary<string, List<(string, string)>>();
            _sharedMeshes = new Dictionary<string, IMeshBuilder<MaterialBuilder>>();
            _separateTwoFacedTriangles = separateTwoFacedTriangles;
            _playerCharacterModel = playerCharacterModel;
            _scene = new SceneBuilder();
        }

        public override void AddFragmentData(WldFragment fragment)
        {
            AddFragmentData(
                mesh:(Mesh)fragment, 
                generationMode:ModelGenerationMode.Separate );
        }

        public void AddFragmentData(Mesh mesh, SkeletonHierarchy skeleton, int singularBoneIndex = -1, string parentSkeletonName = null, 
            string parentSkeletonAttachBoneName = null, string meshNameOverride = null, bool usesMobPieces = false )
        {
            if (!_skeletons.ContainsKey(skeleton.ModelBase))
            {
                AddNewSkeleton(skeleton, parentSkeletonName, parentSkeletonAttachBoneName);
            }

            AddFragmentData(
                mesh: mesh, 
                generationMode: ModelGenerationMode.Combine, 
                isSkinned: true, 
                meshNameOverride: meshNameOverride, 
                singularBoneIndex: singularBoneIndex,
                usesMobPieces: usesMobPieces);
        }

        public void CopyMaterialList(GltfWriter gltfWriter)
        {
            Materials = gltfWriter.Materials;
        }

        public void GenerateGltfMaterials(IEnumerable<MaterialList> materialLists, string textureImageFolder, bool loadVariants = false)
        {
            if (!Materials.Any())
            {
                Materials.Add(MaterialBlankName, GetBlankMaterial());
            }

            foreach (var materialList in materialLists)
            {
                if (materialList == null) continue;

                foreach (var eqMaterial in materialList.Materials)
                {
                    CreateGltfMaterialFromEqMaterial(eqMaterial, textureImageFolder);
                    if (loadVariants)
                    {
                        foreach (var variantMaterial in materialList.GetMaterialVariants(eqMaterial, _logger).Where(m => m != null))
                        {
                            CreateGltfMaterialFromEqMaterial(variantMaterial, textureImageFolder);
                        }
                    }
                }
            }
        }

        public void AddFragmentData(
            Mesh mesh, 
            ModelGenerationMode generationMode, 
            bool isSkinned = false, 
            string meshNameOverride = null,
            int singularBoneIndex = -1, 
            ObjInstance objectInstance = null, 
            int instanceIndex = 0,
            bool isZoneMesh = false,
            bool usesMobPieces = false)
        {
            var meshName = meshNameOverride ?? FragmentNameCleaner.CleanName(mesh);
			var transformMatrix = objectInstance == null ? Matrix4x4.Identity : CreateTransformMatrixForObjectInstance(objectInstance);
			transformMatrix = transformMatrix *= isZoneMesh ? CorrectedWorldMatrix : MirrorXAxisMatrix;

			var canExportVertexColors = _exportVertexColors &&
                ((objectInstance?.Colors?.Colors != null && objectInstance.Colors.Colors.Any())
                || (mesh?.Colors != null && mesh.Colors.Any()));
            
            if (mesh.AnimatedVerticesReference == null && !canExportVertexColors && objectInstance != null && 
                _sharedMeshes.TryGetValue(meshName, out var existingMesh))
            {
                if (generationMode == ModelGenerationMode.Separate)
                {
                    _scene.AddRigidMesh(existingMesh, transformMatrix);
                }
                return;
            }

            IMeshBuilder<MaterialBuilder> gltfMesh;

            if (objectInstance != null && (canExportVertexColors || mesh.AnimatedVerticesReference != null))
            {
                meshName += $".{instanceIndex:00}";
            }
            if (generationMode == ModelGenerationMode.Combine)
            {
                if (_combinedMeshBuilder == null)
                {
                    _combinedMeshBuilder = InstantiateMeshBuilder(meshName, isSkinned, canExportVertexColors);
                }
                gltfMesh = _combinedMeshBuilder;
            }
            else
            {
                gltfMesh = InstantiateMeshBuilder(meshName, isSkinned, canExportVertexColors);
            }

            // Keeping track of vertex indexes for each vertex position in case it's an
            // animated mesh so we can create morph targets later
            var gltfVertexPositionToWldVertexIndex = new Dictionary<VertexPositionNormal, int>();
            
            var polygonIndex = 0;
            var meshHelper = new WldMeshHelper(mesh, _separateTwoFacedTriangles);
            foreach (var materialGroup in mesh.MaterialGroups)
            {
                var material = mesh.MaterialList.Materials[materialGroup.MaterialIndex];
                Color? baseColor = null;
                if (_playerCharacterModel != null)
                {
                    var equip = _playerCharacterModel.GetEquipmentForImageName(material.GetFirstBitmapNameWithoutExtension(), out var isChest);
                    if (equip != null)
                    {
                        if (equip.Material > 0)
                        {
                            var alternateSkins = mesh.MaterialList.GetMaterialVariants(material, _logger);
                            var materialIndex = isChest && _playerCharacterModel.IsChestRobe() ? equip.Material - 7 : equip.Material - 1;

                            if (alternateSkins.Any() && alternateSkins.Count() > materialIndex && alternateSkins.ElementAt(materialIndex) != null)
                            {
                                material = alternateSkins[materialIndex];
                            }
                        }

                        baseColor = equip.Color;
                    }
                }
                var materialName = GetMaterialName(material);

                if (ShouldSkipMeshGenerationForMaterial(materialName))
                {
                    polygonIndex += materialGroup.PolygonCount;
                    continue;
                }

                if (!Materials.TryGetValue(materialName, out var gltfMaterial))
                {
                    gltfMaterial = Materials[MaterialBlankName];
                }

                if (baseColor != null)
                {
                    gltfMaterial = gltfMaterial.WithBaseColor(baseColor.Value.ToVector4());
                }

                var primitive = gltfMesh.UsePrimitive(gltfMaterial);
                for (var i = 0; i < materialGroup.PolygonCount; ++i)
                {
                    IDictionary<VertexPositionNormal, int> triangleGtlfVpToWldVi;
                    if (!canExportVertexColors && !isSkinned)
                    {
                        triangleGtlfVpToWldVi = AddTriangleToMesh<VertexPositionNormal, VertexTexture1, VertexEmpty>
                            (primitive, meshHelper, polygonIndex++, canExportVertexColors, isSkinned, singularBoneIndex, usesMobPieces, objectInstance);
                    }
                    else if (!canExportVertexColors && isSkinned)
                    {
                        triangleGtlfVpToWldVi = AddTriangleToMesh<VertexPositionNormal, VertexTexture1, VertexJoints4>
                            (primitive, meshHelper, polygonIndex++, canExportVertexColors, isSkinned, singularBoneIndex, usesMobPieces, objectInstance);
                    }
                    else if (canExportVertexColors && !isSkinned)
                    {
                        triangleGtlfVpToWldVi = AddTriangleToMesh<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>
                            (primitive, meshHelper, polygonIndex++, canExportVertexColors, isSkinned, singularBoneIndex, usesMobPieces, objectInstance);
                    }
                    else //(canExportVertexColors && isSkinned)
                    {
                        triangleGtlfVpToWldVi = AddTriangleToMesh<VertexPositionNormal, VertexColor1Texture1, VertexJoints4>
                            (primitive, meshHelper, polygonIndex++, canExportVertexColors, isSkinned, singularBoneIndex, usesMobPieces, objectInstance);
                    }
                    triangleGtlfVpToWldVi.ToList().ForEach(kv => gltfVertexPositionToWldVertexIndex[kv.Key] = kv.Value);
                }

                meshHelper.Reset();
            }

            if (generationMode == ModelGenerationMode.Separate)
            {
                if (mesh.AnimatedVerticesReference != null)
                {
                    AddAnimatedMeshMorphTargets(mesh, gltfMesh, meshName, transformMatrix, gltfVertexPositionToWldVertexIndex);
                    // mesh added to scene in ^ method
                }
                else
                {
                    _scene.AddRigidMesh(gltfMesh, transformMatrix);
                    _sharedMeshes[meshName] = gltfMesh;
                }              
            }
        }

        public void ApplyAnimationToSkeleton(SkeletonHierarchy skeleton, string animationKey, bool isCharacterAnimation, bool staticPose, int? instanceIndex = null)
        {
            if (isCharacterAnimation && !staticPose && animationKey == DefaultModelPoseAnimationKey) return;

            var skeletonName = GetSkeletonName(skeleton, instanceIndex);
            if (!_skeletons.TryGetValue(skeletonName, out var skeletonNodes))
            {
                skeletonNodes = AddNewSkeleton(skeleton);
            }
            var animation = skeleton.Animations[animationKey];
            var trackArray = isCharacterAnimation ? animation.TracksCleanedStripped : animation.TracksCleaned;
            var poseArray = isCharacterAnimation
                ? skeleton.Animations[DefaultModelPoseAnimationKey].TracksCleanedStripped
                : skeleton.Animations[DefaultModelPoseAnimationKey].TracksCleaned;
            
            if (poseArray == null) return;
            var hasChildren = _skeletonChildrenAttachBones.TryGetValue(skeleton.ModelBase, out var skeletonChildrenAttachBones);
            for (var i = 0; i < skeleton.Skeleton.Count; i++)
            {
                var boneName = isCharacterAnimation
                    ? Animation.CleanBoneAndStripBase(skeleton.BoneMapping[i], skeleton.ModelBase)
                    : Animation.CleanBoneName(skeleton.BoneMapping[i]);

                if (staticPose || !trackArray.ContainsKey(boneName))
                {
                    if (!poseArray.ContainsKey(boneName)) return;

                    var poseTransform = poseArray[boneName].TrackDefFragment.Frames[0];
                    if (poseTransform == null) return;

                    ApplyBoneTransformation(skeletonNodes[i], poseTransform, animationKey, 0, staticPose);
                    if (hasChildren && skeletonChildrenAttachBones.Where(c => c.Item2 == boneName).Any())
                    {
                        foreach (var child in skeletonChildrenAttachBones.Where(c => c.Item2 == boneName))
                        {
                            var childSkeleton = _skeletons[child.Item1];
                            foreach (var childBone in childSkeleton)
                            {
                                ApplyBoneTransformation(childBone, poseTransform, animationKey, 0, staticPose);
                            }
                        }
                    }
                    continue;
                }

                var totalTimeForBone = 0;
                for (var frame = 0; frame < animation.FrameCount; frame++)
                {
                    if (frame >= trackArray[boneName].TrackDefFragment.Frames.Count) break;

                    var boneTransform = trackArray[boneName].TrackDefFragment.Frames[frame];

                    ApplyBoneTransformation(skeletonNodes[i], boneTransform, animationKey, totalTimeForBone, staticPose);
                    if (frame == 0 && LoopedAnimationKeys.Contains(animationKey))
                    {
                        ApplyBoneTransformation(skeletonNodes[i], boneTransform, animationKey, animation.AnimationTimeMs, staticPose);
                    }
                    if (hasChildren && skeletonChildrenAttachBones.Where(c => c.Item2 == boneName).Any())
                    {
                        foreach (var child in skeletonChildrenAttachBones.Where(c => c.Item2 == boneName))
                        {
                            var childSkeleton = _skeletons[child.Item1];
                            ApplyBoneTransformation(childSkeleton[0], boneTransform, animationKey, 0, staticPose);
                        }
                    }

                    totalTimeForBone += isCharacterAnimation ?
                        (animation.AnimationTimeMs / animation.FrameCount) :
                        skeleton.Skeleton[i].Track.FrameMs;
                }
            }
        }

        public void AddLightInstances(IEnumerable<LightInstance> lightInstances, float lightIntensityMultiplier)
        {
            var groupedLightInstances = lightInstances.GroupBy(i => new UniqueLight(i));
            _lightGroups = groupedLightInstances;
            _lightIntensityMultiplier = lightIntensityMultiplier;
			/* https://github.com/vpenades/SharpGLTF/issues/168
            foreach (var uniqueLightGroups in groupedLightInstances)
            {
                var uniqueLight = uniqueLightGroups.Key;
                var light = new LightBuilder.Point()
                {
                    Color = uniqueLight.Color,
                    Intensity = uniqueLight.Radius * lightIntensityMultiplier
                    // Range = uniqueLight.Radius * ZoneScaleMultiplier
                };
                foreach (var lightInstance in uniqueLightGroups)
                {
                    var position = lightInstance.Position.ToVector3(swapYandZ: true);
                    var translationMatrix = Matrix4x4.CreateTranslation(position) * CorrectedWorldMatrix;
                    Matrix4x4.Decompose(translationMatrix, out _, out _, out var translation);
                    _scene.AddLight(light, new AffineTransform(Quaternion.Identity, translation));
                }
            }
            */
		}

		// https://github.com/vpenades/SharpGLTF/issues/168
		private void AddLightInstancesWorkaround(SharpGLTF.Schema2.ModelRoot modelRoot)
        {
			foreach (var uniqueLightGroups in _lightGroups)
			{
				var uniqueLight = uniqueLightGroups.Key;
                var light = modelRoot.CreatePunctualLight(SharpGLTF.Schema2.PunctualLightType.Point);
                light.Color = uniqueLight.Color;
                light.Intensity = uniqueLight.Radius * _lightIntensityMultiplier;
                // light.Range = uniqueLight.Radius * ZoneScaleMultiplier;

				foreach (var lightInstance in uniqueLightGroups)
				{
					var position = lightInstance.Position.ToVector3(swapYandZ: true);
					var translationMatrix = Matrix4x4.CreateTranslation(position) * CorrectedWorldMatrix;
					Matrix4x4.Decompose(translationMatrix, out _, out _, out var translation);
                    var lightName = lightInstance.LightReference?.LightSource?.Name;
                    var node = modelRoot.DefaultScene.CreateNode(lightName != null ? lightName.Split('_')[0] : "");
                    // node.WithTranslation(translation) makes VS complain for some reason
                    node.LocalTransform = node.LocalTransform.WithTranslation(translation);
                    node.PunctualLight = light;
				}
			}
		}

        public void AddCombinedMeshToScene(
            bool isZoneMesh = false, 
            string meshName = null, 
            string skeletonModelBase = null, 
            ObjInstance objectInstance = null,
            int? instanceIndex = null)
        {
            IMeshBuilder<MaterialBuilder> combinedMesh;
            if (meshName != null && _sharedMeshes.TryGetValue(meshName, out var existingMesh))
            {
                combinedMesh = existingMesh;
            }
            else
            {
                combinedMesh = _combinedMeshBuilder;
            }
            if (combinedMesh == null) return;

			var skeletonName = GetSkeletonName(skeletonModelBase, instanceIndex);
			
            var worldTransformMatrix = Matrix4x4.Identity;
            if (objectInstance != null && skeletonName == null)
            {
                worldTransformMatrix *= CreateTransformMatrixForObjectInstance(objectInstance);
                worldTransformMatrix *= CorrectedWorldMatrix;
            }
            else if (isZoneMesh)
            {
                worldTransformMatrix *= CorrectedWorldMatrix;
            }
            else
            {
                worldTransformMatrix *= CorrectedSingularActorMatrix;
            }

            if (skeletonName == null || !_skeletons.TryGetValue(skeletonName, out var skeletonNodes))
            {
                _scene.AddRigidMesh(combinedMesh, worldTransformMatrix);
            }
            else
            {
                if (_skeletonChildrenAttachBones.TryGetValue(skeletonName, out var children))
                {
                    foreach (var child in children)
                    {
                        skeletonNodes.AddRange(_skeletons[child.Item1]);
                    }
                }
                if (objectInstance != null)
                {
                    worldTransformMatrix = skeletonNodes[0].Parent.WorldMatrix;
                }

                _scene.AddSkinnedMesh(combinedMesh, worldTransformMatrix, skeletonNodes.ToArray());       
            }

            if (meshName != null && !_sharedMeshes.ContainsKey(meshName))
            {
                _sharedMeshes.Add(meshName, combinedMesh);
            }
            _combinedMeshBuilder = null;
        }

        public override void WriteAssetToFile(string fileName)
        {
            WriteAssetToFile(fileName, false);
        }

        public void WriteAssetToFile(string fileName, bool useExistingImages, bool isZone = false, string skeletonModelBase = null, bool cleanupTexturesFolder = false)
        {
            AddCombinedMeshToScene(false, null, skeletonModelBase);

			var outputFilePath = FixFilePath(fileName);
            var model = _scene.ToGltf2();

            if (_lightGroups != null && _lightGroups.Any())
            {
                AddLightInstancesWorkaround(model);
            }
            if (_exportFormat == GltfExportFormat.GlTF)
            {
                if (!useExistingImages)
                {
                    // Don't rename the image files
                    var writeSettings = new SharpGLTF.Schema2.WriteSettings()
                    {
                        JsonIndented = true,
                        ImageWriteCallback = (context, uri, image) =>
                        {
                            var imageSourcePath = image.SourcePath;
                            var imageFileName = Path.GetFileName(imageSourcePath);
                            // Save image to same path as the .gltf
                            var newImagePath = Path.Combine(Path.GetDirectoryName(fileName), imageFileName);
                            image.SaveToFile(newImagePath);

                            return imageFileName;
                        }
                    };
                    model.SaveGLTF(outputFilePath, writeSettings);
                }
                else
                {
                    var writeSettings = new SharpGLTF.Schema2.WriteSettings()
                    {
                        JsonIndented = true,
                        ImageWriteCallback = (context, uri, image) =>
                        {
                            var imageSourcePath = image.SourcePath;
                            return $"Textures/{Path.GetFileName(imageSourcePath)}";
                        }
                    };

                    model.SaveGLTF(outputFilePath, writeSettings);
                }
            }
            else // Glb
            {
                model.SaveGLB(outputFilePath);
            }
            if (cleanupTexturesFolder)
            {
                var outputFolder = Path.GetDirectoryName(outputFilePath);
                Directory.Delete(Path.Combine(outputFolder, "Textures"), true);
            }
        }

		public List<NodeBuilder> AddNewSkeleton(SkeletonHierarchy skeleton, string parent = null, string attachBoneName = null, ObjInstance objInstance = null, int? instanceIndex = null)
		{
			var skeletonNodes = new List<NodeBuilder>();
			var duplicateNameDictionary = new Dictionary<string, int>();
			var skeletonName = GetSkeletonName(skeleton, instanceIndex);
			var boneNamePrefix = instanceIndex != null ? $"{skeletonName}_" : "";
			foreach (var bone in skeleton.Skeleton)
			{
				var boneName = bone.CleanedName;
				if (duplicateNameDictionary.TryGetValue(boneName, out var count))
				{
					skeletonNodes.Add(new NodeBuilder($"{boneNamePrefix}{boneName}_{count:00}"));
					duplicateNameDictionary[boneName] = ++count;
				}
				else
				{
					skeletonNodes.Add(new NodeBuilder($"{boneNamePrefix}{boneName}"));
					duplicateNameDictionary.Add(boneName, 0);
				}
			}
			if (objInstance != null)
			{
				var rootNode = GetRootSkeletonNodeTransformsFromObjectInstance(skeletonName, objInstance);
				rootNode.AddNode(skeletonNodes[0]);
			}
			for (var i = 0; i < skeletonNodes.Count; i++)
			{
				var node = skeletonNodes[i];
				var bone = skeleton.Skeleton[i];
				bone.Children.ForEach(b => node.AddNode(skeletonNodes[b]));
			}
			if (parent != null && attachBoneName != null)
			{
				if (!_skeletons.TryGetValue(parent, out var parentSkeleton))
				{
					throw new InvalidOperationException($"Cannot attach child skeleton to parent: {parent}. It does not exist");
				}
				var attachBone = parentSkeleton
					.Where(n => n.Name.Equals(attachBoneName, StringComparison.InvariantCultureIgnoreCase))
					.SingleOrDefault();
				if (attachBone == null)
				{
					throw new InvalidOperationException($"Cannot attach child skeleton to parent: {parent} at bone {attachBoneName}. Bone does not exist");
				}
				attachBone.AddNode(skeletonNodes[0]);

				if (!_skeletonChildrenAttachBones.ContainsKey(parent))
				{
					_skeletonChildrenAttachBones.Add(parent, new List<(string, string)>());
				}
				_skeletonChildrenAttachBones[parent].Add((skeleton.ModelBase, attachBoneName));
			}
			_skeletons.Add(skeletonName, skeletonNodes);
			return skeletonNodes;
		}

		public override void ClearExportData()
        {
            _scene = null;
            _scene = new SceneBuilder();
            Materials.Clear();
            _sharedMeshes.Clear();
            _skeletons.Clear();
            _meshMaterialsToSkip.Clear();
        }

        public new int GetExportByteCount() => 0;

        private void CreateGltfMaterialFromEqMaterial(Material eqMaterial, string textureImageFolder)
        {
            var materialName = GetMaterialName(eqMaterial);

            if (Materials.ContainsKey(materialName)) return;

            if (eqMaterial.ShaderType == ShaderType.Boundary)
            {
                _meshMaterialsToSkip.Add(materialName);
                return;
            }
            if (eqMaterial.ShaderType == ShaderType.Invisible)
            {
                Materials.Add(materialName, GetInvisibleMaterial());
                return;
            }

            var imageFileNameWithoutExtension = eqMaterial.GetFirstBitmapNameWithoutExtension();
            if (string.IsNullOrEmpty(imageFileNameWithoutExtension)) return;

            var imagePath = Path.Combine(textureImageFolder, eqMaterial.GetFirstBitmapExportFilename());

            if (!File.Exists(imagePath))
            {
                if (ImagesInZoneFoundInObj.Contains(imageFileNameWithoutExtension))
                {
                    _logger.LogWarning($"Zone material: {materialName} image {imageFileNameWithoutExtension}.png not availible until _obj is processed! Manual correction required.");
                }
                else
                {
                    _logger.LogError($"Material: {materialName} image not found at '{imagePath}'!");
                }
                Materials.Add(materialName, GetBlankMaterial(materialName));
            }

            ImageBuilder imageBuilder;
            if (ShaderTypesThatNeedAlphaAddedToImage.Contains(eqMaterial.ShaderType))
            {
                // Materials with these shaders need new images with an alpha channel included to look correct
                // Not a fan of having to write new images during the generation phase, but SharpGLTF
                // needs the image bytes, and if we want to keep the original images we need to use the
                // ImageWriteCallback, and within that callback we only have access to the path the image
                // was loaded from, and that can only be set by loading an image via a path. I can't
                // even write the images to a temp folder since I won't be able to get the correct Textures
                // folder path within the callback to write the image
                var convertedImagePath = ImageAlphaConverter.AddAlphaToImage(imagePath, eqMaterial.ShaderType);
                var newImageName = Path.GetFileNameWithoutExtension(convertedImagePath);
                imageBuilder = ImageBuilder.From(new MemoryImage(convertedImagePath), newImageName);
            }
            else
            {
                var imageName = Path.GetFileNameWithoutExtension(imagePath);
                imageBuilder = ImageBuilder.From(new MemoryImage(imagePath), imageName);
            }

            var gltfMaterial = new MaterialBuilder(materialName)
                .WithDoubleSide(false)
                .WithMetallicRoughnessShader()
                .WithChannelParam(KnownChannel.MetallicRoughness, KnownProperty.RoughnessFactor, MaterialRoughness)
                .WithChannelParam(KnownChannel.MetallicRoughness, KnownProperty.MetallicFactor, 0f)
                .WithChannelParam(KnownChannel.SpecularFactor, KnownProperty.SpecularFactor, 0f); // Helps models look better in some renderers.
            // If we use the method below, the image name is not retained
            //    .WithChannelImage(KnownChannel.BaseColor, $"{textureImageFolder}{eqMaterial.GetFirstBitmapExportFilename()}");
            gltfMaterial.UseChannel(KnownChannel.BaseColor)
                .UseTexture()
                .WithPrimaryImage(imageBuilder);

            switch (eqMaterial.ShaderType)
            {
                case ShaderType.TransparentMasked:
                    gltfMaterial.WithAlpha(AlphaMode.MASK, 0.5f);
                    break;
                case ShaderType.Transparent25:
                case ShaderType.Transparent50:
                case ShaderType.Transparent75:
                case ShaderType.TransparentAdditive:
                case ShaderType.TransparentAdditiveUnlit:
                case ShaderType.TransparentSkydome:
                case ShaderType.TransparentAdditiveUnlitSkydome:
                    gltfMaterial.WithAlpha(AlphaMode.BLEND);
                    break;
                default:
                    gltfMaterial.WithAlpha(AlphaMode.OPAQUE);
                    break;
            }

            if (eqMaterial.ShaderType == ShaderType.TransparentAdditiveUnlit ||
                eqMaterial.ShaderType == ShaderType.DiffuseSkydome ||
                eqMaterial.ShaderType == ShaderType.TransparentAdditiveUnlitSkydome)
            {
                gltfMaterial.WithUnlitShader();
            }

            Materials.Add(materialName, gltfMaterial);
        }
        private Matrix4x4 CreateTransformMatrixForObjectInstance(ObjInstance instance)
        {
            var transformMatrix = Matrix4x4.CreateScale(instance.Scale)
                * Matrix4x4.CreateFromYawPitchRoll(
                    (float)(instance.Rotation.Z * Math.PI)/180f,
                    (float)(instance.Rotation.X * Math.PI)/180f,
                    (float)(instance.Rotation.Y * Math.PI)/180f
                )
                * Matrix4x4.CreateTranslation(instance.Position);
            return transformMatrix;
        }

		private string GetSkeletonName(SkeletonHierarchy skeleton, int? instanceIndex = null)
        {
            if (skeleton == null) return null;

            return GetSkeletonName(skeleton.ModelBase, instanceIndex);
        }

		private string GetSkeletonName(string skeletonModelBase, int? instanceIndex = null)
        {
            if (skeletonModelBase == null) return null;

            if (instanceIndex != null)
            {
                return $"{skeletonModelBase}_{instanceIndex:000}";
			}
            return skeletonModelBase;
		}

		private NodeBuilder GetRootSkeletonNodeTransformsFromObjectInstance(string name, ObjInstance instance)
		{
            var rootNode = new NodeBuilder(name);
			var instanceTransformMatrix = CreateTransformMatrixForObjectInstance(instance);
            var zoneInstanceTransformMatrix = instanceTransformMatrix * CorrectedWorldMatrix;
            Matrix4x4.Decompose(zoneInstanceTransformMatrix, out var scale, out var rotation, out var translation);
			rotation = Quaternion.Normalize(rotation);
			rootNode.WithLocalScale(scale)
                .WithLocalRotation(rotation)
                .WithLocalTranslation(translation);

            return rootNode;
		}

		private string GetMaterialName(Material eqMaterial)
        {
            return $"{MaterialList.GetMaterialPrefix(eqMaterial.ShaderType)}{eqMaterial.GetFirstBitmapNameWithoutExtension()}";
        }

        private bool ShouldSkipMeshGenerationForMaterial(string materialName)
        {
            if (_meshMaterialsToSkip.Contains(materialName)) return true;

            if (_playerCharacterModel == null || !_playerCharacterModel.RequiresMeshModificationsForVeliousHelm()) return false;

            var raceGendersWithHelmMaterialName = new HashSet<string>() { "DAF", "ELF", "ERF", "HUF" };
            if (raceGendersWithHelmMaterialName.Contains(_playerCharacterModel.RaceGender) && materialName.Contains("helm"))
            {
                return true;
            }
            if (_playerCharacterModel.RaceGender == "BAF" && materialName.Contains("bamhe") &&
                (materialName.EndsWith("03") || materialName.EndsWith("05")))
            {
                return true;
            }
            if (_playerCharacterModel.RaceGender == "DAF" && materialName.Contains("dafhe00") && materialName.EndsWith("2"))
            {
                return true;
            }
            if (_playerCharacterModel.RaceGender == "ERM" && 
                (materialName.Contains("clkerm") || (materialName.Contains("clk") && materialName.EndsWith("06"))))
            {
                return true;
            }
            return false;
        }

        private MaterialBuilder GetBlankMaterial(string name = null)
        {
            var materialName = name ?? MaterialBlankName;
            return new MaterialBuilder(materialName)
                .WithDoubleSide(false)
                .WithMetallicRoughnessShader()
                .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, new Vector4(1, 1, 1, 1));
        }

        private MaterialBuilder GetInvisibleMaterial()
        {
            return new MaterialBuilder(MaterialInvisName)
                .WithDoubleSide(false)
                .WithMetallicRoughnessShader()
                .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, new Vector4(1, 1, 1, 0))
                .WithAlpha(AlphaMode.MASK);
        }

        private IMeshBuilder<MaterialBuilder> InstantiateMeshBuilder(string meshName, bool isSkinned = false, bool canExportVertexColors = false)
        {
            var meshBuilderType = typeof(MeshBuilder<,,>).MakeGenericType(
                typeof(VertexPositionNormal),
                canExportVertexColors ? typeof(VertexColor1Texture1) : typeof(VertexTexture1),
                isSkinned ? typeof(VertexJoints4) : typeof(VertexEmpty));

            return (IMeshBuilder<MaterialBuilder>) Activator.CreateInstance(meshBuilderType, meshName);
        }

        private IDictionary<VertexPositionNormal,int> AddTriangleToMesh<TvG, TvM, TvS>(
            IPrimitiveBuilder primitive, WldMeshHelper meshHelper,
            int polygonIndex, bool canExportVertexColors, bool isSkinned,
            int singularBoneIndex = -1, bool usesMobPieces = false, ObjInstance objectInstance = null)
                where TvG : struct, IVertexGeometry
                where TvM : struct, IVertexMaterial
                where TvS : struct, IVertexSkinning
        {
            var triangle = meshHelper.GetTriangle(polygonIndex);
            var vertexPositions = meshHelper.GetVertexPositions(triangle);
            var vertexNormals = meshHelper.GetVertexNormals(triangle);
            var vertexUvs = meshHelper.GetVertexUvs(triangle);
            var boneIndexes = meshHelper.GetBoneIndexes(triangle, isSkinned, usesMobPieces, singularBoneIndex);
            var vertexColors = meshHelper.GetVertexColorVectors(triangle, canExportVertexColors, objectInstance);

            var vertex0 = GetGltfVertex<TvG, TvM, TvS>(vertexPositions.v0, vertexNormals.v0, vertexUvs.v0, vertexColors.v0, isSkinned, boneIndexes.v0);
            var vertex1 = GetGltfVertex<TvG, TvM, TvS>(vertexPositions.v1, vertexNormals.v1, vertexUvs.v1, vertexColors.v1, isSkinned, boneIndexes.v1);
            var vertex2 = GetGltfVertex<TvG, TvM, TvS>(vertexPositions.v2, vertexNormals.v2, vertexUvs.v2, vertexColors.v2, isSkinned, boneIndexes.v2);
            if (isSkinned)
            {
                // Normals come out wrong for skinned models unless we add the triangle
                // vertices in reverse order
                primitive.AddTriangle(vertex2, vertex1, vertex0);
            }
            else
            {
                primitive.AddTriangle(vertex0, vertex1, vertex2);
            }


            var gltfVpToWldVi = new Dictionary<VertexPositionNormal, int>();

            gltfVpToWldVi[new VertexPositionNormal(vertexPositions.v0, vertexNormals.v0)] = triangle.Vertex1;
            gltfVpToWldVi[new VertexPositionNormal(vertexPositions.v1, vertexNormals.v1)] = triangle.Vertex2;
            gltfVpToWldVi[new VertexPositionNormal(vertexPositions.v2, vertexNormals.v2)] = triangle.Vertex3;

            return gltfVpToWldVi;
        }

        private IVertexBuilder GetGltfVertex<TvG, TvM, TvS>(
            Vector3 position, Vector3 normal, Vector2 uv, Vector4? color, bool isSkinned, int boneIndex)
                where TvG : struct, IVertexGeometry
                where TvM : struct, IVertexMaterial
                where TvS : struct, IVertexSkinning
        {
            var exportJoints = boneIndex > -1 && isSkinned;
            IVertexBuilder vertexBuilder;
            if (color == null && !exportJoints)
            {
                vertexBuilder = new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>
                    ((position, normal), uv);
            }
            else if (color == null && exportJoints)
            {
                vertexBuilder = new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexJoints4>
                    ((position, normal), uv, new VertexJoints4(boneIndex));
            }
            else if (color != null && !exportJoints)
            {
                vertexBuilder = new VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>
                    ((position, normal), (color.Value, uv));
            }
            else // (color != null && exportJoints)
            {
                vertexBuilder = new VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexJoints4>
                    ((position, normal), (color.Value, uv), new VertexJoints4(boneIndex));
            }

            return (VertexBuilder<TvG, TvM, TvS>)vertexBuilder;
        }

        private void AddAnimatedMeshMorphTargets(Mesh mesh, IMeshBuilder<MaterialBuilder> gltfMesh,
            string meshName, Matrix4x4 transformMatrix, Dictionary<VertexPositionNormal, int> gltfVertexPositionToWldVertexIndex)
        {
            var frameTimes = new List<float>();
            var weights = new List<float>();
            var frameDelay = mesh.AnimatedVerticesReference.MeshAnimatedVertices.Delay/1000f;

            for (var frame = 0; frame < mesh.AnimatedVerticesReference.MeshAnimatedVertices.Frames.Count; frame++)
            {
                var vertexPositionsForFrame = mesh.AnimatedVerticesReference.MeshAnimatedVertices.Frames[frame];
                var morphTarget = gltfMesh.UseMorphTarget(frame);

                foreach (var vertexGeometry in gltfVertexPositionToWldVertexIndex.Keys)
                {
                    var wldVertexPositionForFrame = vertexPositionsForFrame[gltfVertexPositionToWldVertexIndex[vertexGeometry]];
                    var newPosition = (wldVertexPositionForFrame + mesh.Center).ToVector3(true);
                    vertexGeometry.TryGetNormal(out var originalNormal);
                    morphTarget.SetVertex(vertexGeometry, new VertexPositionNormal(newPosition, originalNormal));
                }
                frameTimes.Add(frame * frameDelay);
                weights.Add(1);
            }

            var node = new NodeBuilder(meshName);
            node.LocalTransform =  transformMatrix;

            var instance = _scene.AddRigidMesh(gltfMesh, node);
            instance.Content.UseMorphing().SetValue(weights.ToArray());
            var track = instance.Content.UseMorphing("Default");
            var morphTargetElements = new float[frameTimes.Count];

            for (var i = 0; i < frameTimes.Count; i++)
            {
                Array.Clear(morphTargetElements, 0, morphTargetElements.Length);
                morphTargetElements[i] = 1;
                track.SetPoint(frameTimes[i], true, morphTargetElements);
            }
        }

        private void ApplyBoneTransformation(NodeBuilder boneNode, DataTypes.BoneTransform boneTransform, 
            string animationKey, int timeMs, bool staticPose)
        {
            var scaleVector = new Vector3(boneTransform.Scale);
            var rotationQuaternion = new Quaternion()
            {
                X = (float)(boneTransform.Rotation.x * Math.PI)/180,
                Y = (float)(boneTransform.Rotation.z * Math.PI)/180,
                Z = (float)(boneTransform.Rotation.y * Math.PI * -1)/180,
                W = (float)(boneTransform.Rotation.w * Math.PI)/180
            };
            rotationQuaternion = Quaternion.Normalize(rotationQuaternion);
            var translationVector = boneTransform.Translation.ToVector3(true);
            translationVector.Z = -translationVector.Z;
            if (!AnimationDescriptions.TryGetValue(animationKey, out var animationDescription))
            {
                animationDescription = animationKey;
            }

            if (staticPose)
            {
                boneNode
                    .WithLocalScale(scaleVector)
                    .WithLocalRotation(rotationQuaternion)
                    .WithLocalTranslation(translationVector);
            }
            else
            {
                boneNode
                    .UseScale(animationDescription)
                    .WithPoint(timeMs/1000f, scaleVector);
                boneNode
                    .UseRotation(animationDescription)
                    .WithPoint(timeMs/1000f, rotationQuaternion);
                boneNode
                    .UseTranslation(animationDescription)
                    .WithPoint(timeMs/1000f, translationVector);
            }
        }

        private string FixFilePath(string filePath)
        {
            var fixedExtension = _exportFormat == GltfExportFormat.GlTF ? ".gltf" : ".glb";
            return Path.ChangeExtension(filePath, fixedExtension);
        }
    }

    public class ObjInstance
    {
        public string Name { get; private set; }
        public ObjType Type { get; private set; }
        public Vector3 Position { get; private set; }
        public Vector3 Rotation { get; private set; }
        public Vector3 Scale { get; private set; }
        public VertexColors Colors { get; private set; }

        public ObjInstance(ObjectInstance objectInstanceFragment)
        {
            Name = objectInstanceFragment.ObjectName;
            Position = objectInstanceFragment.Position.ToVector3(true);
            Rotation = objectInstanceFragment.Rotation.ToVector3();
            Scale = objectInstanceFragment.Scale.ToVector3();
            Colors = objectInstanceFragment.Colors;
            Type = ObjType.ZoneInstance;
        }

        public ObjInstance(Door door)
        {
            Name = door.Name;
            Position = new Vector3(
                door.Position.Y,
                door.Position.Z,
                door.Position.X
            );
            Rotation = new Vector3(0f, door.Incline * 360f / 512f, -(float)(door.Heading * 360d / 512d));
            Scale = new Vector3(1f);

            Type = ObjType.Door;
        }

        public enum ObjType
        {
            ZoneInstance = 0,
            Door = 1
        }
    }

    public class WldMeshHelper
    {
        private readonly Mesh _wldMesh;
        private readonly bool _separateTwoFacedTriangles;
        private readonly ISet<DataTypes.Polygon> _uniqueTriangles;
        private readonly IDictionary<int, Vector3> _wldVertexIndexToDuplicatedVertexNormals;
        private readonly TriangleVertexSetComparer _triangleSetComparer;

		private static readonly Vector4 DefaultVertexColor = new Vector4(0f, 0f, 0f, 1f); // Black

		public WldMeshHelper(Mesh wldMesh, bool separateTwoFacedTriangles)
        {
            _wldMesh = wldMesh;
            _separateTwoFacedTriangles = separateTwoFacedTriangles;
            _triangleSetComparer = new TriangleVertexSetComparer();
            _uniqueTriangles = new HashSet<DataTypes.Polygon>(_triangleSetComparer);
            _wldVertexIndexToDuplicatedVertexNormals = new Dictionary<int, Vector3>();
        }

        public DataTypes.Polygon GetTriangle(int triangleIndex)
        {
            return _wldMesh.Indices[triangleIndex];
        }

        public (Vector3 v0, Vector3 v1, Vector3 v2) GetVertexPositions(DataTypes.Polygon triangle)
        {
			(Vector3 v0, Vector3 v1, Vector3 v2) vertexPositions = (
			(_wldMesh.Vertices[triangle.Vertex1] + _wldMesh.Center).ToVector3(true),
			(_wldMesh.Vertices[triangle.Vertex2] + _wldMesh.Center).ToVector3(true),
			(_wldMesh.Vertices[triangle.Vertex3] + _wldMesh.Center).ToVector3(true));

            return vertexPositions;
		}

        public (Vector3 v0, Vector3 v1, Vector3 v2) GetVertexNormals(DataTypes.Polygon triangle)
        {
			if (_separateTwoFacedTriangles)
			{
				if (!_uniqueTriangles.Contains(triangle, _triangleSetComparer))
				{
					_uniqueTriangles.Add(triangle);
				}
				else
				{
                    return GetDuplicateVertexNormalsForTriangle(triangle);
				}
			}

			(Vector3 v0, Vector3 v1, Vector3 v2) vertexNormals = (
			    Vector3.Normalize(-_wldMesh.Normals[triangle.Vertex1].ToVector3()),
	            Vector3.Normalize(-_wldMesh.Normals[triangle.Vertex2].ToVector3()),
	            Vector3.Normalize(-_wldMesh.Normals[triangle.Vertex3].ToVector3()));

            return vertexNormals;
		}

        public (Vector2 v0, Vector2 v1, Vector2 v2) GetVertexUvs(DataTypes.Polygon triangle)
        {
			(Vector2 v0, Vector2 v1, Vector2 v2) vertexUvs = (
			    _wldMesh.TextureUvCoordinates[triangle.Vertex1].ToVector2(true),
				_wldMesh.TextureUvCoordinates[triangle.Vertex2].ToVector2(true),
				_wldMesh.TextureUvCoordinates[triangle.Vertex3].ToVector2(true));

            return vertexUvs;
		}

        public (int v0, int v1, int v2) GetBoneIndexes(DataTypes.Polygon triangle, bool isSkinned, bool usesMobPieces, int singularBoneIndex)
        {
			(int v0, int v1, int v2) boneIndexes = (singularBoneIndex, singularBoneIndex, singularBoneIndex);
			if (isSkinned && (usesMobPieces || singularBoneIndex == -1))
			{
				var boneOffset = singularBoneIndex == -1 ? 0 : singularBoneIndex;
				boneIndexes = (
				    GetBoneIndexForVertex(triangle.Vertex1) + boneOffset,
				    GetBoneIndexForVertex(triangle.Vertex2) + boneOffset,
					GetBoneIndexForVertex(triangle.Vertex3) + boneOffset);
			}

            return boneIndexes;
		}

		public (Vector4? v0, Vector4? v1, Vector4? v2) GetVertexColorVectors(DataTypes.Polygon triangle, bool canExportVertexColors, ObjInstance objectInstance = null)
		{
			if (!canExportVertexColors) return (null, null, null);

			var objInstanceColors = objectInstance?.Colors?.Colors ?? new List<WldColor>();
			var meshColors = _wldMesh?.Colors ?? new List<WldColor>();

			var v0Color = CoalesceVertexColor(meshColors, objInstanceColors, triangle.Vertex1);
			var v1Color = CoalesceVertexColor(meshColors, objInstanceColors, triangle.Vertex2);
			var v2Color = CoalesceVertexColor(meshColors, objInstanceColors, triangle.Vertex3);

			return (v0Color, v1Color, v2Color);
		}

        public void Reset()
        {
            _uniqueTriangles.Clear();
            _wldVertexIndexToDuplicatedVertexNormals.Clear();
        }

		private (Vector3 v0, Vector3 v1, Vector3 v2) GetDuplicateVertexNormalsForTriangle(DataTypes.Polygon triangle)
        {
            if (!_wldVertexIndexToDuplicatedVertexNormals.TryGetValue(triangle.Vertex1, out var v0Normal))
            {
                v0Normal = Vector3.Normalize(_wldMesh.Normals[triangle.Vertex1].ToVector3());
            }
			if (!_wldVertexIndexToDuplicatedVertexNormals.TryGetValue(triangle.Vertex2, out var v1Normal))
			{
				v1Normal = Vector3.Normalize(_wldMesh.Normals[triangle.Vertex2].ToVector3());
			}
			if (!_wldVertexIndexToDuplicatedVertexNormals.TryGetValue(triangle.Vertex3, out var v2Normal))
			{
				v2Normal = Vector3.Normalize(_wldMesh.Normals[triangle.Vertex3].ToVector3());
			}

            return (v0Normal, v1Normal, v2Normal);
		}

		private int GetBoneIndexForVertex(int vertexIndex)
		{
			foreach (var indexedMobVertexPiece in _wldMesh.MobPieces)
			{
				if (vertexIndex >= indexedMobVertexPiece.Value.Start &&
					vertexIndex < indexedMobVertexPiece.Value.Start + indexedMobVertexPiece.Value.Count)
				{
					return indexedMobVertexPiece.Key;
				}
			}
			return 0;
		}

		private Vector4 CoalesceVertexColor(List<WldColor> meshColors, List<WldColor> objInstanceColors, int vertexIndex)
		{
			if (vertexIndex < objInstanceColors.Count)
			{
				return objInstanceColors[vertexIndex].ToVector4();
			}
			else if (vertexIndex < meshColors.Count)
			{
				return meshColors[vertexIndex].ToVector4();
			}
			else
			{
				return DefaultVertexColor;
			}
		}
	}

	public class TriangleVertexSetComparer : IEqualityComparer<DataTypes.Polygon>
	{
		public bool Equals(DataTypes.Polygon polyX, DataTypes.Polygon polyY)
		{
			var polyXSet = new HashSet<int>() { polyX.Vertex1, polyX.Vertex2, polyX.Vertex3 };
			var polyYSet = new HashSet<int>() { polyY.Vertex1, polyY.Vertex2, polyY.Vertex3 };

            return polyXSet.SetEquals(polyYSet);
		}

		public int GetHashCode(DataTypes.Polygon poly)
		{
            var polyVertList1 = new List<int>() { poly.Vertex1, poly.Vertex2, poly.Vertex3 };
            polyVertList1.Sort();

			unchecked
            {
                return 391 + polyVertList1.GetHashCode();
            }
		}
	}

    public struct UniqueLight
    {
        public float Radius { get; private set; }
        public Vector3 Color { get; private set; }
        public UniqueLight(LightInstance lightInstance)
        {
            Radius = lightInstance.Radius;
            var color = lightInstance.LightReference?.LightSource?.Color;
            if (color != null)
            {
                Color = new Vector3(color.Value.r, color.Value.g, color.Value.b);
            }
            else
            {
                Color = new Vector3(1, 1, 1);
            }
        }
    }
	static class ImageAlphaConverter
    {
        public static string AddAlphaToImage(string filePath, ShaderType shaderType)
        {
            var suffix = $"_{MaterialList.GetMaterialPrefix(shaderType).TrimEnd('_')}";
            var newFileName = $"{Path.GetFileNameWithoutExtension(filePath)}{suffix}{Path.GetExtension(filePath)}";
            var newFilePath = Path.Combine(Path.GetDirectoryName(filePath), newFileName);

            if (File.Exists(newFilePath)) return newFilePath;

            using (var originalImage = new Bitmap(filePath))
            using (var newImage = originalImage.Clone(
                new Rectangle(0, 0, originalImage.Width, originalImage.Height),
                PixelFormat.Format32bppArgb))
            {
                for (int i = 0; i < originalImage.Width; i++)
                {
                    for (int j = 0; j < originalImage.Height; j++)
                    {
                        var pixelColor = originalImage.GetPixel(i, j);
                        switch (shaderType)
                        {
                            case ShaderType.Transparent25:
                                newImage.SetPixel(i, j, Color.FromArgb(64, pixelColor));
                                break;
                            case ShaderType.Transparent50:
                            case ShaderType.TransparentSkydome:
                                newImage.SetPixel(i, j, Color.FromArgb(128, pixelColor));
                                break;
                            case ShaderType.Transparent75:
                            case ShaderType.TransparentAdditive:
                                newImage.SetPixel(i, j, Color.FromArgb(192, pixelColor));
                                break;
                            default:
                                var maxRgb = new[] { pixelColor.R, pixelColor.G, pixelColor.B }.Max();
                                var newAlpha = maxRgb <= FullAlphaToDoubleAlphaThreshold ? maxRgb :
                                    Math.Min(maxRgb + ((maxRgb - FullAlphaToDoubleAlphaThreshold) * 2), 255);
                                newImage.SetPixel(i, j, Color.FromArgb(newAlpha, pixelColor));
                                break;
                        }
                    }
                }
                newImage.Save(newFilePath, ImageFormat.Png);
                return newFilePath;
            }
        }

        private static int FullAlphaToDoubleAlphaThreshold = 64;
    }
    static class VectorConversionExtensionMethods
    {
        public static Vector2 ToVector2(this vec2 v2, bool negateY = false)
        {
            var y = negateY ? -v2.y : v2.y;
            return new Vector2(v2.x, y);
        }

        public static Vector3 ToVector3(this vec3 v3, bool swapYandZ = false)
        {
            if (swapYandZ)
            {
                return new Vector3(v3.x, v3.z, v3.y);
            }
            else
            {
                return new Vector3(v3.x, v3.y, v3.z);
            }
        }

        public static Vector4 ToVector4(this WldColor color)
        {
            return new Vector4(color.R, color.G, color.B, color.A);
        }

        public static Vector4 ToVector4(this Color color)
        {
            return new Vector4(color.R/255.0f, color.G/255.0f, color.B/255.0f, color.A/255.0f);
        }
    }


}
