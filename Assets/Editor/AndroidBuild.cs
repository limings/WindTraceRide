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
            BuildApk("Builds/Android/WindTraceRide-dev.apk", BuildOptions.Development);
        }

        public static void BuildPreviewApk()
        {
            var keystore = ReadSigningVariable("WIND_TRACE_RIDE_KEYSTORE");
            var password = ReadSigningVariable("WIND_TRACE_RIDE_KEYSTORE_PASSWORD");
            var alias = ReadSigningVariable("WIND_TRACE_RIDE_KEY_ALIAS");
            var keyPassword = ReadSigningVariable("WIND_TRACE_RIDE_KEY_PASSWORD");
            if (!System.IO.File.Exists(keystore))
                throw new InvalidOperationException("The Android signing keystore was not found.");

            var previousVersion = PlayerSettings.bundleVersion;
            var previousVersionCode = PlayerSettings.Android.bundleVersionCode;
            var previousKeystore = PlayerSettings.Android.keystoreName;
            var previousPassword = PlayerSettings.Android.keystorePass;
            var previousAlias = PlayerSettings.Android.keyaliasName;
            var previousKeyPassword = PlayerSettings.Android.keyaliasPass;
            var previousCustomSigning = PlayerSettings.Android.useCustomKeystore;

            try
            {
                PlayerSettings.bundleVersion = "1.0.0-preview.1";
                PlayerSettings.Android.bundleVersionCode = 3;
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = keystore;
                PlayerSettings.Android.keystorePass = password;
                PlayerSettings.Android.keyaliasName = alias;
                PlayerSettings.Android.keyaliasPass = keyPassword;

                BuildApk("Builds/Android/WindTraceRide-1.0.0-preview.1.apk", BuildOptions.None);
            }
            finally
            {
                PlayerSettings.bundleVersion = previousVersion;
                PlayerSettings.Android.bundleVersionCode = previousVersionCode;
                PlayerSettings.Android.keystoreName = previousKeystore;
                PlayerSettings.Android.keystorePass = previousPassword;
                PlayerSettings.Android.keyaliasName = previousAlias;
                PlayerSettings.Android.keyaliasPass = previousKeyPassword;
                PlayerSettings.Android.useCustomKeystore = previousCustomSigning;
                AssetDatabase.SaveAssets();
            }
        }

        private static void BuildApk(string defaultOutput, BuildOptions buildOptions)
        {
            AppIconConfiguration.Apply();
            ReferenceWorldValidation.PrepareShaders();
            var output = ReadArgument("-buildOutput") ?? defaultOutput;
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
                options = buildOptions
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Android build failed: {report.summary.result}");

            Debug.Log($"Wind Trace Ride Android build: {report.summary.outputPath}");
        }

        private static string ReadSigningVariable(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"Set {name} before building the Android preview.");
            return value;
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
