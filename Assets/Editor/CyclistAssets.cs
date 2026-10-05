using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WindTraceRide.Bootstrap;

namespace WindTraceRide.Editor
{
    public sealed class CyclistModelImportSettings : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith("Assets/Resources/Art/Models/Cyclists/")) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Legacy;
            importer.importAnimation = true;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.importCameras = importer.importLights = false;
            importer.optimizeGameObjects = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Resources/Art/Textures/Cyclists/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.maxTextureSize = 1024;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }

    public static class CyclistAssets
    {
        [Serializable] private sealed class MaterialEntry { public string name; public float[] color; public string texture; public float metallic; public float roughness; }
        [Serializable] private sealed class MaterialList { public MaterialEntry[] materials; }

        public static void BuildAndValidate()
        {
            Directory.CreateDirectory("Assets/Resources/Art/Cyclists");
            Directory.CreateDirectory("Assets/Resources/Art/Materials/Cyclists");
            Directory.CreateDirectory("TestArtifacts/cyclist-unity");
            foreach (var name in new[] { "Male", "Female" }) Build(name);
            AssetDatabase.SaveAssets();
            RenderWorldReview();
            Debug.Log("CYCLIST_ASSETS_VALIDATED");
        }

        public static void RenderRouteMilestones()
        {
            Directory.CreateDirectory("TestArtifacts/cyclist-unity");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var saved = PlayerPrefs.GetInt(RideWorldController.CharacterPreferenceKey, 0);
            try
            {
                PlayerPrefs.SetInt(RideWorldController.CharacterPreferenceKey, (int)WindTraceRide.Core.RiderCharacter.Female);
                var root = new GameObject("Route milestone review");
                var world = root.AddComponent<RideWorldController>();
                world.Initialize();
                var camera = root.GetComponentInChildren<Camera>();
                var rt = new RenderTexture(1600, 900, 24);
                camera.targetTexture = rt;
                foreach (var milestone in new[] { 360f, 16800f, 33580f })
                {
                    world.SetReviewDistance(milestone);
                    camera.Render();
                    RenderTexture.active = rt;
                    var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                    image.Apply();
                    File.WriteAllBytes($"TestArtifacts/cyclist-unity/route-{milestone:0}m.png", image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                }
                RenderTexture.active = null;
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(root);
                Debug.Log("ROUTE_MILESTONES_RENDERED");
            }
            finally { PlayerPrefs.SetInt(RideWorldController.CharacterPreferenceKey, saved); }
        }

        private static void RenderWorldReview()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var saved = PlayerPrefs.GetInt(RideWorldController.CharacterPreferenceKey, 0);
            try
            {
                foreach (var choice in new[] { WindTraceRide.Core.RiderCharacter.Male, WindTraceRide.Core.RiderCharacter.Female })
                {
                    PlayerPrefs.SetInt(RideWorldController.CharacterPreferenceKey, (int)choice);
                    var root = new GameObject("World review");
                    var world = root.AddComponent<RideWorldController>(); world.Initialize();
                    var rider = root.GetComponentInChildren<CyclingRiderAnimator>();
                    var position = rider.transform.position; var rotation = rider.transform.rotation;
                    rider.Tick(90, 6, .5f);
                    if (Vector3.Distance(position, rider.transform.position) > .0001f || Quaternion.Angle(rotation, rider.transform.rotation) > .01f)
                        throw new InvalidOperationException("Animation moved the placed rider root.");
                    var cameras = root.GetComponentsInChildren<Camera>().OrderBy(camera => camera.depth).ToArray();
                    var rt = new RenderTexture(1600,900,24);
                    foreach (var camera in cameras) { camera.targetTexture=rt; camera.Render(); }
                    WriteWorldBudget(root, choice.ToString()); RenderTexture.active=rt;
                    var image = new Texture2D(1600,900,TextureFormat.RGB24,false);
                    image.ReadPixels(new Rect(0,0,1600,900),0,0); image.Apply();
                    File.WriteAllBytes($"TestArtifacts/cyclist-unity/{choice}-world.png", image.EncodeToPNG());
                    RenderTexture.active=null; foreach (var camera in cameras) camera.targetTexture=null;
                    UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(rt);
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
            finally { PlayerPrefs.SetInt(RideWorldController.CharacterPreferenceKey, saved); }
        }

        private static void WriteWorldBudget(GameObject root, string character)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var meshTriangles = root.GetComponentsInChildren<MeshFilter>(true)
                .Where(filter => filter.GetComponent<MeshRenderer>() == null || filter.GetComponent<MeshRenderer>().enabled)
                .Sum(filter => filter.sharedMesh == null ? 0 : filter.sharedMesh.triangles.Length / 3);
            var skinnedTriangles = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(renderer => renderer.enabled)
                .Sum(renderer => renderer.sharedMesh == null ? 0 : renderer.sharedMesh.triangles.Length / 3);
            var lodGroups = root.GetComponentsInChildren<LODGroup>(true).Length;
            var visible = renderers.Where(renderer => renderer.isVisible).ToArray();
            var visibleTriangles = visible.Sum(renderer => RendererTriangles(renderer));
            var budget = new WorldBudget
            {
                character = character, renderers = renderers.Length, meshTriangles = meshTriangles,
                skinnedTriangles = skinnedTriangles, mobileDistanceCullGroups = lodGroups, visibleRenderers = visible.Length,
                visibleTriangles = visibleTriangles, passed = visible.Length < 120 && visibleTriangles < 190000 && meshTriangles + skinnedTriangles < 650000
            };
            if (!budget.passed) throw new InvalidOperationException($"Mobile geometry budget exceeded: {visible.Length} renderers, {visibleTriangles} visible triangles, {meshTriangles + skinnedTriangles} total triangles.");
            File.WriteAllText("TestArtifacts/cyclist-unity/" + character + "-world-budget.json", JsonUtility.ToJson(budget, true));
            Debug.Log($"WORLD_BUDGET_PASS {character}: visible={visible.Length}/{renderers.Length}, triangles={visibleTriangles}/{meshTriangles + skinnedTriangles}, cullGroups={lodGroups}");
        }

        private static int RendererTriangles(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skin) return skin.sharedMesh == null ? 0 : skin.sharedMesh.triangles.Length / 3;
            var filter = renderer.GetComponent<MeshFilter>();
            return filter == null || filter.sharedMesh == null ? 0 : filter.sharedMesh.triangles.Length / 3;
        }

        private static void Build(string name)
        {
            var path = "Assets/Resources/Art/Models/Cyclists/" + name + ".fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            var defaults = importer.defaultClipAnimations;
            if (defaults.Length == 0) throw new InvalidOperationException(name + " has no imported take.");
            defaults[0].name = "Pedal";
            defaults[0].loopTime = true;
            defaults[0].wrapMode = WrapMode.Loop;
            importer.clipAnimations = new[] { defaults[0] };
            importer.SaveAndReimport();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            var instance = UnityEngine.Object.Instantiate(source);
            instance.name = name + "Cyclist";
            try
            {
                var data = JsonUtility.FromJson<MaterialList>(File.ReadAllText("Assets/Resources/Art/Models/Cyclists/" + name + ".materials.json"));
                foreach (var entry in data.materials)
                {
                    var materialPath = "Assets/Resources/Art/Materials/Cyclists/" + entry.name + ".mat";
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (mat == null) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, materialPath); }
                    mat.color = new Color(entry.color[0], entry.color[1], entry.color[2], entry.color[3]);
                    mat.SetFloat("_Metallic", entry.metallic);
                    mat.SetFloat("_Glossiness", 1-entry.roughness);
                    if (!string.IsNullOrEmpty(entry.texture))
                    {
                        mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Art/Textures/Cyclists/" + entry.texture);
                        if (mat.mainTexture == null) throw new InvalidOperationException("Missing texture: " + entry.texture);
                        mat.color = Color.white;
                        if (entry.name == "Male_model_002" || entry.name == "Female_model_003") mat.SetFloat("_Glossiness", .18f);
                    }
                    foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                    {
                        var slots = renderer.sharedMaterials;
                        for (var i=0;i<slots.Length;i++) if (slots[i]!=null && slots[i].name==entry.name) slots[i]=mat;
                        renderer.sharedMaterials=slots;
                    }
                }
                var animation = instance.GetComponent<CyclingRiderAnimator>() ?? instance.AddComponent<CyclingRiderAnimator>();
                animation.pedalClip = clip;
                clip.SampleAnimation(instance, 0);
                var renderers = instance.GetComponentsInChildren<Renderer>();
                var bounds = renderers[0].bounds;
                foreach (var r in renderers)
                {
                    // Importer culling bounds are deliberately larger than the body.
                    if (r is SkinnedMeshRenderer skin)
                    {
                        foreach (var vertex in WorldVertices(skin)) bounds.Encapsulate(vertex);
                    }
                    else bounds.Encapsulate(r.bounds);
                }
                Debug.Log($"CYCLIST_IMPORT {name}: bounds={bounds.size}, clip={clip.length}, renderers={renderers.Length}");
                if (bounds.size.y < 1.2f || bounds.size.y > 2.3f) throw new InvalidOperationException(name+" scale is incorrect: "+bounds.size);
                if (instance.GetComponentsInChildren<SkinnedMeshRenderer>().Length!=1) throw new InvalidOperationException("Expected one skinned rider.");
                if (Mathf.Abs(clip.length-1f)>.03f) throw new InvalidOperationException("Pedal loop is not one second.");
                animation.Initialize();
                ValidateMotion(instance, animation, clip, name);
                PrefabUtility.SaveAsPrefabAsset(instance,"Assets/Resources/Art/Cyclists/"+name+".prefab");
                RenderReview(instance, clip, name);
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        private static void ValidateMotion(GameObject instance, CyclingRiderAnimator animator, AnimationClip clip, string name)
        {
            var frame = CyclingRiderAnimator.Find(instance.transform,"Frame_STATIC");
            var origin=frame.position; var rotation=frame.rotation;
            var skin=instance.GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh=new Mesh(); float worst=0;
            try
            {
                for (var sample=0;sample<=48;sample++)
                {
                    clip.SampleAnimation(instance,sample/48f);
                    if (Vector3.Distance(frame.position,origin)>.0001f || Quaternion.Angle(frame.rotation,rotation)>.01f) throw new InvalidOperationException("Static frame animated.");
                    var vertices = WorldVertices(skin);
                    foreach (var side in new[]{"Left","Right"})
                    {
                        var contact=CyclingRiderAnimator.Find(instance.transform,side+"_Pedal_Contact").position;
                        var nearest=vertices.Min(v=>Vector3.Distance(v,contact));
                        worst=Mathf.Max(worst,nearest);
                    }
                }
                if (worst>.015f) throw new InvalidOperationException(name+" shoe contact drift: "+worst);
                animator.Tick(60,3, .25f); var phase=animator.PedalPhase;
                animator.Tick(0,0,1);
                if (animator.PedalPhase!=phase) throw new InvalidOperationException("Stopped pedals changed phase.");
                File.WriteAllText("TestArtifacts/cyclist-unity/"+name+"-validation.json",JsonUtility.ToJson(new Validation{character=name,frames=49,soleError=worst,passed=true},true));
                Debug.Log($"CYCLIST_MOTION_PASS {name}: max sole drift {worst}");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); clip.SampleAnimation(instance,0); }
        }
        [Serializable] private sealed class Validation { public string character; public int frames; public float soleError; public bool passed; }
        [Serializable] private sealed class WorldBudget { public string character; public int renderers; public int meshTriangles; public int skinnedTriangles; public int mobileDistanceCullGroups; public int visibleRenderers; public int visibleTriangles; public bool passed; }

        private static Vector3[] WorldVertices(SkinnedMeshRenderer skin)
        {
            var mesh=skin.sharedMesh; var vertices=mesh.vertices; var weights=mesh.boneWeights;
            var bind=mesh.bindposes; var bones=skin.bones;
            var matrices=bones.Select((bone,i)=>bone.localToWorldMatrix*bind[i]).ToArray();
            for(var i=0;i<vertices.Length;i++)
            {
                var v=vertices[i]; var w=weights[i];
                vertices[i]=matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0
                    +matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1
                    +matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2
                    +matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;
            }
            return vertices;
        }

        private static void RenderReview(GameObject instance, AnimationClip clip, string name)
        {
            var cameraObject=new GameObject("ReviewCamera"); var lightObject=new GameObject("ReviewLight");
            var camera=cameraObject.AddComponent<Camera>(); var light=lightObject.AddComponent<Light>();
            var front=CyclingRiderAnimator.Find(instance.transform,"FrontWheelPivot_ROTATE_X");
            var rear=CyclingRiderAnimator.Find(instance.transform,"RearWheelPivot_ROTATE_X");
            var direction=front.position-rear.position; direction.y=0;
            instance.transform.rotation=Quaternion.FromToRotation(direction,Vector3.forward)*instance.transform.rotation;
            cameraObject.transform.position=new Vector3(2.5f,1.7f,-3.4f);
            cameraObject.transform.LookAt(new Vector3(0,.95f,0)); camera.fieldOfView=40;
            camera.backgroundColor=new Color(.03f,.06f,.08f); camera.clearFlags=CameraClearFlags.SolidColor;
            light.type=LightType.Directional; light.intensity=1.4f; lightObject.transform.rotation=Quaternion.Euler(35,-35,0);
            RenderSettings.ambientLight=new Color(.65f,.65f,.65f);
            var rt=new RenderTexture(900,900,24); camera.targetTexture=rt;
            try
            {
                foreach (var sample in new[]{0f,.25f,.5f,.75f})
                {
                    clip.SampleAnimation(instance,sample); camera.Render();
                    RenderTexture.active=rt; var image=new Texture2D(900,900,TextureFormat.RGB24,false);
                    image.ReadPixels(new Rect(0,0,900,900),0,0); image.Apply();
                    File.WriteAllBytes($"TestArtifacts/cyclist-unity/{name}-{sample:0.00}.png",image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                }
            }
            finally { RenderTexture.active=null; camera.targetTexture=null; UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(lightObject); }
        }
    }
}

