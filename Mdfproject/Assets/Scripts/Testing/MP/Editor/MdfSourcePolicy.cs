#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Single, auditable escape hatch for tests that must verify a source/serialized-text
/// contract which cannot be observed through a runtime API. Prefer runtime calls,
/// reflection, AssetDatabase or SerializedObject in every other test.
/// </summary>
internal static class MdfSourcePolicy
{
    private static readonly string[] DirectSourceReadTokens =
    {
        "File." + "ReadAllText",
        "File." + "ReadAllLines",
        "File." + "ReadLines",
        "File." + "OpenText",
        "new " + "StreamReader("
    };

    internal static string ReadStaticContract(string projectRelativePath)
    {
        if (string.IsNullOrWhiteSpace(projectRelativePath))
        {
            throw new ArgumentException("A project-relative source path is required.", nameof(projectRelativePath));
        }

        string normalized = NormalizeAllowedPath(projectRelativePath);
        bool isAsset = normalized.StartsWith("Assets/", StringComparison.Ordinal);
        bool isHarness = normalized.StartsWith("../tools/harness/", StringComparison.Ordinal);
        if (!isAsset && !isHarness)
        {
            throw new InvalidOperationException(
                $"Static contract reads are limited to MDF Assets or tools/harness: {projectRelativePath}");
        }

        string absolutePath = Path.GetFullPath(normalized);
        string allowedRoot = isAsset
            ? Path.GetFullPath(Application.dataPath)
            : Path.GetFullPath("../tools/harness");
        RejectReparsePointTraversal(absolutePath, allowedRoot);

        if (isAsset && normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(normalized);
            if (script == null)
            {
                throw new FileNotFoundException($"MDF script asset was not found: {normalized}", normalized);
            }

            return script.text;
        }

        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException($"MDF static contract file was not found: {normalized}", absolutePath);
        }

        return File.ReadAllText(absolutePath);
    }

    private static void RejectReparsePointTraversal(string absolutePath, string allowedRoot)
    {
        string root = Path.GetFullPath(allowedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string candidate = Path.GetFullPath(absolutePath);
        var pathComparison = Application.platform == RuntimePlatform.WindowsEditor
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.Equals(root, pathComparison) &&
            !candidate.StartsWith(root + Path.DirectorySeparatorChar, pathComparison) &&
            !candidate.StartsWith(root + Path.AltDirectorySeparatorChar, pathComparison))
        {
            throw new InvalidOperationException($"Static contract path escaped its allowlisted root: {absolutePath}");
        }

        var pathsToInspect = new List<string> { root };
        string relative = candidate.Substring(root.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string current = root;
        foreach (string segment in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            pathsToInspect.Add(current);
        }

        foreach (string path in pathsToInspect)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                continue;
            }

            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"Static contract reads may not traverse a symbolic link, junction, or reparse point: {path}");
            }
        }
    }

    private static string NormalizeAllowedPath(string path)
    {
        string normalized = path.Replace('\\', '/');
        string absolute = Path.GetFullPath(normalized).Replace('\\', '/');
        string assetsRoot = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
        if (absolute.StartsWith(assetsRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            return "Assets/" + absolute.Substring(assetsRoot.Length + 1);
        }

        string harnessRoot = Path.GetFullPath("../tools/harness").Replace('\\', '/').TrimEnd('/');
        if (absolute.StartsWith(harnessRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            return "../tools/harness/" + absolute.Substring(harnessRoot.Length + 1);
        }

        // Returning the canonical absolute path makes the caller reject paths outside both
        // allowlisted roots, including relative "../" traversal that merely starts with an
        // allowed-looking prefix.
        return absolute;
    }

    [Test]
    public static void MdfOwnedTestsDoNotReadSourceOutsideCentralPolicy()
    {
        string[] roots =
        {
            "Assets/Scripts/Editor",
            "Assets/Scripts/Testing",
            "Assets/Tests"
        };

        string policyPath = "Assets/Scripts/Testing/MP/Editor/MdfSourcePolicy.cs";
        string[] scriptPaths = AssetDatabase.FindAssets("t:MonoScript", roots)
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (string normalized in scriptPaths)
        {
            if (normalized.Equals(policyPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string source = ReadStaticContract(normalized);
            if (!IsOwnedTestSource(normalized, source))
            {
                continue;
            }

            foreach (string forbidden in DirectSourceReadTokens)
            {
                Assert.That(source, Does.Not.Contain(forbidden),
                    $"{normalized} must use {nameof(MdfSourcePolicy)}.{nameof(ReadStaticContract)} so static contracts stay auditable.");
            }
        }
    }

    private static bool IsOwnedTestSource(string assetPath, string source)
    {
        if (assetPath.StartsWith("Assets/Tests/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return source.Contains("NUnit.Framework") ||
               source.Contains("[Test]") ||
               source.Contains("[TestCase") ||
               source.Contains("[UnityTest]");
    }

    [Test]
    public static void OwnedTestDetectionDoesNotDependOnTestsFileNameSuffix()
    {
        const string disguisedTest = "using NUnit.Framework; public class ContractProbe { [Test] public void Reads() {} }";
        Assert.That(IsOwnedTestSource("Assets/Scripts/Testing/MP/Editor/ContractProbe.cs", disguisedTest), Is.True);
        Assert.That(IsOwnedTestSource("Assets/Tests/PlayMode/RuntimeProbe.cs", "public class RuntimeProbe {}"), Is.True);
        Assert.That(IsOwnedTestSource("Assets/Scripts/Editor/ProductionImporter.cs", "public class ProductionImporter {}"), Is.False);
    }

    [Test]
    public static void StaticContractPolicyRejectsTraversalOutsideAllowlistedRoots()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ReadStaticContract("Assets/../ProjectSettings/ProjectSettings.asset"));
        Assert.Throws<InvalidOperationException>(() =>
            ReadStaticContract("../tools/harness/../../AGENTS.md"));
    }

    [Test]
    public static void StaticContractPolicyRejectsReparsePointTraversal()
    {
#if UNITY_EDITOR_WIN
        string harnessRoot = Path.GetFullPath("../tools/harness");
        string target = Path.GetFullPath("../docs");
        string junction = Path.Combine(harnessRoot, $".mdf-source-policy-junction-{Guid.NewGuid():N}");
        Assert.That(Directory.Exists(target), Is.True, target);

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/d /c mklink /J \"{junction}\" \"{target}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            using (var process = System.Diagnostics.Process.Start(startInfo))
            {
                Assert.That(process, Is.Not.Null);
                process.WaitForExit();
                Assert.That(process.ExitCode, Is.EqualTo(0), process.StandardError.ReadToEnd());
            }

            string relativeJunction = "../tools/harness/" + Path.GetFileName(junction) + "/ai-harness/index.md";
            var error = Assert.Throws<InvalidOperationException>(() => ReadStaticContract(relativeJunction));
            Assert.That(error.Message, Does.Contain("reparse point"));
        }
        finally
        {
            if (Directory.Exists(junction))
            {
                new DirectoryInfo(junction).Delete();
            }
        }
#else
        Assert.Ignore("The actual junction regression currently runs on the MDF Windows editor environment.");
#endif
    }
}
#endif
