#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Generators.NativeApiGeneratorTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Generators;

[Category(TestCategory.Compiler)]
public class NativeApiModuleGeneratorTests
{
    private const string DriverApi = "global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi";
    private const string KernelLoader = "global::Kassyi.GSharp.Runtime.Aot.GpuKernelLoader";
    private const string GpuBuffer = "global::Kassyi.GSharp.Runtime.GpuBuffer";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task ModuleDefinition_GeneratesKernelLaunchers()
    {
        var result = RunGenerator(Api("kernels.yml", ModuleYaml));

        Assert.Empty(result.Diagnostics);
        return VerifyGenerated(result, nameof(ModuleDefinition_GeneratesKernelLaunchers));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task ModuleDefinition_WithDefaultValue_GeneratesDefaultedParameters()
    {
        const string Yaml = """
            namespace: MyApp.Native
            className: DefaultedKernels
            moduleName: ./native/defaulted.fatbin
            methods:
              - name: Scale
                parameters:
                  - { name: data, type: "float[]" }
                  - { name: factor, type: float, defaultValue: 2.0f }
            """;

        var result = RunGenerator(Api("defaulted.yml", Yaml));

        Assert.Empty(result.Diagnostics);
        return VerifyGenerated(result, nameof(ModuleDefinition_WithDefaultValue_GeneratesDefaultedParameters));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GeneratedSources_CompileWithoutErrors()
    {
        var compilation = CreateCompilation();
        var driver = CreateDriver(Api("mylib.yml", LibraryYaml), Api("kernels.yml", ModuleYaml));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics);
        var errors = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
    }

    // The forms the launcher stages through device buffers compile without a warning: values passed by 'ref', 'out',
    // 'in' and 'ref readonly' (an 'out' value is set before the launcher reads it), multidimensional and nullable
    // arrays, the latter also with 'params'
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ModuleDefinition_StagedParameters_CompileWithoutWarnings()
    {
        const string Yaml = """
            namespace: MyApp.Native
            className: StagedKernels
            moduleName: ./native/staged.fatbin
            methods:
              - name: Run
                parameters:
                  - { name: total, type: ref int }
                  - { name: result, type: out double }
                  - { name: seed, type: in uint }
                  - { name: limit, type: ref readonly long }
                  - { name: scale, type: scoped ref float }
                  - { name: grid, type: "float[,]" }
                  - { name: cube, type: "int[ , , ]?" }
                  - { name: values, type: "params float[]?" }
              - name: Optional
                parameters:
                  - { name: data, type: "float[]?" }
                  - { name: gridDimX, type: uint }
            """;
        var compilation = CreateCompilation();
        var driver = CreateDriver(Api("staged.yml", Yaml));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics);
        var problems = output.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Select(d => d.ToString())
            .ToArray();
        Assert.True(problems.Length == 0, string.Join(Environment.NewLine, problems));
        var source = Assert.Single(output.SyntaxTrees, t => t.FilePath.EndsWith("StagedKernels.NativeApi.g.cs"))
            .ToString();
        Assert.Contains("result = default;", source);
        Assert.Contains("d_total.CopyToHost(new global::System.Span<int>(ref total));", source);
        Assert.DoesNotContain("d_seed.CopyToHost", source);
        Assert.DoesNotContain("d_limit.CopyToHost", source);
        Assert.Contains(
            "d_grid.CopyToHost(global::Kassyi.GSharp.Runtime.Aot.ModuleKernelArguments.Flatten<float>(grid));",
            source);
        Assert.Contains(
            "d_cube?.CopyToHost(global::Kassyi.GSharp.Runtime.Aot.ModuleKernelArguments.Flatten<int>(cube));",
            source);
        Assert.Contains("throw new global::System.ArgumentNullException(nameof(grid));", source);
        Assert.DoesNotContain("nameof(cube)", source);
        Assert.DoesNotContain("nameof(values)", source);
    }

