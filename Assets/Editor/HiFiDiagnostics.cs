using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WindTraceRide.Editor
{
    public static class HiFiDiagnostics
    {
        public static void Report()
        {
            var paths = new[]
            {
                "Art/Models/HiFi/RiderBikeHiFi",
                "Art/Models/HiFi/LakesidePineHiFi",
                "Art/Models/HiFi/LakesideBroadleafHiFi",
                "Art/Models/HiFi/LakesideCabinHiFi",
                "Art/Models/HiFi/LakesideMountainHiFi",
                "Art/Models/HiFi/WindEnergyHiFi",
                "Art/Models/HiFi/LakesidePineDetailed",
                "Art/Models/HiFi/LakesideBroadleafDetailed",
                "Art/Models/HiFi/LakesideFenceModule",
            };

            foreach (var path in paths)
            {
                var prefab = Resources.Load<GameObject>(path);
                if (prefab == null)
                {
                    Debug.LogError($"HIFI DIAGNOSTIC missing {path}");
                    continue;
                }

                var renderers = prefab.GetComponentsInChildren<Renderer>(true);
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                var meshes = prefab.GetComponentsInChildren<MeshFilter>(true);
                var triangles = meshes.Sum(filter => filter.sharedMesh == null
                    ? 0
                    : filter.sharedMesh.triangles.Length / 3);
                var materialInfo = string.Join(" | ", renderers
                    .SelectMany(renderer => renderer.sharedMaterials)
                    .Where(material => material != null)
                    .Distinct()
                    .Select(material =>
                        $"{material.name}:{material.shader?.name}:tex={material.mainTexture?.name ?? "none"}:color={material.color}"));
                Debug.Log($"HIFI DIAGNOSTIC {path} renderers={renderers.Length} triangles={triangles} bounds={bounds.size} materials=[{materialInfo}]");
                if (path.EndsWith("RiderBikeHiFi"))
                    Debug.Log("HIFI RIDER NODES " + string.Join(" | ", prefab.GetComponentsInChildren<Transform>(true).Select(item => item.name)));
            }
        }
    }
}
