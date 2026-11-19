#nullable enable
using System.Diagnostics;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Tasks.Helpers;
using Kassyi.GSharp.Compiler.Tasks.Toolchains;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// Preprocesses the generated CUDA prelude's <c>__CUDACC_VER_MAJOR__</c> guard with <c>nvcc -E</c> to verify that
/// releases below the minimum fail with the toolkit error while the minimum release passes preprocessing.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
[Category(TestCategory.Compiler)]
public sealed class PreludeNvccGuardTests
{
    private const string NvccToolName = "nvcc";

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Guard_MajorBelowMinimum_FailsPreprocessingWithToolkitError()
    {
        var result = RunPreprocess(majorReleaseOverride: 12);

        Assert.NotEqual(0, result.ExitCode);
        var combined = result.StandardOutput + Environment.NewLine + result.StandardError;
        Assert.Contains("G# needs the CUDA Toolkit 13.0", combined, StringComparison.Ordinal);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Guard_MajorAtMinimum_SucceedsPreprocessing()
    {
        var result = RunPreprocess(majorReleaseOverride: 13);

        Assert.True(
            result.ExitCode == 0,
            $"nvcc -E exited with {result.ExitCode}:{Environment.NewLine}" +
            $"{result.StandardOutput}{Environment.NewLine}{result.StandardError}");
    }

    private static ProcessResult RunPreprocess(int majorReleaseOverride)
    {
        Skip.If(
            Environment.GetEnvironmentVariable("GSHARP_TEST_NO_CUDA") == "1",
            "GSHARP_TEST_NO_CUDA=1 skips nvcc.");
        Skip.IfNot(IsNvccAvailable(), "nvcc is not available.");

        Dictionary<string, string>? vcvarsEnv = null;
        if (OperatingSystem.IsWindows())
        {
            vcvarsEnv = new MsvcToolchain(ProcessRunner.Default).GetVcvarsEnvironment("x64");
            Skip.If(vcvarsEnv is null, "MSVC (vcvarsall.bat) is not available.");
        }

        var guard = ExtractGuardPrefix(GSharpPreludeResources.Cuda);
        var directory = Path.Combine(Path.GetTempPath(), "gsharp-prelude-nvcc-guard-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            var sourcePath = Path.Combine(directory, "guard_probe.cu");
            var source =
                "#undef __CUDACC_VER_MAJOR__\n" +
                $"#define __CUDACC_VER_MAJOR__ {majorReleaseOverride}\n" +
                guard + "\n";
            File.WriteAllText(sourcePath, source);

            var arguments = OperatingSystem.IsWindows()
                ? $"-std=c++17 -Xcompiler \"/Zc:preprocessor /wd4819\" -E \"{sourcePath}\""
                : $"-std=c++17 -E \"{sourcePath}\"";
            var startInfo = new ProcessStartInfo
            {
                FileName = NvccToolName,
                Arguments = arguments
            };
            if (vcvarsEnv is not null)
            {
                foreach (var entry in vcvarsEnv)
                {
                    startInfo.EnvironmentVariables[entry.Key] = entry.Value;
                }
            }

            return ProcessRunner.Default.Run(startInfo);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string ExtractGuardPrefix(string cudaPrelude)
    {
        const string GuardStart = "#if !defined(__CUDACC_VER_MAJOR__)";
        const string EndIf = "#endif";
        Assert.StartsWith(GuardStart, cudaPrelude, StringComparison.Ordinal);
        var span = cudaPrelude.AsSpan();
        var endIfIndex = span.IndexOf(EndIf, StringComparison.Ordinal);
        Assert.True(endIfIndex >= 0, "Expected #endif closing the __CUDACC_VER_MAJOR__ guard.");
        return span[..(endIfIndex + EndIf.Length)].ToString();
    }

    private static bool IsNvccAvailable()
    {
        try
        {
            var result = ProcessRunner.Default.Run(new ProcessStartInfo
            {
                FileName = NvccToolName,
                Arguments = "--version"
            });
            return result.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            _ = ex.Message;
            return false;
        }
    }
}
