using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WindTraceRide.Bootstrap;
using WindTraceRide.Core;

namespace WindTraceRide.Editor
{
    public static class ArtValidation
    {
        public static void PrepareSkyMaterials()
        {
            const string folder = "Assets/Resources/Art/Materials/Skies";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources/Art/Materials"))
                    AssetDatabase.CreateFolder("Assets/Resources/Art", "Materials");
                AssetDatabase.CreateFolder("Assets/Resources/Art/Materials", "Skies");
            }
            var shader = Shader.Find("Skybox/Panoramic");
            if (shader == null) throw new MissingReferenceException("Skybox/Panoramic is unavailable in the Editor.");
            foreach (var name in new[] { "TahoePanorama", "CanyonPanorama", "VillagePanorama" })
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Resources/Art/Backgrounds/{name}.png");
                if (texture == null) throw new MissingReferenceException($"Missing panorama: {name}");
                var path = $"{folder}/{name}Sky.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }
                material.shader = shader;
                material.SetTexture("_MainTex", texture);
                material.SetFloat("_Exposure", 1f);
                material.SetFloat("_Rotation", 0f);
                EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("SKY_MATERIALS_PREPARED");
        }

        public static void RenderThemeReview()
        {
            Directory.CreateDirectory("TestArtifacts/theme-review");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var target = new RenderTexture(1600, 900, 24);
            try
            {
                var phases = new[] { RidePhaseKind.Warmup, RidePhaseKind.CadenceChallenge, RidePhaseKind.Boss };
                for (var index = 0; index < phases.Length; index++)
                {
                    var root = new GameObject($"Level {index + 1} review");
                    try
                    {
                        var world = root.AddComponent<RideWorldController>();
                        world.SetLevel(index);
                        world.Initialize();
                        world.SetReviewDistance(new[] { 80f, 2400f, 5000f }[index]);
                        world.SetReviewTheme(phases[index]);
                        var camera = root.GetComponentInChildren<Camera>();
                        camera.targetTexture = target;
                        camera.Render();
                        RenderTexture.active = target;
                        var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                        image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                        image.Apply();
                        File.WriteAllBytes($"TestArtifacts/theme-review/stage-{index + 1}.png", image.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(image);
                        camera.targetTexture = null;
                    }
                    finally { UnityEngine.Object.DestroyImmediate(root); }
                }
                Debug.Log("THEME_REVIEW_RENDERED");
            }
            finally
            {
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        public static void RenderTahoeLateRouteReview()
        {
            Directory.CreateDirectory("TestArtifacts/late-route-review");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Tahoe late-route review");
            var target = new RenderTexture(1600, 900, 24);
            try
            {
                var world = root.AddComponent<RideWorldController>();
                world.SetLevel(0);
                world.Initialize();
                var camera = root.GetComponentInChildren<Camera>();
                camera.targetTexture = target;
                foreach (var progress in new[] { .75f, .8f, .85f })
                {
                    world.SetReviewDistance(9600f * progress);
                    camera.Render();
                    RenderTexture.active = target;
                    var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                    image.Apply();
                    File.WriteAllBytes($"TestArtifacts/late-route-review/tahoe-{Mathf.RoundToInt(progress * 100)}.png", image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                }
                camera.targetTexture = null;
                Debug.Log("TAHOE_LATE_ROUTE_REVIEW_RENDERED");
            }
            finally
            {
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        public static void LogStarterKitBounds()
        {
            LogBounds("Assets/Resources/Art/Models/RiderBike.fbx");
            LogBounds("Assets/Resources/Art/Models/LakesideTree.fbx");
            LogBounds("Assets/Resources/Art/Models/LakesideRock.fbx");
            LogBounds("Assets/Resources/Art/Models/TrailSign.fbx");
            LogBounds("Assets/Resources/Art/Models/WindOrb.fbx");
        }

        public static void LogGeoBounds()
        {
            for (var y = 0; y < 2; y++)
                for (var x = 0; x < 2; x++)
                    LogBounds($"Assets/Resources/Art/Models/Geo/TahoeWestShore/TahoeTerrain_{x}_{y}.obj");
        }

        private static void LogBounds(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException($"Missing art asset: {path}");
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) throw new InvalidOperationException($"No renderers in: {path}");
                var bounds = renderers[0].bounds;
                for (var index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
                Debug.Log($"ART_BOUNDS {path} renderers={renderers.Length} center={bounds.center} size={bounds.size}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }
    }
}
