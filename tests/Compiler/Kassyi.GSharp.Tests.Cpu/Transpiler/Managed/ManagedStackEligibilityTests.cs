#nullable enable
using System.Linq;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Cpu.Transpiler.Managed;

[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class ManagedStackEligibilityTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Analysis/ManagedStackEligibilityCases.cs")
    );

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("EligibilityKernels", "BlockBarrierOnly", true)]
    [InlineData("EligibilityKernels", "SpanParameterWithBarrier", true)]
    [InlineData("EligibilityKernels", "CallsStackEligibleDeviceFunction", true)]
    [InlineData("EligibilityKernels", "WithWarpShuffle", true)]
    [InlineData("EligibilityKernels", "WithActiveMask", true)]
    [InlineData("EligibilityKernels", "CallsActiveMaskReachingDeviceFunction", true)]
    [InlineData("EligibilityKernels", "ActiveMaskWithUsing", false)]
    [InlineData("EligibilityKernels", "CallsActiveMaskWithUsing", false)]
    [InlineData("EligibilityKernels", "ShuffleWithUsing", true)]
    [InlineData("EligibilityKernels", "CooperativeKernel", false)]
    [InlineData("EligibilityKernels", "WithoutBarrier", false)]
    [InlineData("InstanceEligibilityTarget", "InstanceKernelWithBarrier", false)]
    [InlineData("EligibilityKernels", "NonSpanRefStructParameter", false)]
    [InlineData("EligibilityKernels", "CallsShuffleReachingDeviceFunction", true)]
    [InlineData("InstanceEligibilityTarget", "CallsInstanceDeviceFunction", false)]
    [InlineData("WarpEligibilityKernels", "WarpOnly", true)]
    [InlineData("WarpEligibilityKernels", "CallsRecursiveShuffle", false)]
    public void IsStackKernel_VariousShapes_MatchesExpectedEligibility(string typeName, string methodName,
        bool expected)
    {
        var method = GetMethod(typeName, methodName);
        Assert.Equal(expected, ManagedStackEligibility.IsStackKernel(method, s_compilation));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("EligibilityKernels", "StackDeviceWait", true)]
    [InlineData("EligibilityKernels", "ShuffleDeviceWait", true)]
    [InlineData("EligibilityKernels", "ActiveMaskDeviceWait", true)]
    [InlineData("EligibilityKernels", "ActiveMaskWithUsingHelper", false)]
    [InlineData("WarpEligibilityKernels", "Chain10", true)]
    [InlineData("WarpEligibilityKernels", "RecursiveShuffle", false)]
    [InlineData("InstanceEligibilityTarget", "InstanceDeviceWait", false)]
    public void IsStackFunction_VariousShapes_MatchesExpectedEligibility(string typeName, string methodName,
        bool expected)
    {
        var method = GetMethod(typeName, methodName);
        Assert.Equal(expected, ManagedStackEligibility.IsStackFunction(method, s_compilation));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("AtTheVisitLimit", 4096, true)]
    [InlineData("OverTheVisitLimit", 4097, false)]
    public void IsStackKernel_VisitCountsAtTheLimit_RunsOnTheStackUpTo4096(string methodName, int size,
        bool expected)
    {
        var method = GetMethod("WarpEligibilityKernels", methodName);

        Assert.True(WarpFrameLayout.TryGet(method, s_compilation, out var layout));
        Assert.Equal(size, layout.Size);
        Assert.Equal(expected, ManagedStackEligibility.IsStackKernel(method, s_compilation));
        Assert.Equal(expected, ManagedStackEligibility.GetWarpFailure(method, s_compilation) is null);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("WarpEligibilityKernels", "CallsRecursiveShuffle", "cannot be placed")]
    [InlineData("WarpEligibilityKernels", "OverTheVisitLimit", "4097 visit counts")]
    [InlineData("EligibilityKernels", "ActiveMaskWithUsing", "declares a local with using")]
    public void GetWarpFailure_KernelWhoseCollectivesCannotRunOnItsStack_StatesTheReason(string typeName,
        string methodName, string reason)
    {
        var method = GetMethod(typeName, methodName);

        Assert.Contains(reason, ManagedStackEligibility.GetWarpFailure(method, s_compilation), StringComparison.Ordinal);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("EligibilityKernels", "WithActiveMask")]
    [InlineData("EligibilityKernels", "BlockBarrierOnly")]
    [InlineData("WarpEligibilityKernels", "WarpOnly")]
    public void GetWarpFailure_NoWarpCollectiveOrPlacedCounts_HasNoReason(string typeName,
        string methodName)
    {
        Assert.Null(ManagedStackEligibility.GetWarpFailure(GetMethod(typeName, methodName), s_compilation));
    }

    // [WHY] Eligibility keeps such a kernel on its state machine; a stack form made for it anyway must fail, never give
    // its collectives site or frame 0, which would make different instances of a collective meet as one
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("CallsRecursiveShuffle")]
    [InlineData("OverTheVisitLimit")]
    public void GenerateStackForm_KernelWhoseCountsDoNotFit_ThrowsAnInternalError(string methodName)
    {
        var method = GetMethod("WarpEligibilityKernels", methodName);
        var declaration = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences[0].GetSyntax();
        var plan = MethodEmissionPlan.Create(declaration, s_compilation.GetSemanticModel(declaration.SyntaxTree),
            isKernel: true);

        var ex = Assert.Throws<InvalidOperationException>(() => ManagedCSharpTranspiler.GenerateStackForm(in plan));

        Assert.Contains("internal error", ex.Message, StringComparison.Ordinal);
    }

    // A delegate cannot pass the frame offset that the stack form of a function reaching a warp collective takes: the
    // form of the kernel fails, and the collector keeps the kernel on its state machine with the reason. The same holds
    // for a kernel that reaches Warp.ActiveMask() and passes a function that reaches it, whose form writes the body
    // lowered for the stacks
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("PassesShuffleGroup")]
    [InlineData("PassesActiveMaskGroup")]
    public void GenerateStackForm_PassesAWarpFunctionAsAMethodGroup_Throws(string methodName)
    {
        var method = GetMethod("WarpEligibilityKernels", methodName);
        var declaration = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences[0].GetSyntax();
        var plan = MethodEmissionPlan.Create(declaration, s_compilation.GetSemanticModel(declaration.SyntaxTree),
            isKernel: true);

        Assert.True(ManagedStackEligibility.IsStackKernel(method, s_compilation));
        var ex = Assert.Throws<InvalidOperationException>(() => ManagedCSharpTranspiler.GenerateStackForm(in plan));
        Assert.Contains("as a method group", ex.Message, StringComparison.Ordinal);
    }

    private static IMethodSymbol GetMethod(string typeName, string methodName)
    {
        var type = s_compilation.GetTypeByMetadataName($"ManagedStackEligibilityCases.{typeName}") ??
            throw new InvalidOperationException($"Type '{typeName}' not found.");
        return type.GetMembers(methodName).OfType<IMethodSymbol>().Single();
    }
}