    // A class named in YAML cannot be told from a struct without the compilation; the launcher takes the address of
    // the argument through a method constrained to unmanaged types, so a class is a compile error in the generated
    // launcher instead of reaching the kernel as the host address of the reference (#1417)
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Action")]
    [InlineData("System.Text.StringBuilder")]
    [InlineData("MyApp.Native.UserClass")]
    public void ModuleDefinition_ClassTypedValueParameter_FailsToCompileInLauncher(string type)
    {
        var yaml = $$"""
            namespace: MyApp.Native
            className: ClassKernels
            moduleName: ./native/class.fatbin
            methods:
              - name: Run
                parameters:
                  - { name: data, type: "float[]" }
                  - { name: callback, type: "{{type}}" }
            """;
        var userClass = CSharpSyntaxTree.ParseText("namespace MyApp.Native; public sealed class UserClass { }",
            new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CreateCompilation().AddSyntaxTrees(userClass);
        var driver = CreateDriver(Api("class.yml", yaml));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics);
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        var error = Assert.Single(errors);
        Assert.Equal("CS8377", error.Id);
        Assert.EndsWith("MyApp.Native.ClassKernels.NativeApi.g.cs", error.Location.SourceTree?.FilePath);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ModuleDefinition_WithStreamParameter_EmitsCuStreamSynchronize()
    {
        const string Yaml = """
            namespace: MyApp.Native
            className: StreamKernels
            moduleName: ./native/stream.fatbin
            methods:
              - name: LaunchWithStream
                parameters:
                  - { name: data, type: "float[]" }
                  - { name: stream, type: IntPtr }
            """;

        var result = RunGenerator(Api("stream.yml", Yaml));

        Assert.Empty(result.Diagnostics);
        var source = FindSource(result, "StreamKernels");
        Assert.Contains($"{DriverApi}.cuStreamSynchronize(stream)", source);
        Assert.DoesNotContain("cuCtxSynchronize", source);
        Assert.Contains(", stream, kernelParams,", source);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ModuleDefinition_WithoutStreamParameter_EmitsCuCtxSynchronize()
    {
        const string Yaml = """
            namespace: MyApp.Native
            className: NoStreamKernels
            moduleName: ./native/nostream.fatbin
            methods:
              - name: LaunchWithoutStream
                parameters:
                  - { name: data, type: "float[]" }
            """;

        var result = RunGenerator(Api("nostream.yml", Yaml));

        Assert.Empty(result.Diagnostics);
        var source = FindSource(result, "NoStreamKernels");
        Assert.Contains($"{DriverApi}.cuCtxSynchronize()", source);
        Assert.DoesNotContain("cuStreamSynchronize", source);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ModuleDefinition_WithStreamParameterAndArray_StreamSynchronizePrecedesCopyToHost()
    {
        const string Yaml = """
            namespace: MyApp.Native
            className: CopyOutKernels
            moduleName: ./native/copyout.fatbin
            methods:
              - name: LaunchCopyOutStream
                parameters:
                  - { name: data, type: "float[]" }
                  - { name: stream, type: IntPtr }
            """;

        var result = RunGenerator(Api("copyout.yml", Yaml));

        Assert.Empty(result.Diagnostics);
        var source = FindSource(result, "CopyOutKernels");
        var syncIndex = source.IndexOf($"{DriverApi}.cuStreamSynchronize(stream)");
        var copyIndex = source.IndexOf(".CopyToHost(");

        Assert.NotEqual(-1, syncIndex);
        Assert.NotEqual(-1, copyIndex);
        Assert.True(syncIndex < copyIndex, "cuStreamSynchronize(stream) must appear before CopyToHost");
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ModuleDefinition_WithSharedMemBytes_PreparesDynamicSharedMemoryBeforeAllocations()
    {
        const string Yaml = """
            namespace: MyApp.Native
            className: SharedKernels
            moduleName: ./native/shared.fatbin
            methods:
              - name: LaunchShared
                parameters:
                  - { name: data, type: "float[]" }
                  - { name: blockDimX, type: uint }
                  - { name: sharedMemBytes, type: uint }
            """;

        var result = RunGenerator(Api("shared.yml", Yaml));

        Assert.Empty(result.Diagnostics);
        var source = FindSource(result, "SharedKernels");
        const string Prepare = "global::Kassyi.GSharp.Runtime.Aot.DynamicSharedMemoryOptIn.Prepare(" +
            "kernel, sharedMemBytes, \"SharedKernels.LaunchShared\");";
        var loadIndex = source.IndexOf($"var kernel = {KernelLoader}.LoadAotKernel(", StringComparison.Ordinal);
        var prepareIndex = source.IndexOf(Prepare, StringComparison.Ordinal);
        var allocationIndex = source.IndexOf($"new {GpuBuffer}<float>(", StringComparison.Ordinal);

        Assert.NotEqual(-1, loadIndex);
        Assert.True(loadIndex < prepareIndex, "Prepare must follow the function lookup");
        Assert.True(prepareIndex < allocationIndex, "Prepare must precede the device allocations");
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ModuleDefinition_WithoutSharedMemBytes_DoesNotPrepareDynamicSharedMemory()
    {
        const string Yaml = """
            namespace: MyApp.Native
            className: NoSharedKernels
            moduleName: ./native/noshared.fatbin
            methods:
              - name: LaunchNoShared
                parameters:
                  - { name: data, type: "float[]" }
            """;

        var result = RunGenerator(Api("noshared.yml", Yaml));

        Assert.Empty(result.Diagnostics);
        var source = FindSource(result, "NoSharedKernels");
        Assert.DoesNotContain("DynamicSharedMemoryOptIn", source);
        Assert.Contains("0, global::System.IntPtr.Zero, kernelParams, global::System.IntPtr.Zero));", source);
    }

    // The module launcher checks its dimensions against the limits of every GPU (#799) before the context, the
    // function lookup and the device allocations; a dimension it does not take is the 1 it launches with
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ModuleDefinition_WithLaunchDimensions_ChecksLaunchLimitsFirst()
    {
        const string Yaml = """
            namespace: MyApp.Native
            className: GridKernels
            moduleName: ./native/grid.fatbin
            methods:
              - name: LaunchGrid
                parameters:
                  - { name: data, type: "float[]" }
                  - { name: gridDimX, type: uint }
                  - { name: blockDimX, type: uint }
                  - { name: blockDimZ, type: uint }
            """;

        var result = RunGenerator(Api("grid.yml", Yaml));

        Assert.Empty(result.Diagnostics);
        var source = FindSource(result, "GridKernels");
        const string Check = "global::Kassyi.GSharp.Runtime.LaunchLimits.CheckLaunch(" +
            "gridDimX, 1, 1, blockDimX, 1, blockDimZ, \"GridKernels.LaunchGrid\");";
        var checkIndex = source.IndexOf(Check, StringComparison.Ordinal);
        var scopeIndex = source.IndexOf($"using var scope = new {KernelLoader}.CudaContextScope();",
            StringComparison.Ordinal);

        Assert.NotEqual(-1, checkIndex);
        Assert.True(checkIndex < scopeIndex, "The launch check must precede the context and the function lookup");
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ModuleDefinition_WithoutLaunchDimensions_EmitsNoLaunchCheck()
    {
        const string Yaml = """
            namespace: MyApp.Native
            className: SingleThreadKernels
            moduleName: ./native/single.fatbin
            methods:
              - name: LaunchSingle
                parameters:
                  - { name: data, type: "float[]" }
                  - { name: sharedMemBytes, type: uint }
            """;

        var result = RunGenerator(Api("single.yml", Yaml));

        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain("LaunchLimits", FindSource(result, "SingleThreadKernels"));
    }
}
