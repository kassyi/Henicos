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
        "WarpActiveMaskAndBarrierKernel", NativeStackObstacle.None)]
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
        "ActiveMaskDeviceFunctionKernel", NativeStackObstacle.None)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "FloatSwitchShuffleKernel", NativeStackObstacle.CaseValueWithoutLabel)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "UsingDeclarationActiveMaskKernel", NativeStackObstacle.None)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "UsingDeclarationInActiveMaskCalleeKernel", NativeStackObstacle.None)]
    [InlineData("NativeStackEligibilityCases.Kernels", "UsingDeclarationShuffleKernel", NativeStackObstacle.None)]
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
    [InlineData("NativeStackEligibilityCases.Kernels",
        "MethodGroupGridSyncKernel", NativeStackObstacle.GridSync)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "MethodGroupRecursiveKernel", NativeStackObstacle.RecursiveSuspendingCall)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "MethodGroupWarpKernel", NativeStackObstacle.WarpMethodGroup)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "MethodGroupActiveMaskKernel", NativeStackObstacle.WarpMethodGroup)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "MethodGroupUsingActiveMaskKernel", NativeStackObstacle.WarpMethodGroup)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "MethodGroupNameofKernel", NativeStackObstacle.None)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "MethodGroupSelfCycleKernel", NativeStackObstacle.RecursiveSuspendingCall)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "MethodGroupMutualCycleKernel", NativeStackObstacle.RecursiveSuspendingCall)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "StructCtorBarrierKernel", NativeStackObstacle.StructMemberSplit)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "StructCtorAndDirectBarrierKernel", NativeStackObstacle.StructMemberSplit)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "WrapperCallingStructCtorKernel", NativeStackObstacle.StructMemberSplit)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "GenericStructBarrierKernel", NativeStackObstacle.StructMemberSplit)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "GenericStructPlainKernel", NativeStackObstacle.NoBlockBarrier)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "GridSyncStructCtorKernel", NativeStackObstacle.StructMemberSplit)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "GridSyncStructCtorDeviceFunc", NativeStackObstacle.StructMemberSplit)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "CallsGridSyncStructCtorDeviceKernel", NativeStackObstacle.StructMemberSplit)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "ConstrainedGenMethodKernel", NativeStackObstacle.StructMemberSplit)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "ConstrainedGenMethodPlainKernel", NativeStackObstacle.NoBlockBarrier)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "PartialGenericKernel", NativeStackObstacle.StructMemberSplit)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "PartialGenericPlainKernel", NativeStackObstacle.NoBlockBarrier)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "PartialStructGenericDeviceFunctionKernel", NativeStackObstacle.None)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "PartialGenericStructDeviceFunctionKernel", NativeStackObstacle.None)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "PartialPolyNonExtAttrDefKernel", NativeStackObstacle.None)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "PartialPolyNonExtAttrImplKernel", NativeStackObstacle.None)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "PartialPolyExtAttrDefKernel", NativeStackObstacle.None)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "PartialPolyExtAttrImplKernel", NativeStackObstacle.None)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "PartialPolyIndirectAttrDefKernel", NativeStackObstacle.None)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "PartialPolyIndirectAttrImplKernel", NativeStackObstacle.None)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "PartialPolyBarrierAttrDefKernel", NativeStackObstacle.RecursiveSuspendingCall)]
    [InlineData("NativeStackEligibilityCases.Kernels",
        "PartialPolyIndirectBarrierAttrImplKernel", NativeStackObstacle.RecursiveSuspendingCall)]
    internal void Classify_KernelBody_ReturnsExpectedObstacle(
        string typeName,
        string methodName,
        NativeStackObstacle expected)
    {
        var (symbol, declaration, model) = GetKernel(s_compilation, typeName, methodName);

        Assert.Equal(expected, NativeStackEligibility.Classify(symbol, declaration, model));
    }

    // Pre-E4-2: operator macro target is skipped until E4-2 enables suspending operator macros
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("GridSyncMacroAfterBarrierKernel", NativeStackObstacle.None)]
    [InlineData("BarrierMacroWithoutDirectBarrierKernel", NativeStackObstacle.NoBlockBarrier)]
    internal void Classify_SuspendingOperatorMacroTarget_SkipsMacroTarget(
        string methodName,
        NativeStackObstacle expected)
    {
        var (symbol, declaration, model) = GetKernel(s_compilation, "NativeStackOperatorMacroCases.Kernels", methodName);

        Assert.Equal(expected, NativeStackEligibility.Classify(symbol, declaration, model));
    }

    // The visit counts of a kernel with size - 1 collectives of its own and no calls take size slots
    // The reason of a failed stack lowering goes into the kernel's C++ text as one comment line: a line break or a
    // backslash (which would continue the comment) becomes a space
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void StackBodyFailureComment_ReasonWithLineBreaksAndBackslash_IsOneCommentLine()
    {
        var comment = Kassyi.GSharp.Compiler.NativeStackTranslator.StackBodyFailureComment(
            "Kernels.K: InvalidOperationException: a\r\nb\\");

        Assert.StartsWith("// G#: runs on the state machine", comment, StringComparison.Ordinal);
        Assert.EndsWith("Kernels.K: InvalidOperationException: a  b " + Environment.NewLine, comment,
            StringComparison.Ordinal);
        Assert.Equal(comment.Length - Environment.NewLine.Length,
            comment.IndexOfAny(['\r', '\n']));
    }

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

        var (groupSymbol, groupDeclaration, groupModel) =
            GetKernel(consumerCompilation, "ConsumerApp.Kernels", "PassRemoteMethodGroupKernel");
        Assert.Equal(
            NativeStackObstacle.ImportedSuspendingCall,
            NativeStackEligibility.Classify(groupSymbol, groupDeclaration, groupModel));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("PriorConstKernel", NativeStackObstacle.None)]
    [InlineData("OutVarInsideUsingKernel", NativeStackObstacle.None)]
    [InlineData("OutVarInIfInsideUsingKernel", NativeStackObstacle.None)]
    [InlineData("TernaryOutVarWithMaskInsideUsingKernel", NativeStackObstacle.None)]
    [InlineData("ConstAfterAnchorKernel", NativeStackObstacle.StackBodyLowering)]
    [InlineData("UsingResourceCapturedKernel", NativeStackObstacle.StackBodyLowering)]
    [InlineData("OutVarAfterAnchorKernel", NativeStackObstacle.StackBodyLowering)]
    [InlineData("OutVarInIfAfterAnchorKernel", NativeStackObstacle.StackBodyLowering)]
    [InlineData("OutVarInUsingHeaderKernel", NativeStackObstacle.StackBodyLowering)]
    [InlineData("MutableLocalAfterAnchorKernel", NativeStackObstacle.StackBodyLowering)]
    [InlineData("AndAlsoOutVarWithMaskAfterAnchorKernel", NativeStackObstacle.StackBodyLowering)]
    [InlineData("TernaryOutVarWithMaskAfterAnchorKernel", NativeStackObstacle.StackBodyLowering)]
    internal void Classify_LocalFunctionAfterUsingDeclaration_DistinguishesCapturePosition(
        string methodName,
        NativeStackObstacle expected)
    {
        const string Source = """
            using System;
            using Kassyi.GSharp.Core;

            namespace NativeLocalFuncCases;

            public struct ScopeGuard : IDisposable
            {
                public uint[] Arr;
                public int I;

                public void Dispose() => Arr[I] += 0x100000u;
            }

            public static partial class Kernels
            {
                [GpuDeviceFunction]
                private static void AssignOut(int tid, out int picked) => picked = tid;

                [GpuDeviceFunction]
                private static bool AssignOutBool(int tid, out int picked)
                {
                    picked = tid;
                    return tid > 0;
                }

                [GpuDeviceFunction]
                private static int AssignOutRet(int tid, out int picked)
                {
                    picked = tid;
                    return tid;
                }

                [GpuDeviceFunction]
                private static bool AssignOutBoolWithMask(int tid, out int picked)
                {
                    picked = tid + (int)(Warp.ActiveMask() & 0xFu);
                    return tid > 0;
                }

                [GpuDeviceFunction]
                private static int AssignOutRetWithMask(int tid, out int picked)
                {
                    picked = tid + (int)(Warp.ActiveMask() & 0xFu);
                    return picked;
                }

                [GpuKernel]
                public static void PriorConstKernel(uint[] result)
                {
                    int t = ThreadIndex.X;
                    const int C = 5;
                    int a = AddConst(1);
                    using var guard = new ScopeGuard { Arr = result, I = t };
                    int AddConst(int v) => v + C;
                    result[t] += (uint)a + (Warp.ActiveMask() & 0xFu);
                }

                [GpuKernel]
                public static void OutVarInsideUsingKernel(uint[] result)
                {
                    int t = ThreadIndex.X;
                    int a = WriteOut(7);
                    using var guard = new ScopeGuard { Arr = result, I = t };
                    AssignOut(t, out var picked);
                    int WriteOut(int v)
                    {
                        picked = v;
                        return picked;
                    }

                    result[t] += (uint)a + (uint)picked + (Warp.ActiveMask() & 0xFu);
                }

                [GpuKernel]
                public static void OutVarInIfInsideUsingKernel(uint[] result)
                {
                    int t = ThreadIndex.X;
                    int a = WriteOut(7);
                    using var guard = new ScopeGuard { Arr = result, I = t };
                    if (t < 8 && AssignOutBool(5, out var picked))
                    {
                        result[t] += (uint)picked;
                    }

                    int WriteOut(int v)
                    {
                        picked = v;
                        return picked;
                    }

                    result[t] += (uint)a + (Warp.ActiveMask() & 0xFu);
                }

                [GpuKernel]
                public static void TernaryOutVarWithMaskInsideUsingKernel(uint[] result)
                {
                    int t = ThreadIndex.X;
                    int a = WriteOut(7);
                    using var guard = new ScopeGuard { Arr = result, I = t };
                    int y = t < 8 ? AssignOutRetWithMask(5, out var picked) : 0;
                    int WriteOut(int v)
                    {
                        picked = v;
                        return picked;
                    }

                    result[t] += (uint)a + (uint)y + (Warp.ActiveMask() & 0xFu);
                }

                [GpuKernel]
                public static void ConstAfterAnchorKernel(uint[] result)
                {
                    int t = ThreadIndex.X;
                    int a = AddConst(1);
                    const int C = 5;
                    using var guard = new ScopeGuard { Arr = result, I = t };
                    int AddConst(int v) => v + C;
                    result[t] += (uint)a + (Warp.ActiveMask() & 0xFu);
                }

                [GpuKernel]
                public static void UsingResourceCapturedKernel(uint[] result)
                {
                    int t = ThreadIndex.X;
                    if (false)
                    {
                        _ = ReadGuard();
                    }

                    using var guard = new ScopeGuard { Arr = result, I = t };
                    int ReadGuard() => guard.I;
                    result[t] += Warp.ActiveMask() & 0xFu;
                }

                [GpuKernel]
                public static void OutVarAfterAnchorKernel(uint[] result)
                {
                    int t = ThreadIndex.X;
                    int a = WriteOut(7);
                    AssignOut(t, out var z);
                    using var guard = new ScopeGuard { Arr = result, I = t };
                    int WriteOut(int v)
                    {
                        z = v + 10;
                        return z;
                    }

                    result[t] += (uint)a + (uint)z + (Warp.ActiveMask() & 0xFu);
                }

                [GpuKernel]
                public static void OutVarInIfAfterAnchorKernel(uint[] result)
                {
                    int t = ThreadIndex.X;
                    int a = WriteOut(7);
                    if (t < 8 && AssignOutBool(5, out var z))
                    {
                        result[t] += (uint)z;
                    }

                    using var guard = new ScopeGuard { Arr = result, I = t };
                    int WriteOut(int v)
                    {
                        z = v + 10;
                        return z;
                    }

                    result[t] += (uint)a + (Warp.ActiveMask() & 0xFu);
                }

                [GpuKernel]
                public static void OutVarInUsingHeaderKernel(uint[] result)
                {
                    int t = ThreadIndex.X;
                    int a = WriteOut(7);
                    using var guard = new ScopeGuard { Arr = result, I = AssignOutRet(t, out var z) };
                    int WriteOut(int v)
                    {
                        z = v + 10;
                        return z;
                    }

                    result[t] += (uint)a + (uint)z + (Warp.ActiveMask() & 0xFu);
                }

                [GpuKernel]
                public static void MutableLocalAfterAnchorKernel(uint[] result)
                {
                    int t = ThreadIndex.X;
                    int a = WriteX(7);
                    int x = 3;
                    using var guard = new ScopeGuard { Arr = result, I = t };
                    int WriteX(int v)
                    {
                        x = v + 10;
                        return x;
                    }

                    result[t] += (uint)a + (uint)x + (Warp.ActiveMask() & 0xFu);
                }

                [GpuKernel]
                public static void AndAlsoOutVarWithMaskAfterAnchorKernel(uint[] result)
                {
                    int t = ThreadIndex.X;
                    int a = WriteOut(7);
                    if (t < 8 && AssignOutBoolWithMask(5, out var z))
                    {
                        result[t] += (uint)z;
                    }

                    using var guard = new ScopeGuard { Arr = result, I = t };
                    int WriteOut(int v)
                    {
                        z = v + 10;
                        return z;
                    }

                    result[t] += (uint)a + (Warp.ActiveMask() & 0xFu);
                }

                [GpuKernel]
                public static void TernaryOutVarWithMaskAfterAnchorKernel(uint[] result)
                {
                    int t = ThreadIndex.X;
                    int a = WriteOut(7);
                    int y = t < 8 ? AssignOutRetWithMask(5, out var z) : 0;
                    using var guard = new ScopeGuard { Arr = result, I = t };
                    int WriteOut(int v)
                    {
                        z = v + 10;
                        return z;
                    }

                    result[t] += (uint)a + (uint)y + (Warp.ActiveMask() & 0xFu);
                }
            }
            """;
        var compilation = RoslynTestUtility.CreateCompilation(Source);
        var (symbol, declaration, model) = GetKernel(compilation, "NativeLocalFuncCases.Kernels", methodName);

        Assert.Equal(expected, NativeStackEligibility.Classify(symbol, declaration, model));
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
