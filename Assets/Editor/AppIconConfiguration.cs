using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
#if UNITY_ANDROID
using UnityEditor.Android;
#endif

namespace WindTraceRide.Editor
{
    public static class AppIconConfiguration
    {
        public const string IconPath = "Assets/Branding/AppIcon.png";

        [MenuItem("Tools/Wind Trace Ride/Apply App Icon")]
        public static void Apply()
        {
            var importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException($"App icon is missing: {IconPath}");

            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (texture == null || texture.width != texture.height)
                throw new InvalidOperationException("The app icon must be a square texture.");

            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { texture }, IconKind.Any);

#if UNITY_ANDROID
            ApplyAndroidIcons(AndroidPlatformIconKind.Legacy, texture);
            ApplyAndroidIcons(AndroidPlatformIconKind.Round, texture);
            ApplyAndroidIcons(AndroidPlatformIconKind.Adaptive, texture);
#endif
            AssetDatabase.SaveAssets();
            Debug.Log($"Wind Trace Ride app icon configured: {texture.width}x{texture.height}.");
        }

#if UNITY_ANDROID
        private static void ApplyAndroidIcons(PlatformIconKind kind, Texture2D texture)
        {
            var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
            if (icons.Length == 0)
                throw new InvalidOperationException($"No Android icon slots are available for {kind}.");

            foreach (var icon in icons)
            {
                var layers = new Texture2D[icon.maxLayerCount];
                for (var index = 0; index < layers.Length; index++)
                    layers[index] = texture;
                icon.SetTextures(layers);
            }

            PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
            Debug.Log($"Wind Trace Ride Android icon slots configured: {kind}, {icons.Length} sizes.");
        }
#endif
    }
}
