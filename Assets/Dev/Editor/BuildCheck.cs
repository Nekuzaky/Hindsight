using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Nekuzaky.Hindsight.Dev
{
    internal static class BuildCheck
    {
        public const string Output = "Tools/Output/build/HindsightDemo.exe";

        public static void Run()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { DemoSceneBuilder.ScenePath },
                locationPathName = Output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[BuildCheck] {summary.result}: {summary.totalErrors} errors, {summary.totalWarnings} warnings, {summary.totalSize / 1048576d:0.0} MB in {summary.totalTime.TotalSeconds:0} s");
            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
