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

    // The analyzer reports a device function's error; the generator reports it no more
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DeviceFunctionDiagnostic_IsReported()
    {
        var diagnostics = await KernelDiagnostics.RunAsync(s_source, "Cuda");

        Assert.Equal(
            [(DiagnosticOrigin.Analyzer, 12, 13, 12, 19)],
            diagnostics.Ranges(GSharpDiagnostics.StringNotSupported.Id));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_DeviceFunctionSort_CallsThePreludeWithoutInclude()
    {
        var aotSource = WriteAotSource("Cuda", ".g.cu");

        // MemoryExtensions.Sort calls the Prelude (#625), so the device function needs no include of its own any more
        Assert.Contains("__gsharp_sort(", aotSource);
        Assert.DoesNotContain("#include <thrust/sort.h>", aotSource);
    }

    // A line break in a reason (a message argument) would end the directive and leave the rest as C++ code
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_TranspileFailureWithLineBreaks_StaysOnOneErrorLine()
    {
        var source = Kassyi.GSharp.Compiler.Aot.NativeModuleAssembler.FormatTranspileFailures(
            ["k.cs(1,2): GSH0003: first\r\nsecond\nthird \"quoted\" C:\\dir", "k.cs(3,4): GSH0001: other"]);

        Assert.Equal(
            "#error \"k.cs(1,2): GSH0003: first  second third \\\"quoted\\\" C:\\\\dir\"" + Environment.NewLine +
            "#error \"k.cs(3,4): GSH0001: other\"" + Environment.NewLine,
            source);
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
