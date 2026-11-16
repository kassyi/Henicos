#nullable enable
using Kassyi.GSharp.Compiler.Marshaling;

namespace Kassyi.GSharp.Tests.Shared.Analysis;

/// <summary>
/// Verifies which kernels cannot run as C# one GPU thread at a time (<see cref="CSharpExecutionAnalysis"/>) and the
/// plan of the launcher's C# execution (<see cref="CSharpExecutionPlan"/>).
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class CSharpExecutionAnalysisTests
{
    private const string OneThread = ", and the C# execution runs one GPU thread at a time";

    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Analysis/CSharpExecutionCases.cs")
    );

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Cases_Compile_WithoutErrors()
    {
        Assert.Empty(s_compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Plain")]
    [InlineData("CallsPlainFunction")]
    [InlineData("IntSum")]
    [InlineData("ConditionalBarrier")]
    [InlineData("Cycle")]
    [InlineData("Parameters")]
    public void FindObstacle_RunsOneThreadAtATime_IsNull(string kernel)
    {
        Assert.Null(CSharpExecutionAnalysis.FindObstacle(GetKernel(kernel), s_compilation));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("BlockBarrier", "Gpu.SyncThreads()" + OneThread)]
    [InlineData("GridBarrier", "Gpu.SyncGrid()" + OneThread)]
    [InlineData("Shuffle", "Warp.ShuffleDownSync()" + OneThread)]
    [InlineData("ActiveMask", "Warp.ActiveMask()" + OneThread)]
    [InlineData("AsyncBarrier", "Gpu.MBarrierInit()" + OneThread)]
    [InlineData("SharedTile", "SharedMemory<T>" + OneThread)]
    [InlineData("BarrierThroughFunction", "Gpu.SyncThreads()" + OneThread)]
    [InlineData("BarrierThroughTwoFunctions", "Gpu.SyncThreads()" + OneThread)]
    [InlineData("BarrierInLambda", "Gpu.SyncThreads()" + OneThread)]
    [InlineData("MethodGroupOfBarrierFunction", "Gpu.SyncThreads()" + OneThread)]
    [InlineData("OperatorMacro", "an operator macro ([GpuOperatorMacro]), which only the generated code expands")]
    [InlineData("FloatSum", "a float or double StackLinq Sum, which only the generated code sums in order")]
    [InlineData("InlinePtx", "Gpu.InlinePtx, which has no C# implementation")]
    public void FindObstacle_NeedsOtherThreadsOrTheManagedRewrite_NamesTheConstruct(string kernel, string construct)
    {
        Assert.Equal(construct, CSharpExecutionAnalysis.FindObstacle(GetKernel(kernel), s_compilation));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_EveryKindOfParameter_PassesEachAsTheKernelTakesIt()
    {
        var plan = CSharpExecutionPlan.Create(Host("Parameters"), isDevicePtr: false);

        Assert.Null(plan.CannotRunReason);
        Assert.True(plan.ReturnsValue);
        Assert.Equal("global::CSharpExecutionCases.Kernels.Parameters", plan.Method);
        Assert.Equal(
            new[]
            {
                new CSharpArgument("a", CSharpArgumentKind.Value, "global::System.Int32", "__gsharp_csharp_arg_0"),
                // An in parameter beside a ref parameter of its type is the caller's variable, as the ref one is
                new CSharpArgument("b", CSharpArgumentKind.InReference, "global::System.Int32",
                    "__gsharp_csharp_arg_1", "__gsharp_csharp_pin_1"),
                new CSharpArgument("c", CSharpArgumentKind.Ref, "global::System.Int32", "__gsharp_csharp_arg_2",
                    "__gsharp_csharp_pin_2"),
                new CSharpArgument("d", CSharpArgumentKind.Out, "global::System.Int32", "__gsharp_csharp_arg_3",
                    "__gsharp_csharp_pin_3"),
                new CSharpArgument("e", CSharpArgumentKind.Span, "global::System.Int32", "__gsharp_csharp_arg_4",
                    "__gsharp_csharp_pin_4", "__gsharp_csharp_len_4"),
                new CSharpArgument(
                    "f", CSharpArgumentKind.ReadOnlySpan, "global::System.Int32", "__gsharp_csharp_arg_5",
                    "__gsharp_csharp_pin_5", "__gsharp_csharp_len_5"),
                new CSharpArgument("g", CSharpArgumentKind.Value, "global::System.Single[]?", "__gsharp_csharp_arg_6")
            },
            plan.Arguments.ToArray());
    }

    // mutant: CSharpExecutionPlan.PlanArgument plans every in parameter as InReference (the pin of a value is missing)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Create_InParameterWithoutARefOfItsType_PassesACopy()
    {
        var plan = CSharpExecutionPlan.Create(Host("InParameter"), isDevicePtr: false);

        Assert.Equal(
            new CSharpArgument("b", CSharpArgumentKind.In, "global::System.Int32", "__gsharp_csharp_arg_0"),
            plan.Arguments[0]);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Create_KernelReachesABarrier_ThrowsWithTheConstruct()
    {
        var plan = CSharpExecutionPlan.Create(Host("BarrierThroughFunction"), isDevicePtr: false);

        Assert.Equal("Kernels.BarrierThroughFunction", plan.MessageName);
        Assert.Equal("it reaches Gpu.SyncThreads(), and the C# execution runs one GPU thread at a time",
            plan.CannotRunReason);
        Assert.Empty(plan.Arguments);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Create_PointerLauncher_ThrowsAndNamesTheArrayLauncher()
    {
        var plan = CSharpExecutionPlan.Create(Host("Plain"), isDevicePtr: true);

        Assert.Equal(
            "LaunchPlainPtr takes the arguments in the form the device uses, and the C# execution runs only through " +
            "LaunchPlain",
            plan.CannotRunReason);
    }

    private static KernelHostModel Host(string kernel)
    {
        var symbol = GetKernel(kernel);
        var tree = symbol.DeclaringSyntaxReferences[0].SyntaxTree;
        return KernelHostModel.Create(symbol, default, s_compilation.GetSemanticModel(tree));
    }

    private static IMethodSymbol GetKernel(string name) =>
        (s_compilation.GetTypeByMetadataName("CSharpExecutionCases.Kernels") ??
         throw new InvalidOperationException("Type 'Kernels' not found."))
        .GetMembers(name)
        .OfType<IMethodSymbol>()
        .First();
}
