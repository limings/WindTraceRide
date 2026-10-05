using UnityEditor;

namespace WindTraceRide.Editor
{
    /// <summary>
    /// Keeps the Blender review masters high quality while applying mobile-safe
    /// import settings to their Unity export copies.
    /// </summary>
    public sealed class HiFiModelImportSettings : AssetPostprocessor
    {
        private const string HiFiFolder = "Assets/Resources/Art/Models/HiFi/";
        private const string HiFiTextureFolder = "Assets/Resources/Art/Textures/HiFi/";
        private const string BackgroundFolder = "Assets/Resources/Art/Backgrounds/";

        public static void ForceReimportModels()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { HiFiFolder }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
        }

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(HiFiFolder)) return;

            var importer = (ModelImporter)assetImporter;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            // RideWorldController combines these modular FBX meshes at runtime.
            // Android strips CPU mesh data unless Read/Write stays enabled, which
            // produced empty optimized meshes after the source renderers were hidden.
            importer.isReadable = true;
            importer.meshCompression = ModelImporterMeshCompression.Medium;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
        }

        private void OnPreprocessTexture()
        {
            if (assetPath.StartsWith(BackgroundFolder))
            {
                var backgroundImporter = (TextureImporter)assetImporter;
                backgroundImporter.maxTextureSize = 2048;
                backgroundImporter.mipmapEnabled = false;
                backgroundImporter.alphaIsTransparency = false;
                backgroundImporter.textureCompression = TextureImporterCompression.CompressedHQ;
                backgroundImporter.wrapMode = UnityEngine.TextureWrapMode.Clamp;
                backgroundImporter.filterMode = UnityEngine.FilterMode.Bilinear;
                return;
            }

            if (!assetPath.StartsWith(HiFiTextureFolder)) return;

            var importer = (TextureImporter)assetImporter;
            importer.maxTextureSize = 1024;
            importer.mipmapEnabled = true;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Compressed;
        }
    }
}
