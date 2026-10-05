using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WindTraceRide.Bootstrap;
using WindTraceRide.Core;

namespace WindTraceRide.Editor
{
    public static class ReferenceWorldValidation
    {
        public static void PrepareShaders()
        {
            const string folder="Assets/Resources/Art/Materials/ReferenceWorld";
            if(!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/Resources/Art/Materials","ReferenceWorld");
            foreach(var name in new[]{"Reference Paving","Reference Surface","Reference Waterfall","Reference Water","Reference Sky","Reference Mountain"})
            {
                var shader=Shader.Find("WindTrace/"+name);
                if(shader==null)throw new MissingReferenceException(name);
                var path=$"{folder}/{name.Replace(" ","")}.mat";
                var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(mat==null)AssetDatabase.CreateAsset(new Material(shader),path);
            }
            AssetDatabase.SaveAssets();
        }

        public static void RenderReview()
        {
            PrepareShaders();
            Directory.CreateDirectory("TestArtifacts/reference-world");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var target=new RenderTexture(1600,900,24);
            var report="Reference world route review\n";
            try
            {
                for(var index=0;index<3;index++)
                {
                    var root=new GameObject("Reference review "+index);
                    try
                    {
                        var world=root.AddComponent<RideWorldController>();
                        world.SetLevel(index);world.Initialize();
                        world.SetReviewTheme(new[]{RidePhaseKind.Warmup,RidePhaseKind.CadenceChallenge,RidePhaseKind.Boss}[index]);
                        var environment=root.GetComponentInChildren<ReferenceRideEnvironment>();
                        var camera=root.GetComponentInChildren<Camera>();
                        camera.targetTexture=target;
                        var length=RideWorldController.CourseKilometers*1000;
                        foreach(var distance in new[]{80f,170f,310f,length*.25f,length*.75f,length-10f})
                        {
                            world.SetReviewDistance(distance);
                            if(environment.ActiveChunks>ReferenceRideEnvironment.PoolSize)
                                throw new Exception("Unbounded scenery chunks");
                            camera.Render();RenderTexture.active=target;
                            var image=new Texture2D(1600,900,TextureFormat.RGB24,false);
                            image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();
                            var path=$"TestArtifacts/reference-world/level-{index+1}-{distance:00000}.png";
                            File.WriteAllBytes(path,image.EncodeToPNG());
                            UnityEngine.Object.DestroyImmediate(image);
                            var planes=GeometryUtility.CalculateFrustumPlanes(camera);
                            var visible=root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled&&GeometryUtility.TestPlanesAABB(planes,r.bounds)).ToArray();
                            var triangles=visible.Sum(r=>
                            {
                                var filter=r.GetComponent<MeshFilter>();
                                return filter!=null && filter.sharedMesh!=null ? filter.sharedMesh.triangles.Length/3 : 0;
                            });
                            report+=$"level={index+1} distance={distance:0} chunks={environment.ActiveChunks} visibleRenderers={visible.Length} upperBoundTriangles={triangles} screenshot={path}\n";
                        }
                        camera.targetTexture=null;
                    }
                    finally{RenderTexture.active=null;UnityEngine.Object.DestroyImmediate(root);}
                }
                File.WriteAllText("TestArtifacts/reference-world/review.txt",report);
                Debug.Log("REFERENCE_WORLD_REVIEW_READY\n"+report);
            }
            finally{RenderTexture.active=null;UnityEngine.Object.DestroyImmediate(target);}
        }
    }
}
