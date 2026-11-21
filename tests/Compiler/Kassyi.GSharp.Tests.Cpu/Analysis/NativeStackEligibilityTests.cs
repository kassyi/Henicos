#nullable enable
using Kassyi.GSharp.Compiler.Transpiler;

namespace Kassyi.GSharp.Tests.Shared.Analysis;

[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Compiler)]
public class NativeStackEligibilityTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        ReadCase("NativeStackEligibilityCases.cs"));

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("NativeStackEligibilityCases.Kernels", "DirectBarrierKernel", NativeStackObstacle.None)]
    [InlineData("NativeStackEligibilityCases.Kernels", "LocalFunctionBarrierKernel", NativeStackObstacle.None)]
    [InlineData("NativeStackEligibilityCases.Kernels", "LambdaBarrierKernel", NativeStackObstacle.None)]
    [InlineData("NativeStackEligibilityCases.Kernels", "NoBarrierKernel", NativeStackObstacle.NoBlockBarrier)]
    [InlineData("NativeStackEligibilityCases.Kernels", "WarpShuffleAndBarrierKernel", NativeStackObstacle.None)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "WarpActiveMaskAndBarrierKernel", NativeStackObstacle.WarpActiveMask)]
    [InlineData("NativeStackEligibilityCases.Kernels", "GridSyncAndBarrierKernel", NativeStackObstacle.GridSync)]
    [InlineData("NativeStackEligibilityCases.Kernels", "CooperativeBarrierKernel", NativeStackObstacle.Cooperative)]
    [InlineData("NativeStackEligibilityCases.Kernels", "SuspendingDeviceFunctionKernel", NativeStackObstacle.None)]
    [InlineData("NativeStackEligibilityCases.Kernels", "MultiLevelSuspendingKernel", NativeStackObstacle.None)]
    [InlineData("NativeStackEligibilityCases.Kernels", "ShuffleDeviceFunctionKernel", NativeStackObstacle.None)]
    [InlineData("NativeStackEligibilityCases.Kernels", "MultiLevelShuffleKernel", NativeStackObstacle.None)]
    [InlineData("NativeStackEligibilityCases.Kernels", "ShuffleOnlyKernel", NativeStackObstacle.None)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "ShuffleCallingShuffleKernel", NativeStackObstacle.WarpLayout)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "ActiveMaskDeviceFunctionKernel", NativeStackObstacle.WarpActiveMask)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "FloatSwitchShuffleKernel", NativeStackObstacle.CaseValueWithoutLabel)]
    [InlineData("NativeStackOperatorMacroCases.Kernels",
        "MacroAfterBarrierKernel", NativeStackObstacle.WarpOperatorMacro)]
    [InlineData("NativeStackOperatorMacroCases.Kernels",
        "MacroAndShuffleKernel", NativeStackObstacle.WarpOperatorMacro)]
    [InlineData("NativeStackOperatorMacroCases.Kernels",
        "PrivateMacroTargetKernel", NativeStackObstacle.WarpOperatorMacro)]
    [InlineData("NativeStackEligibilityCases.Kernels", "GridSyncDeviceFunctionKernel", NativeStackObstacle.GridSync)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "DirectRecursiveSuspendingKernel", NativeStackObstacle.RecursiveSuspendingCall)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "MutuallyRecursiveSuspendingKernel", NativeStackObstacle.RecursiveSuspendingCall)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "WrapperCallingRecursiveSuspendingKernel", NativeStackObstacle.RecursiveSuspendingCall)]
    [InlineData("NativeStackUsingStaticCases.Kernels", "UsingStaticSyncThreadsKernel", NativeStackObstacle.None)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "UserLookalikeBarrierKernel", NativeStackObstacle.NoBlockBarrier)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "ConditionalOmittedCallKernel", NativeStackObstacle.NoBlockBarrier)]
    internal void Classify_KernelBody_ReturnsExpectedObstacle(
        string typeName,
        string methodName,
        NativeStackObstacle expected)
    {
        var (symbol, declaration, model) = GetKernel(s_compilation, typeName, methodName);

        Assert.Equal(expected, NativeStackEligibility.Classify(symbol, declaration, model));
    }

    // The visit counts of a kernel with size - 1 collectives of its own and no calls take size slots
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(WarpFrameLayout.MaxSize, NativeStackObstacle.None)]
    [InlineData(WarpFrameLayout.MaxSize + 1, NativeStackObstacle.WarpFrameTooLarge)]
    internal void Classify_WarpFrameOfSize_RunsOnStacksUpToTheMaximum(int size, NativeStackObstacle expected)
    {
        const string Shuffle = "        v = Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1);\n";
        var shuffles = string.Concat(Enumerable.Repeat(Shuffle, size - 1));
        // Kept as interpolated template: repeats the shuffle statement (size - 1) times up to MaxSize + 1.
        var source = $$"""
            using Kassyi.GSharp.Core;

            namespace WarpFrameSizeCases;

            public static partial class Kernels
            {
                [GpuKernel]
                public static void ManyShufflesKernel(int[] data)
                {
                    int v = data[ThreadIndex.X];
            {{shuffles}}
                    data[ThreadIndex.X] = v;
                }
            }
            """;
        var compilation = RoslynTestUtility.CreateCompilation(source);
        var (symbol, declaration, model) = GetKernel(compilation, "WarpFrameSizeCases.Kernels", "ManyShufflesKernel");

        Assert.True(WarpFrameLayout.TryGet(symbol, compilation, out var layout));
        Assert.Equal(size, layout.Size);
        Assert.Equal(expected, NativeStackEligibility.Classify(symbol, declaration, model));
    }

    // A kernel that the eligibility keeps on the state machine for its layout, written in the stack form anyway: the
    // missing place is a compiler bug, never a frame or site of 0
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void GenerateStackSource_KernelWithoutWarpLayout_ThrowsInternalError()
    {
        var (_, declaration, model) =
            GetKernel(s_compilation, "NativeStackEligibilityCases.Kernels", "ShuffleCallingShuffleKernel");
        var plan = MethodEmissionPlan.Create(declaration, model, isKernel: true);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            MethodTranspiler.GenerateCudaSource(in plan, null, new StackCppDialect(), out _));

        Assert.Contains("internal error", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ShuffleCallingShuffleKernel", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Classify_ImportedSuspendingDeviceFunction_ReturnsImportedSuspendingCall()
    {
        var libCompilation = RoslynTestUtility.CreateCompilation(ReadCase("NativeStackImportedLibraryCases.cs"))
            .WithAssemblyName("ImportedStackLib");
        var (_, libOutput, _) = GeneratorTestRunner.RunGenerator(libCompilation, "Cpu");
        var libReference = libOutput.ToMetadataReference();

        var consumerCompilation = RoslynTestUtility.CreateCompilation(ReadCase("NativeStackImportedCallRemoteCases.cs"))
            .WithAssemblyName("ConsumerApp")
            .AddReferences(libReference);

        var (symbol, declaration, model) = GetKernel(consumerCompilation, "ConsumerApp.Kernels", "CallRemoteKernel");
        Assert.Equal(
            NativeStackObstacle.ImportedSuspendingCall,
            NativeStackEligibility.Classify(symbol, declaration, model));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Classify_TransitiveImportedSuspendingDeviceFunction_ReturnsImportedSuspendingCall()
    {
        var libCompilation = RoslynTestUtility.CreateCompilation(ReadCase("NativeStackImportedLibraryCases.cs"))
            .WithAssemblyName("ImportedStackLib");
        var (_, libOutput, _) = GeneratorTestRunner.RunGenerator(libCompilation, "Cpu");
        var libReference = libOutput.ToMetadataReference();

        var consumerCompilation = RoslynTestUtility.CreateCompilation(ReadCase("NativeStackImportedCallWrapperCases.cs"))
            .WithAssemblyName("ConsumerApp")
            .AddReferences(libReference);

        var (symbol, declaration, model) =
            GetKernel(consumerCompilation, "ConsumerApp.Kernels", "CallLocalWrapperKernel");
        Assert.Equal(
            NativeStackObstacle.ImportedSuspendingCall,
            NativeStackEligibility.Classify(symbol, declaration, model));
    }

    private static string ReadCase(string fileName) =>
        File.ReadAllText(RepositoryPaths.Combine(
            "tests", "Compiler", "Kassyi.GSharp.Tests.Cpu", "Analysis", "Cases", fileName));

    private static (IMethodSymbol Symbol, MethodDeclarationSyntax Declaration, SemanticModel Model) GetKernel(
        Compilation compilation,
        string typeName,
        string methodName)
    {
        var type = compilation.GetTypeByMetadataName(typeName)
            ?? throw new InvalidOperationException($"Type '{typeName}' was not found.");
        var symbol = type.GetMembers(methodName).OfType<IMethodSymbol>().Single();
        var declaration = (MethodDeclarationSyntax)symbol.DeclaringSyntaxReferences[0].GetSyntax();
        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        return (symbol, declaration, model);
    }
}
