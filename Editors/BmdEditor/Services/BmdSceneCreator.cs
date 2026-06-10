using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Bmd;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.Transforms;
using Shared.GameFormats.WsModel;
using GameWorld.Core.Components;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Serilog;
using Editors.BmdEditor.ViewModels;
using Shared.Core.Events;
using GameWorld.Core.Components.Selection;
using Microsoft.Xna.Framework.Graphics;

namespace Editors.BmdEditor.Services
{
    public class BmdSceneCreator
    {
        private readonly ILogger _logger = Serilog.Log.ForContext<BmdSceneCreator>();
        private readonly IPackFileService _packFileService;
        private readonly GameWorld.Core.Components.SceneManager _sceneManager;
        private readonly GameWorld.Core.SceneNodes.Rmv2ModelNodeLoader _rmv2ModelNodeLoader;
        private readonly ResourceLibrary _resourceLibrary;
        private readonly GameWorld.Core.Services.MeshBuilderService _meshBuilderService;
        private readonly BmdLoadDiagnostics _diagnostics = new();
        private readonly HashSet<string> _activeBmdReferences = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Rmv2LodNode?> _modelTemplateCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BmdFile?> _referencedBmdCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _missingModelPaths = new(StringComparer.OrdinalIgnoreCase);
        private int _modelCacheHits;
        private int _modelCacheMisses;
        private int _bmdCacheHits;
        private int _bmdCacheMisses;
        private readonly Stopwatch _sceneLoadStopwatch = new();

        // Store direct references between view models and scene nodes
        public readonly Dictionary<BmdElementViewModel, GameWorld.Core.SceneNodes.SceneNode> ComponentNodes = new();
        private GameWorld.Core.SceneNodes.SceneNode? _currentHighlightedNode;

        public BmdSceneCreator(
            IPackFileService packFileService,
            GameWorld.Core.Components.SceneManager sceneManager,
            GameWorld.Core.SceneNodes.Rmv2ModelNodeLoader rmv2ModelNodeLoader,
            ResourceLibrary resourceLibrary,
            GameWorld.Core.Services.MeshBuilderService meshBuilderService)
        {
            _packFileService = packFileService;
            _sceneManager = sceneManager;
            _rmv2ModelNodeLoader = rmv2ModelNodeLoader;
            _resourceLibrary = resourceLibrary;
            _meshBuilderService = meshBuilderService;
        }

        private GroupNode? _propsGroup;
        private GroupNode? _otherGroup;
        private Dictionary<string, string>? _modelPathsByName;
        private Dictionary<string, string>? _modelPathsByDirectory;

        public GroupNode GetPropsGroup() => _propsGroup!;
        public GroupNode GetOtherGroup() => _otherGroup!;

        public void CreateSceneFromBmd(BmdFile bmdFile, PackFile bmdPackFile)
        {
            _currentBmdPackFile = bmdPackFile;
            _modelPathsByName = null;
            _modelPathsByDirectory = null;
            _activeBmdReferences.Clear();
            _modelTemplateCache.Clear();
            _referencedBmdCache.Clear();
            _missingModelPaths.Clear();
            _modelCacheHits = 0;
            _modelCacheMisses = 0;
            _bmdCacheHits = 0;
            _bmdCacheMisses = 0;
            _sceneLoadStopwatch.Restart();
            _diagnostics.Begin(_packFileService.GetFullPath(bmdPackFile));
            _diagnostics.Write(
                $"Root counts: props={bmdFile.PropInfos.Count}, buildings={bmdFile.BattlefieldBuildings.Count}, " +
                $"bmd references={bmdFile.BmdInfos.Count}, capture locations={bmdFile.CaptureLocations.Count}");
            WriteTransformBounds("Root props", bmdFile.PropInfos.Select(x => x.Transform));
            WriteTransformBounds("Root buildings", bmdFile.BattlefieldBuildings.Select(x => x.Transform));
            WriteTransformBounds("Root BMD references", bmdFile.BmdInfos.Select(x => x.Transform));
            _logger.Information("Creating 3D scene from BMD file: {FileName}", bmdPackFile.Name);

            try
            {
                // Clear existing scene
                RunTimedPhase("clear previous scene", ClearScene);

                // Create scene structure
                var rootNode = _sceneManager.RootNode;
                var bmdRootNode = rootNode.AddObject(new GroupNode("BMD_Scene") { IsEditable = false });

                // Create groups for later reference
                _propsGroup = bmdRootNode.AddObject(new GroupNode("Props") { IsEditable = false });
                _otherGroup = bmdRootNode.AddObject(new GroupNode("Other") { IsEditable = false });
                RunTimedPhase("terrain", () => LoadTerrain(bmdPackFile, bmdRootNode));

                _logger.Information($"BMD scene structure created successfully");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to create BMD scene: {ex.Message}");
                throw;
            }
        }

        private PackFile? _currentBmdPackFile;

        public void LoadSceneContent(BmdFile bmdFile, ObservableCollection<BmdElementViewModel> allElements)
        {
            if (_propsGroup == null || _otherGroup == null)
                throw new InvalidOperationException("Scene groups not initialized. Call CreateSceneFromBmd first.");

            try
            {
                // Load props as RMV2 models
                RunTimedPhase(
                    "root props",
                    () => LoadProps(bmdFile, _propsGroup, allElements.OfType<PropInfoViewModel>().ToList()));

                // Load other components
                RunTimedPhase("root components", () => LoadOtherComponents(bmdFile, _otherGroup, allElements));

                // Try to load vegetation from a companion .bmd.vegetation file
                if (_currentBmdPackFile != null)
                {
                    RunTimedPhase("vegetation", () => TryLoadVegetation(_currentBmdPackFile, _otherGroup));
                    RunTimedPhase("procedural BMD", () => TryLoadProceduralBmd(_currentBmdPackFile, _otherGroup));
                }

                _logger.Information($"BMD scene content loaded successfully");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load BMD scene content: {ex.Message}");
                throw;
            }
            finally
            {
                var cacheSummary =
                    $"Load cache: RMV2 hits={_modelCacheHits}, misses={_modelCacheMisses}; " +
                    $"BMD hits={_bmdCacheHits}, misses={_bmdCacheMisses}";
                _logger.Information(cacheSummary);
                _diagnostics.Write(cacheSummary);
                WriteSceneTotals();
                _sceneLoadStopwatch.Stop();
                _diagnostics.Write($"Total scene load: {_sceneLoadStopwatch.Elapsed.TotalMilliseconds:0.0} ms");
                _diagnostics.Complete();
            }
        }

