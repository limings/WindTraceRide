using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace WindTraceRide.Editor
{
    public static class AndroidBuild
    {
        public static void BuildDevelopmentApk()
        {
            ReferenceWorldValidation.PrepareShaders();
            var output = ReadArgument("-buildOutput") ?? "Builds/Android/WindTraceRide-dev.apk";
            PlayerSettings.companyName = "Wind Trace Studio";
            PlayerSettings.productName = "Wind Trace Ride";
#pragma warning disable CS0618
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.windtraceride.game");
#pragma warning restore CS0618
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel23;

            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0) throw new InvalidOperationException("No enabled scenes in Build Settings.");

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Android build failed: {report.summary.result}");

            Debug.Log($"Wind Trace Ride Android build: {report.summary.outputPath}");
        }

        private static string ReadArgument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var index = 0; index < args.Length - 1; index++)
            {
                if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                    return args[index + 1];
            }
            return null;
        }
    }
}
