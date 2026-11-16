namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// Verifies that overloaded kernels get distinct entry points and host members, and that overloads whose launch
/// wrappers cannot be told apart are reported instead of breaking the generated code.
/// </summary>
[Category(TestCategory.Compiler)]
public class KernelOverloadTests
{
    private static readonly string s_overloadedKernels =
        RoslynTestUtility.ReadTestCase("Generator/KernelOverload_Overloaded.cu.cs");

    // Both overloads take one int buffer in the typed Ptr wrapper.
    private static readonly string s_indistinguishableKernels =
        RoslynTestUtility.ReadTestCase("Generator/KernelOverload_Indistinguishable.cu.cs");

    // The overloads differ only by in, which Launch{Name} drops.
    private static readonly string s_inModifierKernels =
        RoslynTestUtility.ReadTestCase("Generator/KernelOverload_InModifier.cu.cs");

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void OverloadedKernels_ValidOverloads_GeneratesCompilableWrappers(string dialect)
    {
        var (_, output, diagnostics) = GeneratorTestRunner.RunGenerator(s_overloadedKernels, dialect);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void OverloadedKernels_DistinctSignatures_LoadsDistinctEntryPoints()
    {
        var (_, output, _) = GeneratorTestRunner.RunGenerator(s_overloadedKernels, "Cuda");
        var wrapper = output.SyntaxTrees.Single(t => t.FilePath.EndsWith("K.g.cs", StringComparison.Ordinal)).ToString();

        Assert.Contains("\"__gsharp_k_OverloadTest__K__Run_o0\"", wrapper);
        Assert.Contains("\"__gsharp_k_OverloadTest__K__Run_o1\"", wrapper);
        Assert.Contains("LaunchRunPtr(global::Kassyi.GSharp.Runtime.GpuBuffer<global::System.Int32>", wrapper);
        Assert.Contains("LaunchRunPtr(global::Kassyi.GSharp.Runtime.GpuBuffer<global::System.Single>", wrapper);
    }

    // The CPU launchers load each overload's own native launcher, the typed pointer launchers included
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void OverloadedKernels_CpuDialect_LoadsDistinctEntryPointsAndTypedPointerLaunchers()
    {
        var (_, output, _) = GeneratorTestRunner.RunGenerator(s_overloadedKernels, "Cpu");
        var wrapper = output.SyntaxTrees.Single(t => t.FilePath.EndsWith("K.g.cs", StringComparison.Ordinal)).ToString();

        Assert.Contains("LoadAotKernelPtr(\"__gsharp_launch_OverloadTest__K__Run_o0\"", wrapper);
        Assert.Contains("LoadAotKernelPtr(\"__gsharp_launch_OverloadTest__K__Run_o1\"", wrapper);
        Assert.Contains("public static unsafe void LaunchRunPtr(global::Kassyi.GSharp.Runtime.GpuBuffer<global::System.Int32>", wrapper);
        Assert.Contains("public static unsafe void LaunchRunPtr(global::Kassyi.GSharp.Runtime.GpuBuffer<global::System.Single>", wrapper);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public async Task IndistinguishableOverloads_SameTypeSignatures_ReportsGSH0059(string dialect)
    {
        // The analyzer reports the later overload at its name; the generator reports nothing
        var diagnostics = await KernelDiagnostics.RunAsync(s_indistinguishableKernels, dialect);
        Assert.Equal(
            [(DiagnosticOrigin.Analyzer, 12, 28, 12, 31)],
            diagnostics.Ranges(GSharpDiagnostics.IndistinguishableKernelOverloads.Id));

        var (_, output, _) = GeneratorTestRunner.RunGenerator(s_indistinguishableKernels, dialect);
        // The reported overload gets no wrappers, so the generated code itself stays valid.
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public async Task OverloadsDifferOnlyByIn_LaunchMethodCollision_ReportsGSH0059ForLaunch(string dialect)
    {
        // The later overload duplicates the managed Launch{Name}, which the message names
        var diagnostics = await KernelDiagnostics.RunAsync(s_inModifierKernels, dialect);
        var reported = Assert.Single(diagnostics, d => d.Id == GSharpDiagnostics.IndistinguishableKernelOverloads.Id);
        Assert.Equal(
            (DiagnosticOrigin.Analyzer, 12, 28, 12, 31),
            (reported.Origin, reported.StartLine, reported.StartColumn, reported.EndLine, reported.EndColumn));
        Assert.Contains("LaunchRun", reported.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("LaunchRunPtr", reported.Message, StringComparison.Ordinal);

        var (_, output, _) = GeneratorTestRunner.RunGenerator(s_inModifierKernels, dialect);
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
    }

    // A launcher of one kernel that has the name and the written parameter types of a launcher of another kernel:
    // the test case, the reported kernel's name (line, first and last column) and the duplicated member.
    public static TheoryData<string, string, int, int, int, string> CollidingLaunchers() => new()
    {
        // Scale has no overload, so its IntPtr launcher is the public LaunchScalePtr
        { "Generator/KernelOverload_PointerNameRefStruct.cu.cs", "Cpu", 14, 28, 36, "LaunchScalePtr(global::System.IntPtr)" },
        { "Generator/KernelOverload_PointerNameRefStruct.cu.cs", "Cuda", 14, 28, 36, "LaunchScalePtr(global::System.IntPtr)" },
        { "Generator/KernelOverload_PointerNameScalar.cu.cs", "Cpu", 12, 28, 36, "LaunchScalePtr(global::System.IntPtr)" },
        { "Generator/KernelOverload_PointerNameScalar.cu.cs", "Cuda", 12, 28, 36, "LaunchScalePtr(global::System.IntPtr)" },
        // The typed launchers of arrays of structs with array fields take no GpuBuffer<T>
        {
            "Generator/KernelOverload_ProxyArrays.cu.cs", "Cpu", 16, 28, 31,
            "LaunchRunPtr(global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy)"
        },
        {
            "Generator/KernelOverload_ProxyArrays.cu.cs", "Cuda", 16, 28, 31,
            "LaunchRunPtr(global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy)"
        },
        { "Generator/KernelOverload_RefStructs.cu.cs", "Cpu", 16, 28, 31, "LaunchRunPtr(global::System.IntPtr)" },
        { "Generator/KernelOverload_RefStructs.cu.cs", "Cuda", 16, 28, 31, "LaunchRunPtr(global::System.IntPtr)" },
        // The first overload of Run is keyed Run__o0, as is the kernel named Run__o0
        { "Generator/KernelOverload_MemberKey.cu.cs", "Cpu", 15, 28, 35, "WarmupFunctionPointerRun__o0()" },
        { "Generator/KernelOverload_MemberKey.cu.cs", "Cuda", 15, 28, 35, "WarmupFunctionPointerRun__o0()" },
        // With GSharpEmitSource (on in these tests), the constant LaunchX_Source of LaunchX is the launcher of X_Source
        { "Generator/KernelOverload_SourceConstant.cu.cs", "Cpu", 12, 28, 36, "LaunchX_Source" },
        { "Generator/KernelOverload_SourceConstant.cu.cs", "Cuda", 12, 28, 36, "LaunchX_Source" },
        // The warmup method of Foo is the other kernel's own method
        { "Generator/KernelOverload_OwnMethod.cu.cs", "Cpu", 9, 28, 31, "WarmupFunctionPointerFoo()" },
        { "Generator/KernelOverload_OwnMethod.cu.cs", "Cuda", 9, 28, 31, "WarmupFunctionPointerFoo()" },
        // Members of every kernel class; the class then gets no host members at all
        { "Generator/KernelOverload_ClassMember.cu.cs", "Cpu", 9, 28, 39, "AotContexts" },
        { "Generator/KernelOverload_ClassMember.cu.cs", "Cuda", 9, 28, 39, "AotContexts" },
        { "Generator/KernelOverload_ErrorSiteField.cu.cs", "Cpu", 9, 28, 40, "s_errorSites" },
        { "Generator/KernelOverload_ErrorSiteField.cu.cs", "Cuda", 9, 28, 40, "s_errorSites" }
    };

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [MemberData(nameof(CollidingLaunchers))]
    public async Task KernelsWithDifferentNames_SameLauncherSignature_ReportsGSH0059(string testCase, string dialect,
        int line, int startColumn, int endColumn, string wrapper)
    {
        var source = RoslynTestUtility.ReadTestCase(testCase);
        var diagnostics = await KernelDiagnostics.RunAsync(source, dialect);
        var reported = Assert.Single(diagnostics, d => d.Id == GSharpDiagnostics.IndistinguishableKernelOverloads.Id);
        Assert.Equal(
            (DiagnosticOrigin.Analyzer, line, startColumn, line, endColumn),
            (reported.Origin, reported.StartLine, reported.StartColumn, reported.EndLine, reported.EndColumn));
        Assert.Contains($"'{wrapper}'", reported.Message, StringComparison.Ordinal);

        var (_, output, _) = GeneratorTestRunner.RunGenerator(source, dialect);
        // The reported kernel gets no host members, so the generated code stays valid
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public async Task KernelsWithDifferentNames_PointerLaunchersTakeDifferentTypes_ReportsNothing(string dialect)
    {
        // LaunchScalePtr of Scale(float[]) takes a GSharpArrayProxy and LaunchScalePtr of ScalePtr(nint) an IntPtr
        var source = RoslynTestUtility.ReadTestCase("Generator/KernelOverload_PointerNameArray.cu.cs");
        var diagnostics = await KernelDiagnostics.RunAsync(source, dialect);
        Assert.DoesNotContain(diagnostics, d => d.Id == GSharpDiagnostics.IndistinguishableKernelOverloads.Id);

        var (_, output, _) = GeneratorTestRunner.RunGenerator(source, dialect);
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
        var wrapper = output.SyntaxTrees
            .Single(t => t.FilePath.EndsWith("PointerNameArrayKernels.g.cs", StringComparison.Ordinal))
            .ToString();
        Assert.Contains("LaunchScale(", wrapper, StringComparison.Ordinal);
        Assert.Contains("LaunchScalePtr(", wrapper, StringComparison.Ordinal);
    }

    // A kernel named like a launcher of another kernel has the launcher's parameter types without the launch options,
    // which the launcher has: C# keeps both, so nothing is reported
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Generator/KernelOverload_LookalikeArray.cu.cs", "Cpu")]
    [InlineData("Generator/KernelOverload_LookalikeArray.cu.cs", "Cuda")]
    [InlineData("Generator/KernelOverload_LookalikeNoParameters.cu.cs", "Cpu")]
    [InlineData("Generator/KernelOverload_LookalikeNoParameters.cu.cs", "Cuda")]
    [InlineData("Generator/KernelOverload_LookalikePointer.cu.cs", "Cpu")]
    [InlineData("Generator/KernelOverload_LookalikePointer.cu.cs", "Cuda")]
    public async Task KernelNamedLikeALauncher_ParameterListsDifferByLaunchOptions_ReportsNothing(string testCase,
        string dialect)
    {
        var source = RoslynTestUtility.ReadTestCase(testCase);
        var diagnostics = await KernelDiagnostics.RunAsync(source, dialect);
        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.IndistinguishableKernelOverloads.Id));

        var (_, output, _) = GeneratorTestRunner.RunGenerator(source, dialect);
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
    }
}