        private void RunTimedPhase(string name, Action action)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                action();
            }
            finally
            {
                stopwatch.Stop();
                _diagnostics.Write($"Phase {name}: {stopwatch.Elapsed.TotalMilliseconds:0.0} ms");
            }
        }

        private void WriteSceneTotals()
        {
            var nodeCount = 0;
            var drawableCount = 0;

            void CountNode(ISceneNode node)
            {
                nodeCount++;
                if (node is IDrawableItem)
                    drawableCount++;
                foreach (var child in node.Children)
                    CountNode(child);
            }

            CountNode(_sceneManager.RootNode);
            _diagnostics.Write($"Scene totals: nodes={nodeCount}, drawable nodes={drawableCount}");
        }

        private void ClearScene()
        {
            // Remove existing BMD scene nodes
            var existingBmdNodes = _sceneManager.RootNode.Children.Where(x => x.Name == "BMD_Scene").ToList();
            foreach (var node in existingBmdNodes)
            {
                _sceneManager.RootNode.Children.Remove(node);
            }
            
            // Clear node references
            ComponentNodes.Clear();
            _currentHighlightedNode = null;
        }

        private void WriteTransformBounds(string label, IEnumerable<Matrix> transforms)
        {
            var positions = transforms.Select(x => x.Translation).ToList();
            if (positions.Count == 0)
            {
                _diagnostics.Write($"{label}: none");
                return;
            }

            _diagnostics.Write(
                $"{label}: count={positions.Count}, " +
                $"X={positions.Min(x => x.X):0.###}..{positions.Max(x => x.X):0.###}, " +
                $"Y={positions.Min(x => x.Y):0.###}..{positions.Max(x => x.Y):0.###}, " +
                $"Z={positions.Min(x => x.Z):0.###}..{positions.Max(x => x.Z):0.###}");
        }

        private void LoadProps(BmdFile bmdFile, GroupNode propsGroup, List<PropInfoViewModel>? propViewModels)
        {
            _logger.Information($"Loading {bmdFile.PropInfos.Count} props");

            for (int i = 0; i < bmdFile.PropInfos.Count; i++)
            {
                var propInfo = bmdFile.PropInfos[i];
                var propPath = propInfo.Rmv2Path;

                try
                {
                    var node = LoadSingleProp(propPath, propInfo, propsGroup, i);
                    if (propViewModels != null && i < propViewModels.Count)
                        ComponentNodes[propViewModels[i]] = node;
                }
                catch (Exception ex)
                {
                    _logger.Warning("Failed to load prop at index {Index}: {Message}", i, ex.Message);
                }
            }
        }

        private SceneNode LoadSingleProp(string propPath, PropInfo propInfo, GroupNode propsGroup, int instanceIndex)
        {
            if (_missingModelPaths.Contains(propPath))
                return CreatePlaceholderProp(propPath, propInfo, propsGroup, instanceIndex, "File not found");

            var modelFile = _packFileService.FindFile(propPath);
            if (modelFile == null)
            {
                _missingModelPaths.Add(propPath);
                _logger.Warning("Prop model file not found: {Path}; further occurrences will be suppressed.", propPath);
                return CreatePlaceholderProp(propPath, propInfo, propsGroup, instanceIndex, "File not found");
            }

            try
            {
                // Handle wsmodel files by getting the actual rigid_model_v2 path
                var actualModelFile = modelFile;
                var actualModelPath = propPath;
                WsModelFile? wsModel = null;
                
                if (Path.GetExtension(propPath).ToLower() == ".wsmodel")
                {
                    wsModel = new WsModelFile(modelFile);
                    if (string.IsNullOrEmpty(wsModel.GeometryPath))
                    {
                        return CreatePlaceholderProp(propPath, propInfo, propsGroup, instanceIndex, "WsModel has no geometry path");
                    }

                    actualModelPath = wsModel.GeometryPath;
                    actualModelFile = _packFileService.FindFile(actualModelPath);
                    if (actualModelFile == null)
                    {
                        _missingModelPaths.Add(propPath);
                        _logger.Warning(
                            "Referenced prop model file not found: {Path}; further occurrences will be suppressed.",
                            actualModelPath);
                        return CreatePlaceholderProp(propPath, propInfo, propsGroup, instanceIndex, $"Referenced file not found: {actualModelPath}");
                    }
                }

                var modelFullPath = _packFileService.GetFullPath(actualModelFile);
                var cacheKey = Path.GetExtension(propPath).Equals(".wsmodel", StringComparison.OrdinalIgnoreCase)
                    ? propPath
                    : modelFullPath;
                var lodNode = CreateModelInstance(cacheKey, actualModelFile, modelFullPath, wsModel, propInfo.Transform);
                if (lodNode == null)
                {
                    return CreatePlaceholderProp(propPath, propInfo, propsGroup, instanceIndex, "No LOD nodes found");
                }

                // Create a simple group for the prop
                var propName = $"Prop_{instanceIndex}_{Path.GetFileNameWithoutExtension(propPath)}";
                var propGroup = propsGroup.AddObject(new GroupNode(propName) { IsEditable = false });
                propGroup.AddObject(lodNode);

                _logger.Debug($"Loaded prop: {propPath} at {propInfo.Transform.Translation}");
                return propGroup;
            }
            catch (Exception ex)
            {
                _logger.Warning("Error loading prop '{Path}': {Message}", propPath, ex.Message);
                return CreatePlaceholderProp(propPath, propInfo, propsGroup, instanceIndex, ex.Message);
            }
        }

        private SceneNode CreatePlaceholderProp(string propPath, PropInfo propInfo, GroupNode propsGroup, int instanceIndex, string reason)
        {
            var propName = $"Prop_{instanceIndex}_{Path.GetFileNameWithoutExtension(propPath)}";
            var propGroup = propsGroup.AddObject(new GroupNode(propName) { IsEditable = false });

            var placeholderMesh = CreatePlaceholderNode(() => new PropPlaceholderNode("Prop_Placeholder")
            {
                FailedModelPath = propPath
            }, propInfo.Transform);

            if (placeholderMesh != null)
                propGroup.AddObject(placeholderMesh);

            _diagnostics.FailedModel(propPath, reason);
            _logger.Debug($"Created placeholder prop: {propPath} ({reason}) at {propInfo.Transform.Translation}");
            return propGroup;
        }

        
        
        private void LoadOtherComponents(BmdFile bmdFile, GroupNode otherGroup, ObservableCollection<BmdElementViewModel>? allElements)
        {
            _logger.Information("Loading other BMD components");

            var componentLoaders = new (string Name, int Count, Action<GroupNode> Loader)[]
            {
                ("Point_Lights", bmdFile.PointLights.Count, group => LoadComponents(bmdFile.PointLights, group, "PointLight", CreatePointLightPlaceholderNode, allElements?.OfType<PointLightInfoViewModel>().ToList())),
                ("Spot_Lights", bmdFile.SpotLights.Count, group => LoadComponents(bmdFile.SpotLights, group, "SpotLight", CreateSpotLightPlaceholderNode, allElements?.OfType<SpotLightInfoViewModel>().ToList())),
                ("Sounds", bmdFile.Sounds.Count, group => LoadComponents(bmdFile.Sounds, group, "Sound", CreateSoundPlaceholderNode, allElements?.OfType<SoundInfoViewModel>().ToList())),
                ("VFX", bmdFile.VfxInfos.Count, group => LoadComponents(bmdFile.VfxInfos, group, "VFX", CreateVfxPlaceholderNode, allElements?.OfType<VfxInfoViewModel>().ToList())),
                ("CSC", bmdFile.CscInfos.Count, group => LoadComponents(bmdFile.CscInfos, group, "CSC", CreateCscPlaceholderNode, allElements?.OfType<CscInfoViewModel>().ToList())),
                ("Light_Probes", bmdFile.LightProbes.Count, group => LoadComponents(bmdFile.LightProbes, group, "LightProbe", CreateLightProbePlaceholderNode, allElements?.OfType<LightProbeInfoViewModel>().ToList())),
                ("Building_Projectile_Emitters", bmdFile.BuildingProjectileEmitters.Count, group => LoadComponents(bmdFile.BuildingProjectileEmitters, group, "BuildingProjectileEmitter", CreateBuildingProjectileEmitterPlaceholderNode, allElements?.OfType<BuildingProjectileEmitterViewModel>().ToList())),
                ("Terrain_Holes", bmdFile.TerrainHoles.Count, group => LoadComponents(bmdFile.TerrainHoles, group, "TerrainHole", CreateTerrainHoleNode, allElements?.OfType<TerrainHoleInfoViewModel>().ToList())),
                ("PolyMeshes", bmdFile.PolyMeshes.Count, group => LoadComponents(bmdFile.PolyMeshes, group, "PolyMesh", CreatePolyMeshNode, allElements?.OfType<PolyMeshInfoViewModel>().ToList())),
                ("Go_Outlines", bmdFile.GoOutlines.Count, group => LoadComponents(bmdFile.GoOutlines, group, "GoOutline", CreateGoOutlineNode, allElements?.OfType<GoOutlineViewModel>().ToList())),
                ("NonTerrain_Outlines", bmdFile.NonTerrainOutlines.Count, group => LoadComponents(bmdFile.NonTerrainOutlines, group, "NonTerrainOutline", CreateNonTerrainOutlineNode, allElements?.OfType<NonTerrainOutlineViewModel>().ToList())),
                ("Battlefield_Buildings", bmdFile.BattlefieldBuildings.Count, group => LoadComponents(bmdFile.BattlefieldBuildings, group, "BattlefieldBuilding", CreateBattlefieldBuildingPlaceholderNode, allElements?.OfType<BattlefieldBuildingViewModel>().ToList())),
                ("BMD_References", bmdFile.BmdInfos.Count, group => LoadComponents(bmdFile.BmdInfos, group, "BMD", CreateBmdInfoNode, allElements?.OfType<BmdInfoViewModel>().ToList())),
                ("Deployments", bmdFile.Deployments.Count, group => LoadDeploymentComponents(bmdFile.Deployments, group, allElements?.OfType<DeploymentViewModel>().ToList())),
                ("Capture_Locations", bmdFile.CaptureLocations.Count, group => LoadComponents(bmdFile.CaptureLocations, group, "CaptureLocation", CreateCaptureLocationNode, allElements?.OfType<CaptureLocationViewModel>().ToList())),
                ("Terrain_Outlines", bmdFile.TerrainOutlines.Count, group => LoadIndexedComponents(bmdFile.TerrainOutlines, group, "TerrainOutline", CreateTerrainOutlineNode)),
                ("Water_Outlines", bmdFile.WaterOutlines.Count, group => LoadIndexedComponents(bmdFile.WaterOutlines, group, "WaterOutline", CreateWaterOutlineNode)),
            };

            foreach (var (name, count, loader) in componentLoaders)
            {
                if (count > 0)
                {
                    _logger.Information($"Creating {name} group ({count} items)");
                    var group = otherGroup.AddObject(new GroupNode($"{name} ({count})") { IsEditable = false });
                    loader(group);
                }
            }
        }

        private void LoadComponents<T, TViewModel>(IList<T> components, GroupNode group, string prefix, Func<T, GroupNode, int, SceneNode> nodeCreator, IList<TViewModel>? viewModels)
            where TViewModel : BmdElementViewModel
        {
            _logger.Information($"Loading {components.Count} {prefix} components");

            for (int i = 0; i < components.Count; i++)
            {
                try
                {
                    var component = components[i];
                    var node = nodeCreator(component, group, i);
                    if (viewModels != null && i < viewModels.Count)
                        ComponentNodes[viewModels[i]] = node;
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to create {prefix} component at index {i}: {ex.Message}");
                }
            }
        }

        private SceneNode CreateVfxPlaceholderNode(VfxInfo vfxInfo, GroupNode vfxGroup, int index)
        {
            var vfxName = $"VFX_{index}_{Path.GetFileNameWithoutExtension(vfxInfo.VfxString)}";
            var vfxNode = vfxGroup.AddObject(new GroupNode(vfxName) { IsEditable = false });

            var placeholderMesh = CreatePlaceholderNode(() => new VfxPlaceholderNode("VFX_Placeholder"), vfxInfo.Transform);
            if (placeholderMesh != null)
            {
                vfxNode.AddObject(placeholderMesh);
            }

            return vfxNode;
        }

        private SceneNode CreateCscPlaceholderNode(CscInfo cscInfo, GroupNode cscGroup, int index)
        {
            var cscName = $"CSC_{index}_{Path.GetFileNameWithoutExtension(cscInfo.SceneFile)}";
            var cscNode = cscGroup.AddObject(new GroupNode(cscName) { IsEditable = false });

            var placeholderMesh = CreatePlaceholderNode(() => new CscPlaceholderNode("CSC_Placeholder"), cscInfo.Transform);
            if (placeholderMesh != null)
            {
                cscNode.AddObject(placeholderMesh);
            }

            return cscNode;
        }

        private SceneNode CreateLightProbePlaceholderNode(LightProbeInfo lightProbeInfo, GroupNode lightProbeGroup, int index)
        {
            var lightProbeName = $"LightProbe_{index}";
            var lightProbeNode = lightProbeGroup.AddObject(new GroupNode(lightProbeName) { IsEditable = false });

            var positionVector = new Vector3(lightProbeInfo.Position.X, lightProbeInfo.Position.Y, lightProbeInfo.Position.Z);
            var transform = Matrix.CreateTranslation(positionVector);
            var placeholderMesh = CreatePlaceholderNode(() => new LightProbePlaceholderNode("LightProbe_Placeholder")
            {
                OuterRadius = lightProbeInfo.OuterRadius,
                InnerRadius = lightProbeInfo.InnerRadius
            }, transform);
            if (placeholderMesh != null)
            {
                lightProbeNode.AddObject(placeholderMesh);
            }

            return lightProbeNode;
        }

        private SceneNode CreateBuildingProjectileEmitterPlaceholderNode(BuildingProjectileEmitter emitterInfo, GroupNode emitterGroup, int index)
        {
            var emitterName = $"BuildingProjectileEmitter_{index}";
            var emitterNode = emitterGroup.AddObject(new GroupNode(emitterName) { IsEditable = false });

            var positionVector = new Vector3(emitterInfo.Location.X, emitterInfo.Location.Y, emitterInfo.Location.Z);
            var transform = Matrix.CreateTranslation(positionVector);
            var placeholderMesh = CreatePlaceholderNode(() => new BuildingProjectileEmitterPlaceholderNode("BuildingProjectileEmitter_Placeholder"), transform);
            if (placeholderMesh != null)
            {
                emitterNode.AddObject(placeholderMesh);
            }

            return emitterNode;
        }

        private SceneNode? CreatePlaceholderNode(Func<SceneNode> nodeFactory, Matrix transform)
        {
            try
            {
                var node = nodeFactory();
                node.IsEditable = false;
                node.ModelMatrix = transform;
                return node;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to create placeholder node: {ex.Message}");
                return null;
            }
        }

        private SceneNode CreateTerrainHoleNode(TerrainHoleTriangleInfo terrainHole, GroupNode terrainHoleGroup, int index)
        {
            var holeName = $"TerrainHole_{index}";
            var holeNode = terrainHoleGroup.AddObject(new GroupNode(holeName) { IsEditable = false });

            var placeholderMesh = CreateSpecializedNode(() => new TerrainHoleEdgesNode("TerrainHole_Edges")
            {
                FirstVert = terrainHole.FirstVert,
                SecondVert = terrainHole.SecondVert,
                ThirdVert = terrainHole.ThirdVert
            });
            if (placeholderMesh != null)
            {
                holeNode.AddObject(placeholderMesh);
            }

            return holeNode;
        }

        private SceneNode CreatePolyMeshNode(PolyMeshInfo polyMesh, GroupNode polyMeshGroup, int index)
        {
            var meshName = $"PolyMesh_{index}_{Path.GetFileNameWithoutExtension(polyMesh.MaterialString)}";
            var meshNode = polyMeshGroup.AddObject(new GroupNode(meshName) { IsEditable = false });

            var placeholderMesh = CreateSpecializedNode(() => new PolyMeshTrianglesNode("PolyMesh_Triangles")
            {
                Vertices = polyMesh.VertexList,
                Triangles = polyMesh.TriangleList,
                MaterialString = polyMesh.MaterialString
            });
            if (placeholderMesh != null)
            {
                meshNode.AddObject(placeholderMesh);
            }

            return meshNode;
        }

        private SceneNode CreateBattlefieldBuildingPlaceholderNode(BattlefieldBuilding buildingInfo, GroupNode buildingsGroup, int index)
        {
            var buildingName = $"BattlefieldBuilding_{index}_{buildingInfo.BuildingId}";
            var buildingNode = buildingsGroup.AddObject(new GroupNode(buildingName) { IsEditable = false });

            var modelPath = FindModelPathByKey(buildingInfo.BuildingKey);
            if (modelPath != null)
            {
                var modelNode = TryLoadModel(modelPath, buildingInfo.Transform);
                if (modelNode != null)
                {
                    buildingNode.AddObject(modelNode);
                    return buildingNode;
                }
            }

            _diagnostics.UnresolvedBuilding(buildingInfo.BuildingKey);
            var placeholderMesh = CreatePlaceholderNode(() => new BattlefieldBuildingPlaceholderNode("BattlefieldBuilding_Placeholder"), buildingInfo.Transform);
            if (placeholderMesh != null)
            {
                buildingNode.AddObject(placeholderMesh);
            }

            return buildingNode;
        }

        private string? FindModelPathByKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            _modelPathsByName ??= _packFileService.FindAllWithExtention(".wsmodel")
                .Concat(_packFileService.FindAllWithExtention(".rigid_model_v2"))
                .GroupBy(x => Path.GetFileNameWithoutExtension(x.FileName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First().FileName, StringComparer.OrdinalIgnoreCase);

            if (_modelPathsByName.TryGetValue(key, out var exactPath))
                return exactPath;

            _modelPathsByDirectory ??= _packFileService.FindAllWithExtention(".wsmodel")
                .Concat(_packFileService.FindAllWithExtention(".rigid_model_v2"))
                .Select(x => x.FileName)
                .GroupBy(
                    path => Path.GetFileName(Path.GetDirectoryName(path.Replace('/', Path.DirectorySeparatorChar))),
                    StringComparer.OrdinalIgnoreCase)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(path => Path.GetFileNameWithoutExtension(path).Contains("_tech", StringComparison.OrdinalIgnoreCase))
                        .ThenByDescending(path => Path.GetFileNameWithoutExtension(path).Contains("piece01_destruct01", StringComparison.OrdinalIgnoreCase))
                        .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .First(),
                    StringComparer.OrdinalIgnoreCase);

            return _modelPathsByDirectory.GetValueOrDefault(key);
        }

        private SceneNode? TryLoadModel(string modelPath, Matrix transform)
        {
            var modelFile = _packFileService.FindFile(modelPath);
            if (modelFile == null)
                return null;

            try
            {
                var actualFile = modelFile;
                WsModelFile? wsModel = null;
                if (Path.GetExtension(modelPath).Equals(".wsmodel", StringComparison.OrdinalIgnoreCase))
                {
                    wsModel = new WsModelFile(modelFile);
                    actualFile = _packFileService.FindFile(wsModel.GeometryPath);
                    if (actualFile == null)
                        return null;
                }

                return CreateModelInstance(
                    modelPath,
                    actualFile,
                    _packFileService.GetFullPath(actualFile),
                    wsModel,
                    transform);
            }
            catch (Exception ex)
            {
                _logger.Debug("Failed to load model '{Path}': {Message}", modelPath, ex.Message);
                return null;
            }
        }

        private Rmv2LodNode? CreateModelInstance(
            string cacheKey,
            PackFile modelFile,
            string modelFullPath,
            WsModelFile? wsModel,
            Matrix transform)
        {
            if (!_modelTemplateCache.TryGetValue(cacheKey, out var template))
            {
                _modelCacheMisses++;
                _logger.Information("Loading RMV2 model: {Path}", modelFullPath);
                _diagnostics.Write($"RMV2 cache miss: {modelFullPath}");

                try
                {
                    var rmv = ModelFactory.Create().Load(modelFile.DataSource.ReadData(), modelFullPath);
                    template = _rmv2ModelNodeLoader
                        .CreateModelNodesFromFile(rmv, modelFullPath, true, wsModel)
                        .FirstOrDefault();
                }
                catch
                {
                    _modelTemplateCache[cacheKey] = null;
                    throw;
                }

                _modelTemplateCache[cacheKey] = template;
            }
            else
            {
                _modelCacheHits++;
            }

            if (template == null)
                return null;

            var instance = new Rmv2LodNode(template.Name, template.LodValue)
            {
                IsEditable = false,
                ModelMatrix = transform
            };

            foreach (var templateChild in template.Children.OfType<Rmv2MeshNode>())
            {
                var mesh = new Rmv2MeshNode(
                    templateChild.Geometry,
                    templateChild.RmvMaterial,
                    templateChild.Material,
                    null)
                {
                    IsEditable = false,
                    IsSelectable = false,
                    Position = templateChild.Position,
                    Orientation = templateChild.Orientation,
                    Scale = templateChild.Scale,
                    PivotPoint = templateChild.PivotPoint,
                    ScaleMult = templateChild.ScaleMult
                };
                instance.AddObject(mesh);
            }

            return instance;
        }

        private void LoadTerrain(PackFile bmdPackFile, GroupNode rootNode)
        {
            try
            {
                var terrain = BmdTerrainLoader.TryLoad(_packFileService, bmdPackFile, _diagnostics.Write);
                var terrainGroup = rootNode.AddObject(new GroupNode("Terrain") { IsEditable = false });

                if (terrain != null)
                {
                    terrainGroup.AddObject(new TerrainHeightMapNode($"Heightmap_{Path.GetFileName(terrain.SourcePath)}")
                    {
                        Heights = terrain.Heights,
                        WorldSize = terrain.WorldSize,
                        SampleStep = Math.Max(4, terrain.Width / 80),
                        IsEditable = false
                    });
                    _diagnostics.Write(
                        $"Terrain: {terrain.SourcePath}, {terrain.Width}x{terrain.Height}, " +
                        $"world size={terrain.WorldSize}, sample step={Math.Max(4, terrain.Width / 80)}");
                    _logger.Information("Loaded BMD terrain heightmap from {Path}", terrain.SourcePath);
                }
                else
                {
                    terrainGroup.AddObject(new TerrainHeightMapNode("Flat_Terrain_Fallback")
                    {
                        Heights = new float[2, 2],
                        WorldSize = 2048f,
                        SampleStep = 1,
                        IsEditable = false
                    });
                    _diagnostics.Write("Terrain: flat fallback (no matching TIFF or decoded compressed map)");
                    _logger.Warning("No matching TIFF heightmap was available; using flat terrain fallback.");
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Write($"Terrain load failed: {ex.Message}");
                _logger.Warning("Failed to load terrain: {Message}", ex.Message);
            }
        }

        private SceneNode? CreateSpecializedNode(Func<SceneNode> nodeFactory)
        {
            try
            {
                var node = nodeFactory();
                node.IsEditable = false;
                return node;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to create specialized node: {ex.Message}");
                return null;
            }
        }

        private SceneNode CreateGoOutlineNode(GoOutline outlineInfo, GroupNode goOutlineGroup, int index)
        {
            var outlineName = $"GoOutline_{index}";
            var outlineNode = goOutlineGroup.AddObject(new GroupNode(outlineName) { IsEditable = false });

            var placeholderMesh = CreateSpecializedNode(() => new GoOutlineNode("GoOutline_Placeholder")
            {
                VertexList = outlineInfo.VertexList
            });
            if (placeholderMesh != null)
            {
                outlineNode.AddObject(placeholderMesh);
            }

            return outlineNode;
        }

        private SceneNode CreateNonTerrainOutlineNode(NonTerrainOutline outlineInfo, GroupNode nonTerrainOutlineGroup, int index)
        {
            var outlineName = $"NonTerrainOutline_{index}";
            var outlineNode = nonTerrainOutlineGroup.AddObject(new GroupNode(outlineName) { IsEditable = false });

            var placeholderMesh = CreateSpecializedNode(() => new NonTerrainOutlineNode("NonTerrainOutline_Placeholder")
            {
                VertexList = outlineInfo.VertexList
            });
            if (placeholderMesh != null)
            {
                outlineNode.AddObject(placeholderMesh);
            }

            return outlineNode;
        }

        private SceneNode CreatePointLightPlaceholderNode(PointLightInfo pointLightInfo, GroupNode pointLightGroup, int index)
        {
            var pointLightName = $"PointLight_{index}";
            var pointLightNode = pointLightGroup.AddObject(new GroupNode(pointLightName) { IsEditable = false });

            var positionVector = new Vector3(pointLightInfo.Position.X, pointLightInfo.Position.Y, pointLightInfo.Position.Z);
            var transform = Matrix.CreateTranslation(positionVector);
            var placeholderMesh = CreateSpecializedNode(() => new PointLightSphereNode("PointLight_Sphere")
            {
                Radius = pointLightInfo.Radius
            });
            if (placeholderMesh != null)
            {
                placeholderMesh.ModelMatrix = transform;
                pointLightNode.AddObject(placeholderMesh);
            }

            return pointLightNode;
        }

        private SceneNode CreateSpotLightPlaceholderNode(SpotLightInfo spotLightInfo, GroupNode spotLightGroup, int index)
        {
            var spotLightName = $"SpotLight_{index}";
            var spotLightNode = spotLightGroup.AddObject(new GroupNode(spotLightName) { IsEditable = false });

            var placeholderMesh = CreateSpecializedNode(() => new SpotLightConeNode("SpotLight_Cone")
            {
                Length = spotLightInfo.Length,
                InnerAngle = spotLightInfo.InnerAngleRadians,
                OuterAngle = spotLightInfo.OuterAngleRadians,
                Position = spotLightInfo.Position,
                Quaternion = new Quaternion(spotLightInfo.QuartX, spotLightInfo.QuartY, spotLightInfo.QuartZ, spotLightInfo.QuartW)
            });
            if (placeholderMesh != null)
            {
                spotLightNode.AddObject(placeholderMesh);
            }

            return spotLightNode;
        }

        private SceneNode CreateSoundPlaceholderNode(SoundInfo soundInfo, GroupNode soundGroup, int index)
        {
            var soundName = $"Sound_{index}_{Path.GetFileNameWithoutExtension(soundInfo.SoundString)}";
            var soundNode = soundGroup.AddObject(new GroupNode(soundName) { IsEditable = false });

            var placeholderMesh = CreateSpecializedNode(() => new SoundPlaceholderNode("Sound_Placeholder")
            {
                SoundType = soundInfo.TypeString,
                CoordList = soundInfo.CoordList
            });
            if (placeholderMesh != null)
            {
                soundNode.AddObject(placeholderMesh);
            }

            return soundNode;
        }

        private void LoadIndexedComponents<T>(IList<T> components, GroupNode group, string prefix, Func<T, GroupNode, int, SceneNode> nodeCreator)
        {
            for (var i = 0; i < components.Count; i++)
            {
                try { nodeCreator(components[i], group, i); }
                catch (Exception ex) { _logger.Warning($"Failed to create {prefix} at index {i}: {ex.Message}"); }
            }
        }

        private SceneNode CreateCaptureLocationNode(CaptureLocation location, GroupNode parentGroup, int index)
        {
            var nodeName = $"CaptureLocation_{index}_{location.DatabaseKey}";
            var node = parentGroup.AddObject(new GroupNode(nodeName) { IsEditable = false });

            var flagpole = CreateSpecializedNode(() => new CaptureLocationFlagpoleNode("CaptureLocation_Flagpole")
            {
                LocationX = location.LocationX,
                LocationY = location.LocationY
            });
            if (flagpole != null) node.AddObject(flagpole);

            return node;
        }

        private SceneNode CreateTerrainOutlineNode(TerrainOutline outline, GroupNode parentGroup, int index)
        {
            var node = parentGroup.AddObject(new GroupNode($"TerrainOutline_{index}") { IsEditable = false });
            var meshNode = CreateSpecializedNode(() => new TerrainOutlineNode("TerrainOutline_Line")
            {
                Points = outline.Points
            });
            if (meshNode != null) node.AddObject(meshNode);
            return node;
        }

        private SceneNode CreateWaterOutlineNode(WaterOutline outline, GroupNode parentGroup, int index)
        {
            var node = parentGroup.AddObject(new GroupNode($"WaterOutline_{index}") { IsEditable = false });
            var meshNode = CreateSpecializedNode(() => new WaterOutlineNode("WaterOutline_Line")
            {
                Points = outline.Points
            });
            if (meshNode != null) node.AddObject(meshNode);
            return node;
        }

        private void TryLoadVegetation(PackFile bmdPackFile, GroupNode otherGroup)
        {
            var bmdFullPath = _packFileService.GetFullPath(bmdPackFile);
            var vegPath = bmdFullPath + ".vegetation";

            var vegFile = _packFileService.FindFile(vegPath);
            if (vegFile == null)
            {
                _logger.Information("No vegetation file found at: {Path}", vegPath);
                return;
            }

            try
            {
                var vegData = vegFile.DataSource.ReadData();
                var veg = BmdVegetationParser.TryParse(vegData);
                if (veg == null) return;

                var totalTrees = veg.TreeGroups.Sum(g => g.Instances.Count);
                _logger.Information("Loading vegetation: {Groups} tree groups, {Trees} total instances", veg.TreeGroups.Count, totalTrees);

                var vegGroup = otherGroup.AddObject(new GroupNode($"Vegetation ({totalTrees} trees)") { IsEditable = false });

                foreach (var treeGroup in veg.TreeGroups)
                {
                    var modelName = Path.GetFileNameWithoutExtension(treeGroup.ModelPath);
                    var groupNode = vegGroup.AddObject(new GroupNode($"{modelName} ({treeGroup.Instances.Count})") { IsEditable = false });

                    foreach (var instance in treeGroup.Instances)
                    {
                        var rotRad = MathHelper.ToRadians(instance.RotationDegrees);
                        var transform = Matrix.CreateScale(instance.Scale)
                            * Matrix.CreateRotationY(rotRad)
                            * Matrix.CreateTranslation(instance.X, instance.Y, instance.Z);

                        var treeNode = TryLoadTreeModel(treeGroup.ModelPath, transform, groupNode);
                        if (treeNode == null)
                        {
                            // Fall back to a small cube placeholder
                            var placeholder = new VfxPlaceholderNode("Tree_Placeholder");
                            placeholder.IsEditable = false;
                            placeholder.ModelMatrix = transform;
                            placeholder.Scale = 0.3f;
                            groupNode.AddObject(placeholder);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning("Failed to load vegetation: {Message}", ex.Message);
            }
        }

        private void TryLoadProceduralBmd(PackFile bmdPackFile, GroupNode otherGroup)
        {
            var bmdPath = NormalizePackPath(_packFileService.GetFullPath(bmdPackFile));
            var bmdDirectory = Path.GetDirectoryName(bmdPath);
            if (string.IsNullOrEmpty(bmdDirectory))
                return;

            var proceduralMatch = _packFileService.FindAllWithExtention(".bin")
                .FirstOrDefault(file =>
                {
                    var candidatePath = NormalizePackPath(file.FileName);
                    return string.Equals(Path.GetDirectoryName(candidatePath), bmdDirectory, StringComparison.OrdinalIgnoreCase)
                        && candidatePath.EndsWith("_procedural_bmd_data.bin", StringComparison.OrdinalIgnoreCase);
                });

            if (proceduralMatch.Pack == null)
            {
                _diagnostics.Write("Procedural BMD: no sibling *_procedural_bmd_data.bin found");
                return;
            }

            var proceduralPath = proceduralMatch.FileName;
            _diagnostics.Write($"Procedural BMD: {proceduralPath}");
            var group = otherGroup.AddObject(new GroupNode("Procedural_BMD") { IsEditable = false });
            LoadReferencedBmd(proceduralPath, group, 900000);
        }

        private static string NormalizePackPath(string path)
            => path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

        private SceneNode? TryLoadTreeModel(string modelPath, Matrix transform, GroupNode parentGroup)
        {
            var modelFile = _packFileService.FindFile(modelPath);
            if (modelFile == null) return null;

            try
            {
                var actualPath = modelPath;
                var actualFile = modelFile;

                if (Path.GetExtension(modelPath).Equals(".wsmodel", StringComparison.OrdinalIgnoreCase))
                {
                    var wsModel = new WsModelFile(modelFile);
                    if (string.IsNullOrEmpty(wsModel.GeometryPath)) return null;
                    actualPath = wsModel.GeometryPath;
                    actualFile = _packFileService.FindFile(actualPath);
                    if (actualFile == null) return null;
                }

                var fullPath = _packFileService.GetFullPath(actualFile);
                var lod = CreateModelInstance(modelPath, actualFile, fullPath, null, transform);
                if (lod == null) return null;
                parentGroup.AddObject(lod);
                return lod;
            }
            catch (Exception ex)
            {
                _logger.Debug("Failed to load tree model '{Path}': {Message}", modelPath, ex.Message);
                return null;
            }
        }

        public void HighlightComponent(BmdElementViewModel component)
        {
            // Clear previous highlight
            ClearHighlight();
            
            if (ComponentNodes.TryGetValue(component, out var node))
            {
                // TODO: Implement actual highlighting logic
                // This might involve changing the material, adding an outline, or changing the node's properties
                _currentHighlightedNode = node;
                _logger.Debug($"Highlighted component: {component.ElementType} - {component.DisplayName}");
            }
            else
            {
                _logger.Warning($"Component node not found: {component.ElementType} - {component.DisplayName}");
            }
        }

        
        public void ClearHighlight()
        {
            if (_currentHighlightedNode != null)
            {
                // TODO: Implement actual highlight clearing logic
                _logger.Debug($"Cleared highlight from: {_currentHighlightedNode.Name}");
                _currentHighlightedNode = null;
            }
        }

        private SceneNode CreateBmdInfoNode(BmdInfo bmdInfo, GroupNode bmdGroup, int index)
        {
            var bmdName = $"BMD_{index}_{Path.GetFileNameWithoutExtension(bmdInfo.BmdString)}";
            var bmdNode = bmdGroup.AddObject(new GroupNode(bmdName)
            {
                IsEditable = false,
                ModelMatrix = bmdInfo.Transform
            });

            // Create placeholder node for the BMD reference
            var placeholderMesh = CreatePlaceholderNode(() => new BmdInfoPlaceholderNode("BMD_Info_Placeholder")
            {
                ReferencedBmdPath = bmdInfo.BmdString
            }, Matrix.Identity);
            if (placeholderMesh != null)
            {
                bmdNode.AddObject(placeholderMesh);
            }

            // Try to load the referenced BMD file recursively
            try
            {
                LoadReferencedBmd(bmdInfo.BmdString, bmdNode, index);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to load referenced BMD '{bmdInfo.BmdString}' at index {index}: {ex.Message}");
            }

            return bmdNode;
        }

        private void LoadReferencedBmd(string bmdPath, GroupNode parentNode, int parentIndex)
        {
            if (string.IsNullOrEmpty(bmdPath))
            {
                _logger.Warning($"Empty BMD path for parent index {parentIndex}");
                return;
            }

            if (!_activeBmdReferences.Add(bmdPath))
            {
                _logger.Warning("Skipping recursive BMD reference: {Path}", bmdPath);
                return;
            }

            // Find the referenced BMD file
            var referencedBmdFile = _packFileService.FindFile(bmdPath);
            if (referencedBmdFile == null)
            {
                _logger.Warning($"Referenced BMD file not found: {bmdPath}");
                _activeBmdReferences.Remove(bmdPath);
                return;
            }

            try
            {
                if (!_referencedBmdCache.TryGetValue(bmdPath, out var referencedBmd))
                {
                    _bmdCacheMisses++;
                    _logger.Information("Loading referenced BMD file: {Path}", bmdPath);
                    _diagnostics.Write($"BMD cache miss: {bmdPath}");
                    referencedBmd = BmdParser.Parse(referencedBmdFile.DataSource.ReadData());
                    _referencedBmdCache[bmdPath] = referencedBmd;
                    WriteTransformBounds($"Referenced props [{bmdPath}]", referencedBmd.PropInfos.Select(x => x.Transform));
                    WriteTransformBounds($"Referenced buildings [{bmdPath}]", referencedBmd.BattlefieldBuildings.Select(x => x.Transform));
                }
                else
                {
                    _bmdCacheHits++;
                }

                if (referencedBmd == null)
                    return;

                // Reference will be stored by BmdElementLoader after view model is created

                // Create a group for the referenced BMD content
                var referencedBmdGroup = parentNode.AddObject(new GroupNode($"Referenced_Content_{Path.GetFileNameWithoutExtension(bmdPath)}") { IsEditable = false });

                // Load props from the referenced BMD
                if (referencedBmd.PropInfos.Count > 0)
                {
                    var propsGroup = referencedBmdGroup.AddObject(new GroupNode($"Referenced_Props ({referencedBmd.PropInfos.Count})") { IsEditable = false });
                    LoadReferencedProps(referencedBmd, propsGroup, parentIndex);
                }

                // Load other components from the referenced BMD (excluding BmdInfos to prevent infinite recursion)
                LoadReferencedOtherComponents(referencedBmd, referencedBmdGroup, parentIndex);

                _logger.Information($"Successfully loaded referenced BMD: {bmdPath}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to parse referenced BMD '{bmdPath}': {ex.Message}");
            }
            finally
            {
                _activeBmdReferences.Remove(bmdPath);
            }
        }

        private void LoadReferencedProps(BmdFile referencedBmd, GroupNode propsGroup, int parentIndex)
        {
            _logger.Information($"Loading {referencedBmd.PropInfos.Count} props from referenced BMD");

            for (int i = 0; i < referencedBmd.PropInfos.Count; i++)
            {
                try
                {
                    var propInfo = referencedBmd.PropInfos[i];
                    LoadSingleProp(propInfo.Rmv2Path, propInfo, propsGroup, parentIndex * 1000 + i); // Use unique index to avoid conflicts
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to load referenced prop at index {i}: {ex.Message}");
                }
            }
        }

        private void LoadReferencedOtherComponents(BmdFile referencedBmd, GroupNode otherGroup, int parentIndex)
        {
            _logger.Information("Loading other components from referenced BMD");

            var componentLoaders = new (string Name, int Count, Action<GroupNode> Loader)[]
            {
                ("Referenced_Point_Lights", referencedBmd.PointLights.Count, group => LoadReferencedComponents(referencedBmd.PointLights, group, "Referenced_PointLight", CreatePointLightPlaceholderNode, parentIndex)),
                ("Referenced_Spot_Lights", referencedBmd.SpotLights.Count, group => LoadReferencedComponents(referencedBmd.SpotLights, group, "Referenced_SpotLight", CreateSpotLightPlaceholderNode, parentIndex)),
                ("Referenced_Sounds", referencedBmd.Sounds.Count, group => LoadReferencedComponents(referencedBmd.Sounds, group, "Referenced_Sound", CreateSoundPlaceholderNode, parentIndex)),
                ("Referenced_VFX", referencedBmd.VfxInfos.Count, group => LoadReferencedComponents(referencedBmd.VfxInfos, group, "Referenced_VFX", CreateVfxPlaceholderNode, parentIndex)),
                ("Referenced_CSC", referencedBmd.CscInfos.Count, group => LoadReferencedComponents(referencedBmd.CscInfos, group, "Referenced_CSC", CreateCscPlaceholderNode, parentIndex)),
                ("Referenced_Light_Probes", referencedBmd.LightProbes.Count, group => LoadReferencedComponents(referencedBmd.LightProbes, group, "Referenced_LightProbe", CreateLightProbePlaceholderNode, parentIndex)),
                ("Referenced_Building_Projectile_Emitters", referencedBmd.BuildingProjectileEmitters.Count, group => LoadReferencedComponents(referencedBmd.BuildingProjectileEmitters, group, "Referenced_BuildingProjectileEmitter", CreateBuildingProjectileEmitterPlaceholderNode, parentIndex)),
                ("Referenced_Terrain_Holes", referencedBmd.TerrainHoles.Count, group => LoadReferencedComponents(referencedBmd.TerrainHoles, group, "Referenced_TerrainHole", CreateTerrainHoleNode, parentIndex)),
                ("Referenced_PolyMeshes", referencedBmd.PolyMeshes.Count, group => LoadReferencedComponents(referencedBmd.PolyMeshes, group, "Referenced_PolyMesh", CreatePolyMeshNode, parentIndex)),
                ("Referenced_Go_Outlines", referencedBmd.GoOutlines.Count, group => LoadReferencedComponents(referencedBmd.GoOutlines, group, "Referenced_GoOutline", CreateGoOutlineNode, parentIndex)),
                ("Referenced_NonTerrain_Outlines", referencedBmd.NonTerrainOutlines.Count, group => LoadReferencedComponents(referencedBmd.NonTerrainOutlines, group, "Referenced_NonTerrainOutline", CreateNonTerrainOutlineNode, parentIndex)),
                ("Referenced_Battlefield_Buildings", referencedBmd.BattlefieldBuildings.Count, group => LoadReferencedComponents(referencedBmd.BattlefieldBuildings, group, "Referenced_BattlefieldBuilding", CreateBattlefieldBuildingPlaceholderNode, parentIndex))
                // Note: We intentionally exclude BmdInfos here to prevent infinite recursion
            };

            foreach (var (name, count, loader) in componentLoaders)
            {
                if (count > 0)
                {
                    _logger.Information($"Creating {name} group ({count} items)");
                    var group = otherGroup.AddObject(new GroupNode($"{name} ({count})") { IsEditable = false });
                    loader(group);
                }
            }
        }

        private void LoadReferencedComponents<T>(IList<T> components, GroupNode group, string prefix, Func<T, GroupNode, int, SceneNode> nodeCreator, int parentIndex)
        {
            _logger.Information($"Loading {components.Count} {prefix} components from referenced BMD");

            for (int i = 0; i < components.Count; i++)
            {
                try
                {
                    var component = components[i];
                    var node = nodeCreator(component, group, parentIndex * 1000 + i); // Use unique index to avoid conflicts
                    // Reference will be stored by BmdElementLoader after view model is created
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to create referenced {prefix} component at index {i}: {ex.Message}");
                }
            }
        }

        private void LoadDeploymentComponents(List<Deployment> deployments, GroupNode deploymentGroup, List<DeploymentViewModel>? deploymentViewModels)
        {
            _logger.Information($"Loading {deployments.Count} deployment components");

            for (int i = 0; i < deployments.Count; i++)
            {
                try
                {
                    var deployment = deployments[i];
                    var deploymentNode = CreateDeploymentNode(deployment, deploymentGroup, i);

                    if (deploymentViewModels != null && i < deploymentViewModels.Count)
                    {
                        var viewModel = deploymentViewModels[i];
                        ComponentNodes[viewModel] = deploymentNode;
                        LoadDeploymentZones(deployment.DeploymentZones, deploymentNode, viewModel.Children.OfType<DeploymentZoneViewModel>().ToList());
                    }
                    else
                    {
                        LoadDeploymentZones(deployment.DeploymentZones, deploymentNode, null);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to create deployment component at index {i}: {ex.Message}");
                }
            }
        }

        private void LoadDeploymentZones(List<DeploymentZone> deploymentZones, SceneNode deploymentNode, List<DeploymentZoneViewModel>? zoneViewModels)
        {
            for (int i = 0; i < deploymentZones.Count; i++)
            {
                try
                {
                    var deploymentZone = deploymentZones[i];
                    var zoneNode = CreateDeploymentZoneNode(deploymentZone, deploymentNode, i);

                    if (zoneViewModels != null && i < zoneViewModels.Count)
                    {
                        var zoneViewModel = zoneViewModels[i];
                        ComponentNodes[zoneViewModel] = zoneNode;
                        LoadDeploymentZoneRegions(deploymentZone.DeploymentZoneRegions, zoneNode, zoneViewModel.Children.OfType<DeploymentZoneRegionViewModel>().ToList());
                    }
                    else
                    {
                        LoadDeploymentZoneRegions(deploymentZone.DeploymentZoneRegions, zoneNode, null);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to create deployment zone component at index {i}: {ex.Message}");
                }
            }
        }

        private void LoadDeploymentZoneRegions(List<DeploymentZoneRegion> deploymentZoneRegions, SceneNode zoneNode, List<DeploymentZoneRegionViewModel>? regionViewModels)
        {
            for (int i = 0; i < deploymentZoneRegions.Count; i++)
            {
                try
                {
                    var deploymentZoneRegion = deploymentZoneRegions[i];
                    var regionNode = CreateDeploymentZoneRegionNode(deploymentZoneRegion, zoneNode, i);

                    if (regionViewModels != null && i < regionViewModels.Count)
                    {
                        var regionViewModel = regionViewModels[i];
                        ComponentNodes[regionViewModel] = regionNode;
                        LoadBoundaries(deploymentZoneRegion.Boundaries, regionNode, regionViewModel.Children.OfType<BoundaryViewModel>().ToList());
                    }
                    else
                    {
                        LoadBoundaries(deploymentZoneRegion.Boundaries, regionNode, null);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to create deployment zone region component at index {i}: {ex.Message}");
                }
            }
        }

        private void LoadBoundaries(List<Boundary> boundaries, SceneNode regionNode, List<BoundaryViewModel>? boundaryViewModels)
        {
            for (int i = 0; i < boundaries.Count; i++)
            {
                try
                {
                    var boundary = boundaries[i];
                    var boundaryNode = CreateBoundaryNode(boundary, regionNode, i);
                    if (boundaryViewModels != null && i < boundaryViewModels.Count)
                        ComponentNodes[boundaryViewModels[i]] = boundaryNode;
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to create boundary component at index {i}: {ex.Message}");
                }
            }
        }

        private SceneNode CreateDeploymentNode(Deployment deployment, GroupNode deploymentGroup, int index)
        {
            var deploymentName = $"Deployment_{index}_{deployment.Category}";
            var deploymentNode = deploymentGroup.AddObject(new GroupNode(deploymentName) { IsEditable = false });
            return deploymentNode;
        }

        private SceneNode CreateDeploymentZoneNode(DeploymentZone deploymentZone, SceneNode deploymentNode, int index)
        {
            var zoneName = $"DeploymentZone_{index}";
            var zoneNode = deploymentNode.AddObject(new GroupNode(zoneName) { IsEditable = false });
            return zoneNode;
        }

        private SceneNode CreateDeploymentZoneRegionNode(DeploymentZoneRegion deploymentZoneRegion, SceneNode zoneNode, int index)
        {
            var regionName = $"DeploymentZoneRegion_{deploymentZoneRegion.Id}";
            var regionNode = zoneNode.AddObject(new GroupNode(regionName) { IsEditable = false });
            return regionNode;
        }

        private SceneNode CreateBoundaryNode(Boundary boundary, SceneNode regionNode, int index)
        {
            var boundaryName = $"Boundary_{index}_{boundary.BoundaryType}";
            var boundaryNode = regionNode.AddObject(new GroupNode(boundaryName) { IsEditable = false });

            var boundaryMesh = CreateSpecializedNode(() => new BoundaryNode("Boundary_Placeholder")
            {
                PointList = boundary.PointList
            });
            if (boundaryMesh != null)
            {
                boundaryNode.AddObject(boundaryMesh);
            }

            if (boundary.PointList.Count >= 3)
            {
                var area = CreateSpecializedNode(() => new DeploymentAreaNode("Deployment_Fill")
                {
                    Points = boundary.PointList
                });
                if (area != null)
                    boundaryNode.AddObject(area);
            }

            return boundaryNode;
        }

        public GroupNode CreateBattleMapRoot(string folderPath)
        {
            _modelPathsByName = null;
            _modelPathsByDirectory = null;
            _activeBmdReferences.Clear();
            _modelTemplateCache.Clear();
            _referencedBmdCache.Clear();
            _missingModelPaths.Clear();
            _modelCacheHits = _modelCacheMisses = _bmdCacheHits = _bmdCacheMisses = 0;
            _sceneLoadStopwatch.Restart();
            _diagnostics.Begin(folderPath);
            var existingNodes = _sceneManager.RootNode.Children
                .Where(x => x.Name is "BattleMap_Scene" or "BMD_Scene").ToList();
            foreach (var node in existingNodes)
                _sceneManager.RootNode.Children.Remove(node);
            ComponentNodes.Clear();
            _currentHighlightedNode = null;
            return _sceneManager.RootNode.AddObject(new GroupNode("BattleMap_Scene") { IsEditable = false });
        }

        public void LoadBmdContentNoTracking(BmdFile bmdFile, PackFile bmdPackFile, GroupNode propsGroup, GroupNode otherGroup)
        {
            _currentBmdPackFile = bmdPackFile;
            _propsGroup = propsGroup;
            _otherGroup = otherGroup;
            var label = bmdPackFile.Name;
            RunTimedPhase($"props [{label}]", () => LoadProps(bmdFile, propsGroup, null));
            RunTimedPhase($"components [{label}]", () => LoadOtherComponents(bmdFile, otherGroup, null));
            RunTimedPhase($"vegetation [{label}]", () => TryLoadVegetation(bmdPackFile, otherGroup));
            RunTimedPhase($"procedural [{label}]", () => TryLoadProceduralBmd(bmdPackFile, otherGroup));
        }

        public void LoadTerrainIntoGroup(PackFile bmdPackFile, GroupNode rootNode)
            => RunTimedPhase($"terrain [{bmdPackFile.Name}]", () => LoadTerrain(bmdPackFile, rootNode));

        public void CompleteLoadLog()
        {
            var cacheSummary =
                $"Load cache: RMV2 hits={_modelCacheHits}, misses={_modelCacheMisses}; " +
                $"BMD hits={_bmdCacheHits}, misses={_bmdCacheMisses}";
            _diagnostics.Write(cacheSummary);
            WriteSceneTotals();
            _sceneLoadStopwatch.Stop();
            _diagnostics.Write($"Total scene load: {_sceneLoadStopwatch.Elapsed.TotalMilliseconds:0.0} ms");
            _diagnostics.Complete();
        }

    }

    // Special key class for BMD reference tracking to prevent infinite recursion
    public class BmdBmdReferenceKey : BmdElementViewModel
    {
        public string BmdPath { get; }

        public BmdBmdReferenceKey(string bmdPath) : base("BMD_Reference", bmdPath, "BMD reference for recursion prevention")
        {
            BmdPath = bmdPath;
        }

        public override bool Equals(object? obj)
        {
            return obj is BmdBmdReferenceKey other && BmdPath == other.BmdPath;
        }

        public override int GetHashCode()
        {
            return BmdPath.GetHashCode();
        }
    }
}
