#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Tests.Shared.Helpers;
using VerifyXunit;

namespace Kassyi.GSharp.Tests.Cpu.Generator;

[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class ManagedStackModelGeneratorTests
{
    private static readonly CSharpParseOptions s_parseOptions = new(LanguageVersion.Preview);

    private static readonly SyntaxTree s_tree =
        RoslynTestUtility.GetTestCaseContext("Generators/ManagedStackModelCases.cs").Tree;

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void BarrierModel_UnsetStateMachineOrUnknown_ProducesIdenticalSources()
    {
        var unset = Run(barrierModel: null);
        var stateMachine = Run(barrierModel: "StateMachine");
        var unknown = Run(barrierModel: "Stak");

        AssertSameSources(unset, stateMachine);
        AssertSameSources(unset, unknown);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void BarrierModel_StackCaseInsensitive_MatchesAndDiffersFromDefault()
    {
        var unset = Run(barrierModel: null);
        var upper = Run(barrierModel: "Stack");
        var lower = Run(barrierModel: "stack");

        AssertSameSources(upper, lower);
        Assert.NotEqual(
            unset.Select(static s => (s.HintName, s.SourceText)),
            upper.Select(static s => (s.HintName, s.SourceText)));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task BarrierModel_Stack_EmitsStackFormsForBarriersAndWarpCollectives()
    {
        var sources = Run(barrierModel: "Stack");
        var combined = string.Join(
            "\n\n",
            sources
                .Where(static s => s.HintName.EndsWith("StackModelKernels.g.cs", StringComparison.Ordinal) ||
                                   s.HintName.EndsWith("StackModelKernels.DeviceStateMachines.g.cs",
                                       StringComparison.Ordinal))
                .Select(static s => $"// === {s.HintName} ===\n{s.SourceText}"));

        return Verifier.Verify(combined, "cs")
            .UseDirectory("Snapshots")
            .UseFileName("Snap.ManagedStackModel_Stack_Cpu");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void StackFormThatCannotBeMade_EitherModel_BuildsAndTheStackModelListsTheFallback()
    {
        var unset = Run(barrierModel: null, s_fallbackTree);
        var stack = Run(barrierModel: "Stack", s_fallbackTree);

        Assert.DoesNotContain(unset, static s => s.HintName == StateMachineFallbackPipeline.HintName);
        var failures = Assert.Single(stack, static s => s.HintName == StateMachineFallbackPipeline.HintName);
        // PassesSuspendingGroupKernel passes BarrierStep (which has no stack form) as a method group, so the kernel is
        // kept off the stacks before its form is made, not by a failure to make it
        Assert.DoesNotContain("// managed\tFallbackKernels.PassesSuspendingGroupKernel\tStackFormGenerationFailed",
            failures.SourceText, StringComparison.Ordinal);
        // A device function whose form cannot be made takes the forms of its callers along, up the whole chain
        Assert.Contains("// managed\tFallbackKernels.Inner\tStackFormGenerationFailed: InvalidOperationException",
            failures.SourceText, StringComparison.Ordinal);
        Assert.Contains("// managed\tFallbackKernels.Middle\tCallsFunctionWithoutForm: calls a function without a stack form",
            failures.SourceText, StringComparison.Ordinal);
        Assert.Contains("// managed\tFallbackKernels.Outer\tCallsFunctionWithoutForm: calls a function without a stack form",
            failures.SourceText, StringComparison.Ordinal);
        Assert.Contains("// managed\tFallbackKernels.CallsChainKernel\tCallsFunctionWithoutForm: calls a function without a stack form",
            failures.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("FallbackKernels.UsingAfterContinueKernel", failures.SourceText,
            StringComparison.Ordinal);
        // The kernels and the functions of the chain keep their state machines: no stack form is emitted for them, and
        // their launchers do not call ManagedStackRunner.Run. Only AddShuffled and UsingAfterContinueKernel have stack
        // forms of their own
        foreach (var name in new[]
                 {
                     "PassesSuspendingGroupKernel", "BarrierStep", "CallsChainKernel", "Outer", "Middle", "Inner"
                 })
        {
            var formName = GSharpInternalNames.StackModel.MethodPrefix +
                           "ManagedStackFallbackGeneratorCases__FallbackKernels__" + name + "(";
            Assert.DoesNotContain(stack, s => s.SourceText.Contains(formName, StringComparison.Ordinal));
        }

        const string AddShuffledForm =
            GSharpInternalNames.StackModel.MethodPrefix + "ManagedStackFallbackGeneratorCases__FallbackKernels__AddShuffled(";
        Assert.Contains(stack, static s => s.SourceText.Contains(AddShuffledForm, StringComparison.Ordinal));
        const string UsingAfterContinueForm =
            GSharpInternalNames.StackModel.MethodPrefix +
            "ManagedStackFallbackGeneratorCases__FallbackKernels__UsingAfterContinueKernel(";
        Assert.Contains(stack, static s => s.SourceText.Contains(UsingAfterContinueForm, StringComparison.Ordinal));

        var unsetKernelFile = Assert.Single(unset, static s => s.HintName.EndsWith("__FallbackKernels.g.cs",
            StringComparison.Ordinal));
        var stackKernelFile = Assert.Single(stack, static s => s.HintName.EndsWith("__FallbackKernels.g.cs",
            StringComparison.Ordinal));
        Assert.NotEqual(unsetKernelFile.SourceText, stackKernelFile.SourceText);
        Assert.Contains("FallbackKernels.UsingAfterContinueKernel\", true,", stackKernelFile.SourceText,
            StringComparison.Ordinal);
        Assert.DoesNotContain("FallbackKernels.PassesSuspendingGroupKernel\", true,", stackKernelFile.SourceText,
            StringComparison.Ordinal);
        Assert.DoesNotContain("FallbackKernels.CallsChainKernel\", true,", stackKernelFile.SourceText,
            StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void StackScopeKernels_ManagedStackBuild_RunOnStacksWithoutFailures()
    {
        var path = RepositoryPaths.Combine(
            "tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "Differential", "StackScopeKernels.cu.cs");
        var (_, tree) = RoslynTestUtility.GetCallerSourceContext(path);
        var stack = Run(barrierModel: "Stack", tree);

        Assert.DoesNotContain(stack, static s => s.HintName == StateMachineFallbackPipeline.HintName);
        var kernelFile = Assert.Single(stack, static s => s.HintName.EndsWith("__StackScopeKernels.g.cs",
            StringComparison.Ordinal));
        var kernels = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(static m => m.AttributeLists.SelectMany(static l => l.Attributes)
                .Any(static a => a.Name is IdentifierNameSyntax { Identifier.ValueText: "GpuKernel" }))
            .Select(static m => m.Identifier.ValueText)
            .ToArray();
        Assert.Equal(19, kernels.Length);
        Assert.Contains("ManagedStackRunner.Run(", kernelFile.SourceText, StringComparison.Ordinal);
        foreach (var kernel in kernels)
        {
            Assert.Contains($"sharedMemBytes, \"StackScopeKernels.{kernel}\"", kernelFile.SourceText,
                StringComparison.Ordinal);
        }

        Assert.Contains("sharedMemBytes, \"StackScopeKernels.UsingDeclarationMaskKernel\", true,",
            kernelFile.SourceText, StringComparison.Ordinal);
        Assert.Contains("sharedMemBytes, \"StackScopeKernels.ContinueInsideFixedInLoopKernel\", true,",
            kernelFile.SourceText, StringComparison.Ordinal);
        Assert.Contains("sharedMemBytes, \"StackScopeKernels.GotoLoopCrossingUsingStatementKernel\", true,",
            kernelFile.SourceText, StringComparison.Ordinal);
        Assert.Contains("sharedMemBytes, \"StackScopeKernels.GotoLoopCrossingFixedStatementKernel\", true,",
            kernelFile.SourceText, StringComparison.Ordinal);
        Assert.Contains(
            "sharedMemBytes, \"StackScopeKernels.LocalFunctionCapturingPriorConstAfterUsingDeclarationKernel\", true,",
            kernelFile.SourceText, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void LocalFunctionCapturingUnmovableAfterAnchor_StackModel_FallsBackWithoutCompileError()
    {
        const string Source = """
            using System;
            using Kassyi.GSharp.Core;

            namespace LocalFunctionFallbackCases;

            public struct ScopeGuard : IDisposable
            {
                public uint[] Arr;
                public int I;

                public void Dispose() => Arr[I] += 0x100000u;
            }

            [SkipSnapshotConsistency("Input for ManagedStackModelGeneratorTests")]
            public static partial class FallbackKernels
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
        var stack = Run(barrierModel: "Stack", RoslynTestUtility.CreateCompilation(Source));

        var failures = Assert.Single(stack, static s => s.HintName == StateMachineFallbackPipeline.HintName);
        Assert.Contains("// managed\tFallbackKernels.ConstAfterAnchorKernel\tStackFormGenerationFailed: InvalidOperationException",
            failures.SourceText, StringComparison.Ordinal);
        Assert.Contains("// managed\tFallbackKernels.UsingResourceCapturedKernel\tStackFormGenerationFailed: InvalidOperationException",
            failures.SourceText, StringComparison.Ordinal);
        Assert.Contains("// managed\tFallbackKernels.OutVarAfterAnchorKernel\tStackFormGenerationFailed: InvalidOperationException",
            failures.SourceText, StringComparison.Ordinal);
        Assert.Contains("// managed\tFallbackKernels.OutVarInIfAfterAnchorKernel\tStackFormGenerationFailed: InvalidOperationException",
            failures.SourceText, StringComparison.Ordinal);
        Assert.Contains("// managed\tFallbackKernels.OutVarInUsingHeaderKernel\tStackFormGenerationFailed: InvalidOperationException",
            failures.SourceText, StringComparison.Ordinal);
        Assert.Contains("// managed\tFallbackKernels.MutableLocalAfterAnchorKernel\tStackFormGenerationFailed: InvalidOperationException",
            failures.SourceText, StringComparison.Ordinal);
        Assert.Contains("// managed\tFallbackKernels.AndAlsoOutVarWithMaskAfterAnchorKernel\tStackFormGenerationFailed: InvalidOperationException",
            failures.SourceText, StringComparison.Ordinal);
        Assert.Contains("// managed\tFallbackKernels.TernaryOutVarWithMaskAfterAnchorKernel\tStackFormGenerationFailed: InvalidOperationException",
            failures.SourceText, StringComparison.Ordinal);
        var kernelFile = Assert.Single(stack, static s => s.HintName.EndsWith("__FallbackKernels.g.cs",
            StringComparison.Ordinal));
        Assert.DoesNotContain("ManagedStackRunner.Run(", kernelFile.SourceText, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalFunctionCapturingPriorConstAndOutVarInsideUsing_StackModel_RunsOnStackWithoutFailures()
    {
        const string Source = """
            using System;
            using Kassyi.GSharp.Core;

            namespace LocalFunctionStackCases;

            public struct ScopeGuard : IDisposable
            {
                public uint[] Arr;
                public int I;

                public void Dispose() => Arr[I] += 0x100000u;
            }

            [SkipSnapshotConsistency("Input for ManagedStackModelGeneratorTests")]
            public static partial class StackKernels
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
            }
            """;
        var stack = Run(barrierModel: "Stack", RoslynTestUtility.CreateCompilation(Source));

        Assert.DoesNotContain(stack, static s => s.HintName == StateMachineFallbackPipeline.HintName);
        var kernelFile = Assert.Single(stack, static s => s.HintName.EndsWith("__StackKernels.g.cs",
            StringComparison.Ordinal));
        Assert.Contains("sharedMemBytes, \"StackKernels.PriorConstKernel\", true,", kernelFile.SourceText,
            StringComparison.Ordinal);
        Assert.Contains("sharedMemBytes, \"StackKernels.OutVarInsideUsingKernel\", true,", kernelFile.SourceText,
            StringComparison.Ordinal);
        Assert.Contains("sharedMemBytes, \"StackKernels.OutVarInIfInsideUsingKernel\", true,", kernelFile.SourceText,
            StringComparison.Ordinal);
        Assert.Contains("sharedMemBytes, \"StackKernels.TernaryOutVarWithMaskInsideUsingKernel\", true,",
            kernelFile.SourceText, StringComparison.Ordinal);
    }

    // An operator macro whose target reaches a warp collective, after a block barrier, in a kernel that has a
    // collective of its own, and in a kernel that reaches Warp.ActiveMask() (whose stack form writes the lowered body)
    // with a target that reaches Warp.ActiveMask(): the stack forms of the kernels cannot be made, the build still
    // compiles (Run checks it), the stack model lists the reason, and the kernels keep the launchers of the default
    // build
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void OperatorMacroReachingAWarpCollective_StackModel_KeepsTheKernelsOnTheStateMachine()
    {
        var unset = Run(barrierModel: null, s_macroTree);
        var stack = Run(barrierModel: "Stack", s_macroTree);

        Assert.DoesNotContain(unset, static s => s.HintName == StateMachineFallbackPipeline.HintName);
        var failures = Assert.Single(stack, static s => s.HintName == StateMachineFallbackPipeline.HintName);
        Assert.Contains("// managed\tMacroKernels.MacroAfterBarrierKernel\tStackFormGenerationFailed: InvalidOperationException",
            failures.SourceText, StringComparison.Ordinal);
        Assert.Contains("// managed\tMacroKernels.MacroAndShuffleKernel\tStackFormGenerationFailed: InvalidOperationException",
            failures.SourceText, StringComparison.Ordinal);
        Assert.Contains("// managed\tMacroKernels.MacroAndActiveMaskKernel\tStackFormGenerationFailed: InvalidOperationException",
            failures.SourceText, StringComparison.Ordinal);
        Assert.Contains("through an operator macro", failures.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain(stack, static s => s.SourceText.Contains("ManagedStackRunner.Run(",
            StringComparison.Ordinal));
        var kernelFile = Assert.Single(unset, static s => s.HintName.EndsWith("MacroKernels.g.cs",
            StringComparison.Ordinal));
        Assert.Equal(kernelFile, Assert.Single(stack, static s => s.HintName.EndsWith("MacroKernels.g.cs",
            StringComparison.Ordinal)));
    }

    // The kernels whose locals the lowering for the stacks leaves out of the scope of their uses, untyped or not
    // definitely assigned: under the stack model every one of them gets a stack form that compiles (Run checks that the
    // output has no error) and runs on the stack scheduler, with no fallback
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalsOutOfTheirScope_StackModel_CompileAndRunOnTheStacks()
    {
        var tree = CSharpSyntaxTree.ParseText(
            File.ReadAllText(RepositoryPaths.Combine(
                "tests", "Compiler", "Kassyi.GSharp.Tests.Cpu", "Runtime", "StackBodyLocalsKernels.cu.cs")),
            s_parseOptions,
            "StackBodyLocalsKernels.cu.cs");

        var stack = Run(barrierModel: "Stack", tree);

        Assert.DoesNotContain(stack, static s => s.HintName == StateMachineFallbackPipeline.HintName);
        var kernelFile = Assert.Single(stack, static s => s.HintName.EndsWith("__StackBodyLocalsKernels.g.cs",
            StringComparison.Ordinal));
        var kernels = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(static m => m.AttributeLists.SelectMany(static l => l.Attributes)
                .Any(static a => a.Name is IdentifierNameSyntax { Identifier.ValueText: "GpuKernel" }))
            .Select(static m => m.Identifier.ValueText)
            .ToArray();
        Assert.Equal(17, kernels.Length);
        foreach (var kernel in kernels)
        {
            Assert.Contains($"sharedMemBytes, \"StackBodyLocalsKernels.{kernel}\", true,", kernelFile.SourceText,
                StringComparison.Ordinal);
        }
    }

    // A scoped span declared without a value: its stack form declares it with default(Span<uint>), and the form holds no
    // C# error. Only the stack form is checked: the state machine of the same kernel does not compile yet (it writes
    // scoped twice), which is a problem of the default build of its own
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ScopedLocalWithoutValue_StackModel_FormDeclaresItWithTheDefaultOfItsType()
    {
        var tree = RoslynTestUtility.GetTestCaseContext("Generators/StackBodyScopedCases.cs").Tree;
        var compilation = CSharpCompilation.Create("ManagedStackModelCase")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true))
            .AddReferences(RoslynTestUtility.BaseReferences)
            .AddSyntaxTrees(tree);
        CSharpGeneratorDriver.Create(
                generators: [new GpuKernelGenerator().AsSourceGenerator()],
                additionalTexts: null,
                parseOptions: s_parseOptions,
                optionsProvider: new TestAnalyzerConfigOptionsProvider("Cpu",
                    new Dictionary<string, string> { ["build_property.GSharpManagedBarrierModel"] = "Stack" }))
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var form = output.SyntaxTrees
            .SelectMany(static t => t.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            .Single(static m => m.Identifier.ValueText ==
                                GSharpInternalNames.StackModel.MethodPrefix +
                                "StackBodyScopedCases__ScopedKernels__ScopedSpanKernel");
        var declaration = form.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Single(static d => d.Identifier.ValueText == "s");
        var initializer = Assert.IsType<DefaultExpressionSyntax>(declaration.Initializer?.Value);
        Assert.IsNotType<ScopedTypeSyntax>(initializer.Type);
        Assert.Empty(output.GetSemanticModel(form.SyntaxTree)
            .GetDiagnostics(form.Span)
            .Where(static d => d.Severity == DiagnosticSeverity.Error));
    }

    // A device function whose own stack form fails (passes a warp-reaching function as a method group) cannot be
    // called or passed as a method group from another stack form: the caller keeps its state machine with the failure
    // line instead of emitting a call to the missing stack form (CS0117), while GSH0003 on the non-lambda macro body
    // stays
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task MethodGroupToFailedStackForm_StackModel_ReportsFailureWithoutCompileError()
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Generators/ManagedStackMethodGroupFallbackCases.cs"));
        var defaultRun = await KernelDiagnostics.RunCompilationAsync(compilation, "Cpu");
        var stackRun = await KernelDiagnostics.RunCompilationAsync(
            compilation,
            "Cpu",
            new Dictionary<string, string> { ["build_property.GSharpManagedBarrierModel"] = "Stack" });

        Assert.Equal(2, stackRun.Analyzer.Count(static d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id));
        Assert.Empty(defaultRun.Output.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(stackRun.Output.GetDiagnostics(), static d => d.Id == "CS0117");
        Assert.Empty(stackRun.Output.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));

        var failuresTree = Assert.Single(stackRun.Output.SyntaxTrees,
            static t => Path.GetFileName(t.FilePath) == StateMachineFallbackPipeline.HintName);
        var failuresText = failuresTree.GetText().ToString();
        Assert.Contains(
            "// managed\tMethodGroupFallbackKernels.InnerStep\tStackFormGenerationFailed: InvalidOperationException: A stack form cannot pass 'ShuffleStep', which reaches a warp collective, as a method group.",
            failuresText,
            StringComparison.Ordinal);
        Assert.Contains(
            "// managed\tMethodGroupFallbackKernels.PassesFailedGroupKernel\tCallsFunctionWithoutForm: passes MethodGroupFallbackKernels.InnerStep, which has no stack form, as a method group",
            failuresText,
            StringComparison.Ordinal);

        var defaultKernelText = Assert.Single(defaultRun.Output.SyntaxTrees,
            static t => t.FilePath.EndsWith("MethodGroupFallbackKernels.g.cs", StringComparison.Ordinal))
            .GetText().ToString();
        var stackKernelText = Assert.Single(stackRun.Output.SyntaxTrees,
            static t => t.FilePath.EndsWith("MethodGroupFallbackKernels.g.cs", StringComparison.Ordinal))
            .GetText().ToString();
        Assert.Equal(defaultKernelText, stackKernelText);
        Assert.DoesNotContain(
            GSharpInternalNames.StackModel.MethodPrefix +
            "ManagedStackMethodGroupFallbackCases__MethodGroupFallbackKernels__PassesFailedGroupKernel(",
            stackKernelText,
            StringComparison.Ordinal);

        var deviceText = Assert.Single(stackRun.Output.SyntaxTrees,
            static t => t.FilePath.EndsWith("MethodGroupFallbackKernels.DeviceStateMachines.g.cs",
                StringComparison.Ordinal))
            .GetText().ToString();
        Assert.DoesNotContain(
            GSharpInternalNames.StackModel.MethodPrefix +
            "ManagedStackMethodGroupFallbackCases__MethodGroupFallbackKernels__InnerStep(",
            deviceText,
            StringComparison.Ordinal);
        Assert.Contains(
            GSharpInternalNames.StackModel.MethodPrefix +
            "ManagedStackMethodGroupFallbackCases__MethodGroupFallbackKernels__ShuffleStep(",
            deviceText,
            StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task MethodGroupCyclesGenericPartialAndUserNameof_StackModel_CompileWithoutCrashOrCs0117()
    {
        var source = File.ReadAllText(RepositoryPaths.Combine(
            "tests", "Compiler", "Kassyi.GSharp.Tests.Cpu", "Generator", "Cases",
            "ManagedStackMethodGroupEdgeCases.cs"));
        var compilation = RoslynTestUtility.CreateCompilation(source);
        var defaultRun = await KernelDiagnostics.RunCompilationAsync(compilation, "Cpu");
        var stackRun = await KernelDiagnostics.RunCompilationAsync(
            compilation,
            "Cpu",
            new Dictionary<string, string> { ["build_property.GSharpManagedBarrierModel"] = "Stack" });

        Assert.Empty(defaultRun.Output.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(stackRun.Output.GetDiagnostics(), static d => d.Id == "CS0117");
        Assert.DoesNotContain(stackRun.Output.GetDiagnostics(), static d => d.Id == "CS0123");
        Assert.Empty(stackRun.Output.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));

        var failuresText = Assert.Single(stackRun.Output.SyntaxTrees,
                static t => Path.GetFileName(t.FilePath) == StateMachineFallbackPipeline.HintName)
            .GetText().ToString();
        Assert.Contains(
            "// managed\tMethodGroupEdgeKernels.PassesGenericGroupKernel\tCallsFunctionWithoutForm: passes MethodGroupEdgeKernels.GenericStep, which has no stack form, as a method group",
            failuresText,
            StringComparison.Ordinal);
        Assert.Contains(
            "// managed\tMethodGroupEdgeKernels.PassesPartialGroupKernel\tCallsFunctionWithoutForm: passes MethodGroupEdgeKernels.PartialStep, which has no stack form, as a method group",
            failuresText,
            StringComparison.Ordinal);
        Assert.Contains(
            "// managed\tUserNameofMethodGroupKernels.CallsUserNameofKernel\tStackFormGenerationFailed: InvalidOperationException: A stack form cannot pass 'ShuffleStep', which reaches a warp collective, as a method group.",
            failuresText,
            StringComparison.Ordinal);

        var defaultKernelText = Assert.Single(defaultRun.Output.SyntaxTrees,
                static t => t.FilePath.EndsWith("MethodGroupEdgeKernels.g.cs", StringComparison.Ordinal))
            .GetText().ToString();
        var stackKernelText = Assert.Single(stackRun.Output.SyntaxTrees,
                static t => t.FilePath.EndsWith("MethodGroupEdgeKernels.g.cs", StringComparison.Ordinal))
            .GetText().ToString();
        Assert.Equal(defaultKernelText, stackKernelText);

        var deviceText = Assert.Single(stackRun.Output.SyntaxTrees,
                static t => t.FilePath.EndsWith("MethodGroupEdgeKernels.DeviceStateMachines.g.cs",
                    StringComparison.Ordinal))
            .GetText().ToString();
        foreach (var omitted in new[] { "SelfGroup", "GenericStep", "PartialStep" })
        {
            Assert.DoesNotContain(
                GSharpInternalNames.StackModel.MethodPrefix +
                "ManagedStackMethodGroupEdgeCases__MethodGroupEdgeKernels__" + omitted,
                deviceText,
                StringComparison.Ordinal);
        }

        Assert.Contains(
            GSharpInternalNames.StackModel.MethodPrefix +
            "ManagedStackMethodGroupEdgeCases__MethodGroupEdgeKernels__ShuffleStep(",
            deviceText,
            StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void ImportedSuspendingWithoutManagedStackForm_StackModel_KeepsCallerOnStateMachineWithFailureLine()
    {
        var library = DeviceLibraryBuilder.Build(
            "XasmLib",
            new Dictionary<string, string> { ["build_property.GSharpExportDeviceCode"] = "false" },
            RoslynTestUtility.ReadTestCase("Diagnostics/CrossAssembly/Library.cs"),
            HandWrittenExportRecords.Read("Diagnostics/CrossAssembly/HandWrittenSuspendingRecord.cs"));
        var consumer = DeviceLibraryBuilder.CreateConsumer(
            RoslynTestUtility.ReadTestCase("Diagnostics/CrossAssembly/Consumer.cs"),
            library);

        var driver = CSharpGeneratorDriver.Create(
                generators: [new GpuKernelGenerator().AsSourceGenerator()],
                additionalTexts: null,
                parseOptions: s_parseOptions,
                optionsProvider: new TestAnalyzerConfigOptionsProvider("Cpu",
                    new Dictionary<string, string> { ["build_property.GSharpManagedBarrierModel"] = "Stack" }))
            .RunGenerators(consumer);

        var result = driver.GetRunResult().Results.Single();
        Assert.Null(result.Exception);
        var failures = Assert.Single(result.GeneratedSources,
            static s => s.HintName == StateMachineFallbackPipeline.HintName).SourceText.ToString();
        Assert.Contains(
            "// managed\tConsumerKernels.CallKernel\tImportedSuspendingCall: calls LibMath.Twice of XasmLib, which has no stack form",
            failures, StringComparison.Ordinal);
        Assert.DoesNotContain(result.GeneratedSources,
            static s => s.SourceText.ToString().Contains("ManagedStackRunner.Run(", StringComparison.Ordinal));
    }

    private static readonly SyntaxTree s_macroTree =
        RoslynTestUtility.GetTestCaseContext("Generators/ManagedStackOperatorMacroCases.cs").Tree;

    private static readonly SyntaxTree s_fallbackTree =
        RoslynTestUtility.GetTestCaseContext("Generators/ManagedStackFallbackCases.cs").Tree;

    private static (string HintName, string SourceText)[] Run(string? barrierModel) => Run(barrierModel, s_tree);

    private static (string HintName, string SourceText)[] Run(string? barrierModel, SyntaxTree tree) =>
        Run(
            barrierModel,
            CSharpCompilation.Create("ManagedStackModelCase")
                .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true))
                .AddReferences(RoslynTestUtility.BaseReferences)
                .AddSyntaxTrees(tree));

    private static (string HintName, string SourceText)[] Run(string? barrierModel, Compilation compilation)
    {
        var options = barrierModel is null
            ? null
            : new Dictionary<string, string> { ["build_property.GSharpManagedBarrierModel"] = barrierModel };

        var driver = CSharpGeneratorDriver.Create(
                generators: [new GpuKernelGenerator().AsSourceGenerator()],
                additionalTexts: null,
                parseOptions: s_parseOptions,
                optionsProvider: new TestAnalyzerConfigOptionsProvider("Cpu", options))
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        var result = driver.GetRunResult().Results.Single();
        Assert.Null(result.Exception);
        Assert.Empty(generatorDiagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));

        return result.GeneratedSources
            .OrderBy(static s => s.HintName, StringComparer.Ordinal)
            .Select(static s => (s.HintName, s.SourceText.ToString()))
            .ToArray();
    }

    private static void AssertSameSources((string HintName, string SourceText)[] expected,
        (string HintName, string SourceText)[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].HintName, actual[i].HintName);
            Assert.Equal(expected[i].SourceText, actual[i].SourceText);
        }
    }
}
