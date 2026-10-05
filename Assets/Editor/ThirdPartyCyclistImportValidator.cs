using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WindTraceRide.Editor
{
    public static class ThirdPartyCyclistImportValidator
    {
        private static readonly string[] ExpectedMotionTokens =
        {
            "pedal", "idle", "brak"
        };

        [MenuItem("Tools/Wind Trace Ride/Validate Imported Cyclist")]
        public static void ValidateImportedCyclist()
        {
            var modelGuids = AssetDatabase.FindAssets("cyclist t:Model");
            var prefabGuids = AssetDatabase.FindAssets("cyclist t:Prefab");
            var candidatePaths = modelGuids.Concat(prefabGuids)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .ToArray();

            if (candidatePaths.Length == 0)
            {
                Debug.LogWarning(
                    "No imported cyclist model was found. Import the licensed package, " +
                    "then run this validator again.");
                return;
            }

            var problems = new List<string>();
            var clipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hasSkinnedRider = false;
            var hasFrontWheel = false;
            var hasRearWheel = false;
            var hasFrame = false;

            foreach (var path in candidatePaths)
            {
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                        clipNames.Add(clip.name);
                }

                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) continue;
                hasSkinnedRider |= root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0;

                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                {
                    var normalized = child.name.Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
                    hasFrontWheel |= normalized.Contains("frontwheel");
                    hasRearWheel |= normalized.Contains("rearwheel");
                    hasFrame |= normalized.Contains("frame");
                }
            }

            if (!hasSkinnedRider) problems.Add("no skinned rider mesh was found");
            if (!hasFrontWheel) problems.Add("FrontWheel node was not found");
            if (!hasRearWheel) problems.Add("RearWheel node was not found");
            if (!hasFrame) problems.Add("a separate bicycle frame node was not found");
            foreach (var token in ExpectedMotionTokens)
            {
                if (!clipNames.Any(name => name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
                    problems.Add($"no animation clip containing '{token}' was found");
            }

            var summary =
                $"Cyclist import scan: {candidatePaths.Length} candidate assets, " +
                $"{clipNames.Count} animation clips.";
            if (problems.Count == 0)
            {
                Debug.Log(summary + " Required structure and core clips are present.");
                return;
            }

            Debug.LogError(summary + " Problems: " + string.Join("; ", problems));
        }
    }
}
