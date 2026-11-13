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
        Assert.Contains("double __gsharp_out_1 = default;", source);
        Assert.Contains("__gsharp_d_1.CopyToHost(new global::System.Span<double>(ref __gsharp_out_1));", source);
        Assert.Contains("result = __gsharp_out_1;", source);
        Assert.DoesNotContain("\nresult = default;", source);
        Assert.Contains("__gsharp_d_0.CopyToHost(new global::System.Span<int>(ref total));", source);
        Assert.DoesNotContain("__gsharp_d_2.CopyToHost", source);
        Assert.DoesNotContain("__gsharp_d_3.CopyToHost", source);
        Assert.Contains(
            "__gsharp_d_5.CopyToHost(global::Kassyi.GSharp.Runtime.Aot.ModuleKernelArguments.Flatten<float>(grid));",
            source);
        Assert.Contains(
            "__gsharp_d_6?.CopyToHost(global::Kassyi.GSharp.Runtime.Aot.ModuleKernelArguments.Flatten<int>(cube));",
            source);
        Assert.Contains("throw new global::System.ArgumentNullException(\"grid\");", source);
        Assert.DoesNotContain("ArgumentNullException(\"cube\")", source);
        Assert.DoesNotContain("ArgumentNullException(\"values\")", source);
    }

    // The launcher that YAML bindings share with [CudaImport] declares no local that a parameter name can collide with,
    // and names the runtime types from global::, so types of the binding's namespace with the same names change
    // nothing (#1131)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ModuleDefinition_ParameterNamesAndTypesOfTheNamespace_Compile()
    {
        const string Yaml = """
            namespace: MyApp.Native
            className: CollidingKernels
            moduleName: ./native/colliding.fatbin
            methods:
              - name: Run
                parameters:
                  - { name: a, type: "float[]" }
                  - { name: d_a, type: "float[]" }
                  - { name: p_a, type: int }
                  - { name: kernel, type: int }
                  - { name: scope, type: int }
                  - { name: kernelParams, type: int }
                  - { name: result, type: out int }
                  - { name: h_result, type: int }
                  - { name: gridDimX, type: uint }
            """;
        var compilation = RoslynTestUtility.CreateCompilationAt(
                LanguageVersion.Latest,
                ("UserTypes.cs",
                """
            namespace MyApp.Native;
            public struct GpuKernelLoader;
            public struct GpuBuffer<T>;
            public struct CudaException;
            public struct CudaDriverApi;
            public class Lazy<T>;
            public class AppContext;
            """))
            .WithAssemblyName(CreateCompilation().AssemblyName);
        var driver = CreateDriver(Api("colliding.yml", Yaml));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics);
        var errors = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
    }

    // A YAML method named nameof, in a namespace with a nameof segment, still compiles: the null check names the
    // parameter with a string literal, not nameof(<parameter>); a class named var still compiles: every launcher local
    // has an explicit type, which 'var' would otherwise resolve to the class
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("My.nameof", "Kernels", "nameof")]
    [InlineData("My.Native", "var", "Run")]
    public void ModuleDefinition_ContextualKeywordNames_Compile(string namespaceName, string className,
        string methodName)
    {
        var yaml = $$"""
            namespace: {{namespaceName}}
            className: {{className}}
            moduleName: ./native/keyword.fatbin
            methods:
              - name: {{methodName}}
                parameters:
                  - { name: data, type: "float[]" }
                  - { name: result, type: out int }
                  - { name: grid, type: "float[,]?" }
                  - { name: scale, type: float }
                  - { name: gridDimX, type: uint }
            """;
        var compilation = CreateCompilation();
        var driver = CreateDriver(Api("keyword.yml", yaml));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics);
        var errors = GetErrors(output);
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
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
        var compilation = RoslynTestUtility.CreateCompilationAt(
                LanguageVersion.Latest,
                ("UserClass.cs", "namespace MyApp.Native; public sealed class UserClass { }"))
            .WithAssemblyName(CreateCompilation().AssemblyName);
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
        Assert.Contains(", stream, __gsharp_kernel_params,", source);
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
            "__gsharp_hfunc, sharedMemBytes, \"SharedKernels.LaunchShared\");";
        var loadIndex = source.IndexOf(
            $"global::System.IntPtr __gsharp_hfunc = {KernelLoader}.LoadAotKernel(", StringComparison.Ordinal);
        var prepareIndex = source.IndexOf(Prepare, StringComparison.Ordinal);
        var allocationIndex = source.IndexOf($"using {GpuBuffer}<float> __gsharp_d_0 = new(", StringComparison.Ordinal);

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
        Assert.Contains("0, global::System.IntPtr.Zero, __gsharp_kernel_params, global::System.IntPtr.Zero));", source);
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
        var scopeIndex = source.IndexOf(
            $"using {KernelLoader}.CudaContextScope __gsharp_cuda_context_scope = new();",
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
