using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// Pins how the generator assembles the native module (device diagnostics, includes, body order) and how it reports a
/// failed AOT write, ahead of moving that code out of <c>GpuKernelGenerator</c>.
/// </summary>
[Category(TestCategory.Compiler)]
public class NativeModuleAssemblyTests
{
    private static readonly string s_source = RoslynTestUtility.ReadTestCase("Generator/NativeModuleAssembly.cu.cs");

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_DeviceFunctionDiagnostic_IsReported()
    {
        var (_, _, diagnostics) = RoslynTestUtility.RunGenerator(s_source);

        Assert.Contains(diagnostics, static d => d.Id == GSharpDiagnostics.StringNotSupported.Id);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_DeviceFunctionInclude_IsInTheAotSource()
    {
        var aotSource = WriteAotSource("Cuda", ".g.cu");

        Assert.Contains("#include <thrust/sort.h>", aotSource);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CpuDialect_EmitsCalleeBodyBeforeEmbeddingCaller()
    {
        var deviceCode = GetSharedDeviceCode("Cpu");

        // The frame definitions (not the forward declarations) come with the bodies
        var innerFrame = deviceCode.IndexOf("struct __gsharp_frame_NativeModuleTest__Kernels__Inner {", StringComparison.Ordinal);
        var outerFrame = deviceCode.IndexOf("struct __gsharp_frame_NativeModuleTest__Kernels__Outer {", StringComparison.Ordinal);
        Assert.True(innerFrame >= 0 && outerFrame >= 0, deviceCode);
        Assert.True(innerFrame < outerFrame, deviceCode);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_UnwritableIntermediateOutputPath_ReportsOneInfo()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            // A file where the output directory should be makes CreateDirectory fail
            File.WriteAllText(Path.Combine(tempDir, "blocker"), "");
            var options = new Dictionary<string, string>
            {
                { "build_property.IntermediateOutputPath", Path.Combine("blocker", "obj") },
                { "build_property.MSBuildProjectDirectory", tempDir }
            };

            var (_, _, diagnostics) = RoslynTestUtility.RunGenerator(s_source, "Cuda", options);

            var failures = diagnostics.Where(static d => d.Id == GSharpDiagnostics.AotArtifactWriteFailed.Id).ToList();
            var failure = Assert.Single(failures);
            Assert.Equal(DiagnosticSeverity.Info, failure.Severity);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static string GetSharedDeviceCode(string dialect)
    {
        var (_, outputCompilation, _) = RoslynTestUtility.RunGenerator(s_source, dialect);
        var tree = outputCompilation.SyntaxTrees.Single(t => t.FilePath.EndsWith($"GSharpShared{dialect}DeviceCode.g.cs", StringComparison.Ordinal));
        return tree.ToString();
    }

    private static string WriteAotSource(string dialect, string extension)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var options = new Dictionary<string, string>
            {
                { "build_property.IntermediateOutputPath", "." },
                { "build_property.MSBuildProjectDirectory", tempDir }
            };
            RoslynTestUtility.RunGenerator(s_source, dialect, options);
            return File.ReadAllText(Path.Combine(tempDir, $"Kassyi.GSharp.Kernels_MegaTestCompilationBase{extension}"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
