#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityCliConnector;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

[UnityCliTool(Name = "mp_build_player", Description = "Build an MDF player for multiplayer harness runs. Development Build is enabled by default.")]
public static class MPBuildPlayerTool
{
    private static readonly string[] RequiredScenes =
    {
        "Assets/Scenes/Title.unity",
        "Assets/Scenes/MatchingLobby.unity",
        "Assets/Scenes/JoinLobby.unity",
        "Assets/Scenes/Game.unity"
    };

    public class Parameters
    {
        [ToolParameter("Output directory. Default: artifacts/builds/<timestamp>")]
        public string OutputDir { get; set; }

        [ToolParameter("Build target enum name. Default: active Editor build target")]
        public string BuildTarget { get; set; }

        [ToolParameter("Executable name. Default: MDF-MPTest")]
        public string PlayerName { get; set; }

        [ToolParameter("Include Development Build. Default: true")]
        public bool DevelopmentBuild { get; set; }

        [ToolParameter("Include AllowDebugging. Default: true")]
        public bool AllowDebugging { get; set; }
    }

    public static object HandleCommand(JObject parameters)
    {
        var p = new ToolParams(parameters ?? new JObject());
        var target = ResolveBuildTarget(p.Get("build_target"));
        var outputDir = ResolveOutputDir(p.Get("output_dir"));
        var playerName = p.Get("player_name", "MDF-MPTest");
        var developmentBuild = p.GetBool("development_build", true);
        var allowDebugging = p.GetBool("allow_debugging", true);
        var scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            return new ErrorResponse("No enabled scenes in EditorBuildSettings.");
        }

        var missingRequiredScenes = RequiredScenes.Except(scenes).ToArray();
        if (missingRequiredScenes.Length > 0)
        {
            return new ErrorResponse("Required multiplayer scenes are not enabled in EditorBuildSettings.", new { missingRequiredScenes, scenes });
        }

        Directory.CreateDirectory(outputDir);
        var locationPathName = Path.Combine(outputDir, BuildExecutableName(target, playerName));
        var options = developmentBuild ? BuildOptions.Development : BuildOptions.None;
        var effectiveAllowDebugging = developmentBuild && allowDebugging;
        if (effectiveAllowDebugging)
        {
            options |= BuildOptions.AllowDebugging;
        }

        var buildOptions = new BuildPlayerOptions
        {
            scenes = scenes,
            target = target,
            locationPathName = locationPathName,
            options = options
        };

        var report = BuildPipeline.BuildPlayer(buildOptions);
        var summary = report.summary;
        var metadata = new
        {
            result = summary.result.ToString(),
            target = target.ToString(),
            outputPath = locationPathName,
            outputDir,
            totalSize = summary.totalSize,
            totalTimeSeconds = summary.totalTime.TotalSeconds,
            options = options.ToString(),
            scenes,
            unityVersion = Application.unityVersion,
            timestampUtc = DateTime.UtcNow.ToString("o"),
            developmentBuild = (options & BuildOptions.Development) != 0,
            allowDebugging = (options & BuildOptions.AllowDebugging) != 0,
            companyName = Application.companyName,
            productName = Application.productName,
            playerLogPath = ResolvePlayerLogPath()
        };

        var metadataPath = Path.Combine(outputDir, "build-metadata.json");
        File.WriteAllText(metadataPath, JsonConvert.SerializeObject(metadata, Formatting.Indented));

        if (summary.result == BuildResult.Succeeded)
        {
            return new SuccessResponse("Player build succeeded.", new { metadata, metadataPath });
        }

        return new ErrorResponse("Player build failed.", new { metadata, metadataPath });
    }

    private static BuildTarget ResolveBuildTarget(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return EditorUserBuildSettings.activeBuildTarget;
        }

        if (Enum.TryParse(raw, true, out BuildTarget target))
        {
            return target;
        }

        throw new ArgumentException($"Unknown BuildTarget '{raw}'.");
    }

    private static string ResolveOutputDir(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = Path.Combine("artifacts", "builds", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        }

        if (Path.IsPathRooted(raw))
        {
            return Path.GetFullPath(raw);
        }

        var repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        return Path.GetFullPath(Path.Combine(repoRoot, raw));
    }

    private static string BuildExecutableName(BuildTarget target, string playerName)
    {
        switch (target)
        {
            case BuildTarget.StandaloneWindows:
            case BuildTarget.StandaloneWindows64:
                return playerName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? playerName : playerName + ".exe";
            case BuildTarget.StandaloneOSX:
                return playerName.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? playerName : playerName + ".app";
            default:
                return playerName;
        }
    }

    private static string ResolvePlayerLogPath()
    {
        if (Application.platform == RuntimePlatform.WindowsEditor)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string localLow = Path.GetFullPath(Path.Combine(localAppData, "..", "LocalLow"));
            return Path.Combine(localLow, Application.companyName, Application.productName, "Player.log");
        }

        return string.Empty;
    }
}
#endif
