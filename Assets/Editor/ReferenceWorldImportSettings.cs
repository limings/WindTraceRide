using UnityEditor;

namespace WindTraceRide.Editor
{
    public sealed class ReferenceWorldImportSettings : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if(!assetPath.Contains("/Models/ReferenceWorld/"))return;
            var importer=(ModelImporter)assetImporter;
            importer.importAnimation=false;importer.importCameras=false;importer.importLights=false;
            importer.bakeAxisConversion=true;
            importer.isReadable=true;
            importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
            importer.meshCompression=ModelImporterMeshCompression.Low;
        }
        private void OnPreprocessTexture()
        {
            if(!assetPath.Contains("/Textures/ReferenceWorld/"))return;
            var importer=(TextureImporter)assetImporter;
            importer.maxTextureSize=512;importer.mipmapEnabled=true;
            importer.wrapMode=UnityEngine.TextureWrapMode.Repeat;
            importer.textureCompression=TextureImporterCompression.Compressed;
        }
    }
}
