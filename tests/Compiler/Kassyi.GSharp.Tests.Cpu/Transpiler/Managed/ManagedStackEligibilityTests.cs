#nullable enable
using System.IO;
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

    private static readonly Compilation s_cooperativeCompilation = RoslynTestUtility.CreateCompilation(
        File.ReadAllText(RepositoryPaths.Combine(
            "tests", "Compiler", "Kassyi.GSharp.Tests.Cpu", "Analysis", "Cases",
            "ManagedStackCooperativeEligibilityCases.cs"))
    );

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("EligibilityKernels", "BlockBarrierOnly", true)]
    [InlineData("EligibilityKernels", "SpanParameterWithBarrier", true)]
    [InlineData("EligibilityKernels", "CallsStackEligibleDeviceFunction", true)]
    [InlineData("EligibilityKernels", "WithWarpShuffle", true)]
    [InlineData("EligibilityKernels", "WithActiveMask", true)]
    [InlineData("EligibilityKernels", "CallsActiveMaskReachingDeviceFunction", true)]
    [InlineData("EligibilityKernels", "ActiveMaskWithUsing", true)]
    [InlineData("EligibilityKernels", "CallsActiveMaskWithUsing", true)]
    [InlineData("EligibilityKernels", "ShuffleWithUsing", true)]
    [InlineData("EligibilityKernels", "CooperativeKernel", true)]
    [InlineData("EligibilityKernels", "WithoutBarrier", false)]
    [InlineData("InstanceEligibilityTarget", "InstanceKernelWithBarrier", false)]
    [InlineData("EligibilityKernels", "NonSpanRefStructParameter", false)]
    [InlineData("EligibilityKernels", "CallsShuffleReachingDeviceFunction", true)]
    [InlineData("InstanceEligibilityTarget", "CallsInstanceDeviceFunction", false)]
    [InlineData("WarpEligibilityKernels", "WarpOnly", true)]
    [InlineData("WarpEligibilityKernels", "CallsRecursiveShuffle", false)]
    [InlineData("EligibilityKernels", "PassesIneligibleGroupKernel", false)]
    [InlineData("EligibilityKernels", "PassesIneligibleExtensionGroupKernel", false)]
    [InlineData("EligibilityKernels", "NameofIneligibleGroupKernel", true)]
    [InlineData("EligibilityKernels", "PassesSelfGroupKernel", false)]
    [InlineData("EligibilityKernels", "PassesCycleKernel", false)]
    [InlineData("EligibilityKernels", "PassesEligibleGenericGroupKernel", true)]
    [InlineData("EligibilityKernels", "PassesIneligibleGenericGroupKernel", false)]
    [InlineData("EligibilityKernels", "PassesEligiblePartialGroupKernel", true)]
    [InlineData("EligibilityKernels", "PassesIneligiblePartialGroupKernel", false)]
    [InlineData("UserDefinedNameofKernels", "CallsUserNameofWithIneligibleGroupKernel", false)]
    [InlineData("EligibilityKernels", "StructCtorBarrierKernel", false)]
    [InlineData("EligibilityKernels", "StructCtorAndDirectBarrierKernel", false)]
    [InlineData("EligibilityKernels", "GenericStructBarrierKernel", false)]
    [InlineData("EligibilityKernels", "GenericStructPlainKernel", false)]
    [InlineData("EligibilityKernels", "GridSyncStructCtorKernel", false)]
    [InlineData("EligibilityKernels", "CallsGridSyncStructCtorDeviceKernel", false)]
    [InlineData("EligibilityKernels", "ConstrainedGenMethodKernel", false)]
    [InlineData("EligibilityKernels", "ConstrainedGenMethodPlainKernel", false)]
    [InlineData("EligibilityKernels", "PartialGenericKernel", false)]
    [InlineData("EligibilityKernels", "PartialGenericPlainKernel", false)]
    [InlineData("EligibilityKernels", "PartialStructGenericDeviceFunctionKernel", true)]
    [InlineData("EligibilityKernels", "PartialGenericStructDeviceFunctionKernel", true)]
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
    [InlineData("EligibilityKernels", "ActiveMaskWithUsingHelper", true)]
    [InlineData("WarpEligibilityKernels", "Chain10", true)]
    [InlineData("WarpEligibilityKernels", "RecursiveShuffle", false)]
    [InlineData("InstanceEligibilityTarget", "InstanceDeviceWait", false)]
    [InlineData("EligibilityKernels", "IneligibleGroupStep", false)]
    [InlineData("EligibilityKernels", "CallsIneligibleGroupHelper", false)]
    [InlineData("EligibilityKernels", "IneligibleExtensionStep", false)]
    [InlineData("EligibilityKernels", "PassesInstanceGroupHelper", false)]
    [InlineData("EligibilityKernels", "EligibleGenericStep", true)]
    [InlineData("EligibilityKernels", "IneligibleGenericStep", false)]
    [InlineData("EligibilityKernels", "EligiblePartialStep", true)]
    [InlineData("EligibilityKernels", "IneligiblePartialStep", false)]
    [InlineData("EligibilityKernels", "StructCtorDeviceWait", false)]
    [InlineData("EligibilityKernels", "GridSyncStructCtorDeviceFunc", false)]
    public void IsStackFunction_VariousShapes_MatchesExpectedEligibility(string typeName, string methodName,
        bool expected)
    {
        var method = GetMethod(typeName, methodName);
        Assert.Equal(expected, ManagedStackEligibility.IsStackFunction(method, s_compilation));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void IsStackFunction_SelfMethodGroupCycle_ReturnsFalseWithoutOverflow()
    {
        var method = GetMethod("EligibilityKernels", "SelfGroup");
        Assert.False(ManagedStackEligibility.IsStackFunction(method, s_compilation));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void IsStackFunction_MutualMethodGroupCycle_ReturnsFalseWithoutOverflow()
    {
        Assert.False(ManagedStackEligibility.IsStackFunction(GetMethod("EligibilityKernels", "CycleA"), s_compilation));
        Assert.False(ManagedStackEligibility.IsStackFunction(GetMethod("EligibilityKernels", "CycleB"), s_compilation));
        Assert.False(ManagedStackEligibility.IsStackKernel(GetMethod("EligibilityKernels", "PassesCycleKernel"),
            s_compilation));
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
    [InlineData("EligibilityKernels", "ActiveMaskWithUsing")]
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

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("CooperativeSyncGridModeKernel", true)]
    [InlineData("CooperativeSyncGridBodyKernel", true)]
    [InlineData("CallsGridBarrierDeviceFunction", true)]
    public void IsStackKernel_CooperativeAndGridSyncShapes_MatchesExpectedEligibility(string methodName, bool expected)
    {
        var method = GetCooperativeMethod(methodName);
        Assert.Equal(expected, ManagedStackEligibility.IsStackKernel(method, s_cooperativeCompilation));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void IsStackFunction_ReachesGridBarrier_ReturnsTrue()
    {
        var method = GetCooperativeMethod("GridDeviceWait");
        Assert.True(ManagedStackEligibility.IsStackFunction(method, s_cooperativeCompilation));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("StructCtorBarrierKernel", "StructMemberSplit")]
    [InlineData("StructCtorAndDirectBarrierKernel", "StructMemberSplit")]
    [InlineData("GenericStructBarrierKernel", "StructMemberSplit")]
    [InlineData("GenericStructPlainKernel", "NoBlockBarrier")]
    [InlineData("GridSyncStructCtorKernel", "StructMemberSplit")]
    [InlineData("CallsGridSyncStructCtorDeviceKernel", "StructMemberSplit")]
    [InlineData("ConstrainedGenMethodKernel", "StructMemberSplit")]
    [InlineData("ConstrainedGenMethodPlainKernel", "NoBlockBarrier")]
    [InlineData("PartialGenericKernel", "StructMemberSplit")]
    [InlineData("PartialGenericPlainKernel", "NoBlockBarrier")]
    public void FindKernelReason_StructMemberSplit_ReturnsStructMemberSplit(string methodName, string expectedReason)
    {
        var method = GetMethod("EligibilityKernels", methodName);
        var reason = ManagedStackEligibility.FindKernelReason(method, s_compilation);
        Assert.NotNull(reason);
        Assert.Equal(expectedReason, reason.Value.Name);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("StructCtorDeviceWait", "StructMemberSplit")]
    [InlineData("GridSyncStructCtorDeviceFunc", "StructMemberSplit")]
    public void FindFunctionReason_StructMemberSplit_ReturnsStructMemberSplit(string methodName, string expectedReason)
    {
        var method = GetMethod("EligibilityKernels", methodName);
        var reason = ManagedStackEligibility.FindFunctionReason(method, s_compilation);
        Assert.NotNull(reason);
        Assert.Equal(expectedReason, reason.Value.Name);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("PartialPolyNonExtAttrDefKernel")]
    [InlineData("PartialPolyNonExtAttrImplKernel")]
    [InlineData("PartialPolyExtAttrDefKernel")]
    [InlineData("PartialPolyExtAttrImplKernel")]
    [InlineData("PartialPolyIndirectAttrDefKernel")]
    [InlineData("PartialPolyIndirectAttrImplKernel")]
    public void IsStackKernel_PartialGenericPolymorphicRecursion_TerminatesAsEligible(string methodName)
    {
        const string Source = """
            using Kassyi.GSharp.Core;

            namespace ManagedPartialPolyCases;

            public struct PolyWrap<T>
            {
                public T Value;
            }

            public static partial class Kernels
            {
                [GpuDeviceFunction]
                public static partial void PartialPolyNonExtAttrDef<T>(int depth);

                public static partial void PartialPolyNonExtAttrDef<T>(int depth)
                {
                    if (depth <= 0)
                    {
                        return;
                    }

                    PartialPolyNonExtAttrDef<PolyWrap<T>>(depth - 1);
                }

                [GpuKernel]
                public static void PartialPolyNonExtAttrDefKernel(int[] data)
                {
                    PartialPolyNonExtAttrDef<int>(2);
                    Gpu.SyncThreads();
                    data[0] = 1;
                }

                public static partial void PartialPolyNonExtAttrImpl<T>(int depth);

                [GpuDeviceFunction]
                public static partial void PartialPolyNonExtAttrImpl<T>(int depth)
                {
                    if (depth <= 0)
                    {
                        return;
                    }

                    PartialPolyNonExtAttrImpl<PolyWrap<T>>(depth - 1);
                }

                [GpuKernel]
                public static void PartialPolyNonExtAttrImplKernel(int[] data)
                {
                    PartialPolyNonExtAttrImpl<int>(2);
                    Gpu.SyncThreads();
                    data[0] = 1;
                }

                [GpuDeviceFunction]
                public static partial int PartialPolyExtAttrDef<T>(this int depth, T value);

                public static partial int PartialPolyExtAttrDef<T>(this int depth, T value) =>
                    depth <= 0 ? 0 : (depth - 1).PartialPolyExtAttrDef(new PolyWrap<T> { Value = value });

                [GpuKernel]
                public static void PartialPolyExtAttrDefKernel(int[] data)
                {
                    data[0] = 2.PartialPolyExtAttrDef(0);
                    Gpu.SyncThreads();
                }

                public static partial int PartialPolyExtAttrImpl<T>(this int depth, T value);

                [GpuDeviceFunction]
                public static partial int PartialPolyExtAttrImpl<T>(this int depth, T value) =>
                    depth <= 0 ? 0 : (depth - 1).PartialPolyExtAttrImpl(new PolyWrap<T> { Value = value });

                [GpuKernel]
                public static void PartialPolyExtAttrImplKernel(int[] data)
                {
                    data[0] = 2.PartialPolyExtAttrImpl(0);
                    Gpu.SyncThreads();
                }

                [GpuDeviceFunction]
                public static partial void PartialPolyIndirectAttrDef<T>(int depth);

                public static partial void PartialPolyIndirectAttrDef<T>(int depth)
                {
                    if (depth <= 0)
                    {
                        return;
                    }

                    PartialPolyIndirectHelperDef<T>(depth);
                }

                [GpuDeviceFunction]
                public static void PartialPolyIndirectHelperDef<U>(int depth) =>
                    PartialPolyIndirectAttrDef<PolyWrap<U>>(depth - 1);

                [GpuKernel]
                public static void PartialPolyIndirectAttrDefKernel(int[] data)
                {
                    PartialPolyIndirectAttrDef<int>(2);
                    Gpu.SyncThreads();
                    data[0] = 1;
                }

                public static partial void PartialPolyIndirectAttrImpl<T>(int depth);

                [GpuDeviceFunction]
                public static partial void PartialPolyIndirectAttrImpl<T>(int depth)
                {
                    if (depth <= 0)
                    {
                        return;
                    }

                    PartialPolyIndirectHelperImpl<T>(depth);
                }

                [GpuDeviceFunction]
                public static void PartialPolyIndirectHelperImpl<U>(int depth) =>
                    PartialPolyIndirectAttrImpl<PolyWrap<U>>(depth - 1);

                [GpuKernel]
                public static void PartialPolyIndirectAttrImplKernel(int[] data)
                {
                    PartialPolyIndirectAttrImpl<int>(2);
                    Gpu.SyncThreads();
                    data[0] = 1;
                }
            }
            """;
        var compilation = RoslynTestUtility.CreateCompilation(Source);
        var type = compilation.GetTypeByMetadataName("ManagedPartialPolyCases.Kernels")
            ?? throw new InvalidOperationException("Type 'Kernels' not found.");
        var method = type.GetMembers(methodName).OfType<IMethodSymbol>().Single();

        Assert.True(ManagedStackEligibility.IsStackKernel(method, compilation));
        Assert.Null(ManagedStackEligibility.FindKernelReason(method, compilation));
    }

    private static IMethodSymbol GetMethod(string typeName, string methodName)
    {
        var type = s_compilation.GetTypeByMetadataName($"ManagedStackEligibilityCases.{typeName}") ??
            throw new InvalidOperationException($"Type '{typeName}' not found.");
        return type.GetMembers(methodName).OfType<IMethodSymbol>().Single();
    }

    private static IMethodSymbol GetCooperativeMethod(string methodName)
    {
        var type = s_cooperativeCompilation.GetTypeByMetadataName(
                "ManagedStackCooperativeEligibilityCases.CooperativeEligibilityKernels") ??
            throw new InvalidOperationException("Type 'CooperativeEligibilityKernels' not found.");
        return type.GetMembers(methodName).OfType<IMethodSymbol>().Single();
    }
}
