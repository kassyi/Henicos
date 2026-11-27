#nullable enable
using System.Collections.Immutable;
using System.Linq;
using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class ActiveMaskIterationPlanTests
{
    private static (ActiveMaskIterationPlan Plan, MethodDeclarationSyntax Method, SemanticModel Model, ImmutableArray<InvocationExpressionSyntax> ActiveMaskCalls)
        PlanKernel(string kernelBody, string methodSignature = "public static void TestKernel(uint[] result, int n)", bool isDeviceFunction = false)
    {
        var source = $$"""
            using Kassyi.GSharp.Core;

            namespace TestNs;

            public static class TestKernels
            {
                {{(isDeviceFunction ? "[GpuDeviceFunction]" : "[GpuKernel]")}}
                {{methodSignature}}
                {
                    {{kernelBody}}
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Last();
        var model = compilation.GetSemanticModel(tree);
        var methodDecl = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static m => m.Identifier.Text == "TestKernel");
        var methodSymbol = Assert.IsAssignableFrom<IMethodSymbol>(model.GetDeclaredSymbol(methodDecl));
        var plan = ActiveMaskIterationPlanner.Plan(methodDecl, model, methodSymbol, isDeviceFunction);
        var calls = methodDecl.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(static inv => inv.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "ActiveMask",
                Expression: IdentifierNameSyntax { Identifier.ValueText: "Warp" }
            })
            .ToImmutableArray();
        return (plan, methodDecl, model, calls);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_CallAfterLoopWithConditionalBreak_ExcludesAfterLoopCall()
    {
        var (plan, _, _, calls) = PlanKernel("""
            int t = ThreadIndex.X;
            for (int i = 0; i < 4; i++)
            {
                if ((t & 1) == 0 && i == 1)
                {
                    break;
                }

                result[t + 32 * i] = Warp.ActiveMask();
            }

            result[t + 128] = Warp.ActiveMask();
            """);

        Assert.Equal(2, calls.Length);
        Assert.True(plan.TryGetKeys(calls[0], out var inLoopKeys));
        var key = Assert.Single(inLoopKeys);
        Assert.Equal(IterationKeyKind.LoopVariable, key.Kind);
        Assert.Equal("i", key.LoopVariable?.Name);

        Assert.False(plan.TryGetKeys(calls[1], out var afterLoopKeys));
        Assert.True(afterLoopKeys.IsEmpty);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ForwardGotoOutOfGotoLoop_KeysInsideLoopAndExcludesAfterLabel()
    {
        var (plan, _, _, calls) = PlanKernel("""
            int t = ThreadIndex.X;
            int p = 0;
            L1:
            {
                if ((t & 1) != 0 && p == 0)
                {
                    goto LOut;
                }

                result[t] = Warp.ActiveMask();
            }

            if ((t & 2) != 0 && p < 1)
            {
                p++;
                goto L1;
            }

            LOut:
            result[t + 32] = Warp.ActiveMask();
            """);

        Assert.Equal(2, calls.Length);
        Assert.True(plan.TryGetKeys(calls[0], out var inGotoKeys));
        var key = Assert.Single(inGotoKeys);
        Assert.Equal(IterationKeyKind.Counter, key.Kind);

        Assert.False(plan.TryGetKeys(calls[1], out _));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_CallAtGotoLoopGateInBlockAndElse_ExcludesGateCalls()
    {
        var (plan, _, _, calls) = PlanKernel("""
            int t = ThreadIndex.X;
            int p = 0;
            L1:
            result[t + 32 * p] = Warp.ActiveMask();
            if ((t & 3) != 0 && p < 2)
            {
                p++;
                goto L1;
            }
            else
            {
                result[t + 96] = Warp.ActiveMask();
            }

            result[t + 128] = Warp.ActiveMask();
            """);

        Assert.Equal(3, calls.Length);
        Assert.True(plan.TryGetKeys(calls[0], out var headerKeys));
        Assert.Single(headerKeys);
        Assert.False(plan.TryGetKeys(calls[1], out _));
        Assert.False(plan.TryGetKeys(calls[2], out _));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_CallAfterContinueGateLeavingWhileTrue_ExcludesLeavingCall()
    {
        var (plan, _, _, calls) = PlanKernel("""
            int t = ThreadIndex.X;
            int k = 0;
            while (true)
            {
                result[t + 32 * k] = Warp.ActiveMask();
                if ((t & 4) == 0)
                {
                    if ((t & 3) != 0 && k++ < 2)
                    {
                        continue;
                    }

                    result[t + 96] = Warp.ActiveMask();
                }

                break;
            }
            """);

        Assert.Equal(2, calls.Length);
        Assert.True(plan.TryGetKeys(calls[0], out var keys));
        var key = Assert.Single(keys);
        Assert.Equal(IterationKeyKind.Counter, key.Kind);
        Assert.False(plan.TryGetKeys(calls[1], out _));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_CallBeforeBreakInsideIf_KeysInsideLoopCall()
    {
        var (plan, _, _, calls) = PlanKernel("""
            int t = ThreadIndex.X;
            for (int i = 0; i < 4; i++)
            {
                if ((t & 1) == 0 && i == 1)
                {
                    result[t + 32] = Warp.ActiveMask();
                    break;
                }

                result[t + 64] = Warp.ActiveMask();
            }
            """);

        Assert.Equal(2, calls.Length);
        Assert.True(plan.TryGetKeys(calls[0], out var breakKeys));
        Assert.Single(breakKeys);
        Assert.True(plan.TryGetKeys(calls[1], out var bodyKeys));
        Assert.Single(bodyKeys);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_NestedGotoLoops_OrdersOutermostKeyFirst()
    {
        var (plan, _, _, calls) = PlanKernel("""
            int t = ThreadIndex.X;
            int outer = 0;
            LOuter:
            result[t] = Warp.ActiveMask();
            int inner = 0;
            LInner:
            result[t + 32] = Warp.ActiveMask();
            if ((t & 1) != 0 && inner < 1)
            {
                inner++;
                goto LInner;
            }

            result[t + 64] = Warp.ActiveMask();
            if ((t & 2) != 0 && outer < 1)
            {
                outer++;
                goto LOuter;
            }

            result[t + 96] = Warp.ActiveMask();
            """);

        Assert.Equal(4, calls.Length);
        Assert.Equal(2, plan.CounterCount);

        Assert.True(plan.TryGetKeys(calls[0], out var headerKeys));
        var outerKey = Assert.Single(headerKeys);
        Assert.Equal(IterationKeyKind.Counter, outerKey.Kind);

        Assert.True(plan.TryGetKeys(calls[1], out var innerKeys));
        Assert.Equal(2, innerKeys.Length);
        Assert.Equal(outerKey.CounterIndex, innerKeys[0].CounterIndex);
        Assert.NotEqual(innerKeys[0].CounterIndex, innerKeys[1].CounterIndex);

        Assert.True(plan.TryGetKeys(calls[2], out var afterInnerKeys));
        var afterInnerKey = Assert.Single(afterInnerKeys);
        Assert.Equal(outerKey.CounterIndex, afterInnerKey.CounterIndex);

        Assert.False(plan.TryGetKeys(calls[3], out _));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_UniformForLoop_DoesNotKeyCall()
    {
        var (plan, _, _, calls) = PlanKernel("""
            for (int i = 0; i < n; i++)
            {
                if (i == 2)
                {
                    result[ThreadIndex.X] = Warp.ActiveMask();
                }
            }
            """);

        Assert.Single(calls);
        Assert.Empty(plan.KeyedLoops);
        Assert.Equal(0, plan.CounterCount);
        Assert.False(plan.TryGetKeys(calls[0], out _));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_ThreadVariantForLoop_KeysCall()
    {
        var (plan, _, _, calls) = PlanKernel("""
            int t = ThreadIndex.X;
            for (int i = 0; i < 4; i++)
            {
                if (((t + i) & 1) == 0)
                {
                    result[t + 32 * i] = Warp.ActiveMask();
                }
            }
            """);

        Assert.Single(calls);
        Assert.True(plan.TryGetKeys(calls[0], out var keys));
        var key = Assert.Single(keys);
        Assert.Equal(IterationKeyKind.LoopVariable, key.Kind);
        Assert.Equal("i", key.LoopVariable?.Name);
        Assert.Equal(0, plan.CounterCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ChainedLabelsOnGotoLoop_AssignsIncrementToInnermostLabel()
    {
        var (plan, _, _, calls) = PlanKernel("""
            int t = ThreadIndex.X;
            int p = 0;
            L0:
            L1:
            result[t] = Warp.ActiveMask();
            if ((t & 1) != 0 && p == 0)
            {
                p++;
                goto L1;
            }
            """);

        Assert.Single(calls);
        var keyed = Assert.Single(plan.KeyedLoops);
        Assert.Equal(IterationKeyKind.Counter, keyed.Kind);
        var incrementLabel = Assert.IsAssignableFrom<LabeledStatementSyntax>(keyed.IncrementLabel);
        Assert.Equal("L1", incrementLabel.Identifier.ValueText);
        var resetLabel = Assert.IsAssignableFrom<LabeledStatementSyntax>(keyed.ResetStatement);
        Assert.Equal("L0", resetLabel.Identifier.ValueText);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_UnrollHintBeforeKeyedLoop_AssignsResetToHintStatement()
    {
        var (plan, _, _, calls) = PlanKernel("""
            int t = ThreadIndex.X;
            int i = t & 1;
            Gpu.Hint.Unroll(2);
            while (i < 4)
            {
                result[t + 32 * i] = Warp.ActiveMask();
                i++;
            }
            """);

        Assert.Single(calls);
        var keyed = Assert.Single(plan.KeyedLoops);
        Assert.Equal(IterationKeyKind.Counter, keyed.Kind);
        Assert.NotNull(keyed.ResetStatement);
        var exprStmt = Assert.IsType<ExpressionStatementSyntax>(keyed.ResetStatement);
        var hintCall = Assert.IsType<InvocationExpressionSyntax>(exprStmt.Expression);
        var hintAccess = Assert.IsType<MemberAccessExpressionSyntax>(hintCall.Expression);
        Assert.Equal("Unroll", hintAccess.Name.Identifier.ValueText);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_IteratorForEachLoop_IsExcluded()
    {
        var source = """
            using System.Collections.Generic;
            using Kassyi.GSharp.Core;

            namespace TestNs;

            public static class TestKernels
            {
                [GpuDeviceFunction]
                private static IEnumerable<int> YieldRange()
                {
                    yield return 0;
                    yield return 1;
                }

                [GpuKernel]
                public static void TestKernel(uint[] result)
                {
                    int t = ThreadIndex.X;
                    foreach (var x in YieldRange())
                    {
                        if ((t & 1) == 0)
                        {
                            result[t + 32 * x] = Warp.ActiveMask();
                        }
                    }
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Last();
        var model = compilation.GetSemanticModel(tree);
        var methodDecl = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static m => m.Identifier.Text == "TestKernel");
        var methodSymbol = Assert.IsAssignableFrom<IMethodSymbol>(model.GetDeclaredSymbol(methodDecl));
        var plan = ActiveMaskIterationPlanner.Plan(methodDecl, model, methodSymbol, isDeviceFunction: false);

        Assert.Empty(plan.KeyedLoops);
        Assert.Equal(0, plan.CounterCount);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("for (int i = 0; i < 4; i++) { if ((t & 1) == 0) result[t] = Warp.ActiveMask(); }")]
    [InlineData("for (int i = 0; i < 4; ++i) { if ((t & 1) == 0) result[t] = Warp.ActiveMask(); }")]
    [InlineData("for (int i = 0; i < 4; i += 1) { if ((t & 1) == 0) result[t] = Warp.ActiveMask(); }")]
    [InlineData("for (int i = 4; i > 0; i--) { if ((t & 1) == 0) result[t] = Warp.ActiveMask(); }")]
    [InlineData("for (int i = 4; i > 0; --i) { if ((t & 1) == 0) result[t] = Warp.ActiveMask(); }")]
    [InlineData("for (int i = 4; i > 0; i -= 1) { if ((t & 1) == 0) result[t] = Warp.ActiveMask(); }")]
    [InlineData("for (uint i = 0u; i < 4u; i++) { if ((t & 1) == 0) result[t] = Warp.ActiveMask(); }")]
    [InlineData("for (uint i = 4u; i > 0u; i--) { if ((t & 1) == 0) result[t] = Warp.ActiveMask(); }")]
    public void Plan_CountedForLoopIntOrUintUnitStep_UsesLoopVariable(string loopCode)
    {
        var (plan, _, _, calls) = PlanKernel($$"""
            int t = ThreadIndex.X;
            {{loopCode}}
            """);

        Assert.Single(calls);
        var keyed = Assert.Single(plan.KeyedLoops);
        Assert.Equal(IterationKeyKind.LoopVariable, keyed.Kind);
        Assert.Equal("i", keyed.LoopVariable?.Name);
        Assert.Equal(0, plan.CounterCount);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("for (int i = 0; i < 8; i += 2) { if ((t & 1) == 0) result[t] = Warp.ActiveMask(); }")]
    [InlineData("for (int i = 0; i <= 4; i++) { if ((t & 1) == 0) result[t] = Warp.ActiveMask(); }")]
    [InlineData("for (int i = 0; i != 4; i++) { if ((t & 1) == 0) result[t] = Warp.ActiveMask(); }")]
    [InlineData("for (long i = 0L; i < 4L; i++) { if ((t & 1) == 0) result[t] = Warp.ActiveMask(); }")]
    [InlineData("for (int i = 0; i < 4L; i++) { if ((t & 1) == 0) result[t] = Warp.ActiveMask(); }")]
    [InlineData("for (int i = 0; i < 4u; i++) { if ((t & 1) == 0) result[t] = Warp.ActiveMask(); }")]
    [InlineData("for (int i = 0, j = 0; i < 4; i++) { if ((t & 1) == 0) result[t + j] = Warp.ActiveMask(); }")]
    [InlineData("int i = 0; for (; i < 4; i++) { if ((t & 1) == 0) result[t] = Warp.ActiveMask(); }")]
    [InlineData("for (int i = 0; i < 4; i++) { if ((t & 1) == 0) { i++; result[t] = Warp.ActiveMask(); } }")]
    [InlineData("for (int i = 0; i < 4; i++) { ref int r = ref i; if ((t & 1) == 0) result[t + r] = Warp.ActiveMask(); }")]
    [InlineData("for (int i = t & 1; i < 4; i++) { result[t + 32 * i] = Warp.ActiveMask(); }")]
    [InlineData("int i = 0; while (i < 4) { if ((t & 1) == 0) result[t] = Warp.ActiveMask(); i++; }")]
    [InlineData("int i = 0; do { if ((t & 1) == 0) result[t] = Warp.ActiveMask(); i++; } while (i < 4);")]
    [InlineData("foreach (var x in result) { if ((t & 1) == 0 && x == 0u) result[t] = Warp.ActiveMask(); }")]
    public void Plan_NonCountedForLoopShapes_UseCounter(string loopCode)
    {
        var (plan, _, _, calls) = PlanKernel($$"""
            int t = ThreadIndex.X;
            {{loopCode}}
            """);

        Assert.Single(calls);
        var keyed = Assert.Single(plan.KeyedLoops);
        Assert.Equal(IterationKeyKind.Counter, keyed.Kind);
        Assert.Equal(1, plan.CounterCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_CountedForLoopWithLambdaWriteCapture_UsesCounter()
    {
        var (plan, _, _, calls) = PlanKernel("""
            int t = ThreadIndex.X;
            for (int i = 0; i < 4; i++)
            {
                System.Action step = () => i++;
                if ((t & 1) == 0)
                {
                    result[t] = Warp.ActiveMask();
                }
                step();
            }
            """);

        Assert.Single(calls);
        var keyed = Assert.Single(plan.KeyedLoops);
        Assert.Equal(IterationKeyKind.Counter, keyed.Kind);
        Assert.Equal(1, plan.CounterCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_GotoLoopCrossingUsingStatement_KeysHeaderAndExcludesPostGateCall()
    {
        var source = """
            using System;
            using Kassyi.GSharp.Core;

            namespace TestNs;

            public struct ScopeGuard : IDisposable
            {
                public void Dispose() { }
            }

            public static class TestKernels
            {
                [GpuKernel]
                public static void TestKernel(uint[] result)
                {
                    int t = ThreadIndex.X;
                    int n = 0;
                    Loop:
                    result[t + 32 * n] = Warp.ActiveMask();
                    using (var gd = new ScopeGuard())
                    {
                        if ((t & 3) != 0 && n++ < 1)
                        {
                            goto Loop;
                        }

                        result[t + 96] = Warp.ActiveMask();
                    }
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Last();
        var model = compilation.GetSemanticModel(tree);
        var methodDecl = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static m => m.Identifier.Text == "TestKernel");
        var methodSymbol = Assert.IsAssignableFrom<IMethodSymbol>(model.GetDeclaredSymbol(methodDecl));
        var plan = ActiveMaskIterationPlanner.Plan(methodDecl, model, methodSymbol, isDeviceFunction: false);
        var calls = methodDecl.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(static inv => inv.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "ActiveMask",
                Expression: IdentifierNameSyntax { Identifier.ValueText: "Warp" }
            })
            .ToImmutableArray();

        Assert.Equal(2, calls.Length);
        Assert.True(plan.TryGetKeys(calls[0], out var headerKeys));
        var key = Assert.Single(headerKeys);
        Assert.Equal(IterationKeyKind.Counter, key.Kind);
        Assert.False(plan.TryGetKeys(calls[1], out _));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_GotoLoopCrossingFixedStatement_KeysHeaderAndExcludesPostGateCall()
    {
        var (plan, _, _, calls) = PlanKernel("""
            int t = ThreadIndex.X;
            int n = 0;
            Loop:
            result[t + 32 * n] = Warp.ActiveMask();
            fixed (uint* p = result)
            {
                if ((t & 3) != 0 && n++ < 1)
                {
                    goto Loop;
                }

                p[t + 96] = Warp.ActiveMask();
            }
            """, "public static unsafe void TestKernel(uint[] result, int n)");

        Assert.Equal(2, calls.Length);
        Assert.True(plan.TryGetKeys(calls[0], out var headerKeys));
        var key = Assert.Single(headerKeys);
        Assert.Equal(IterationKeyKind.Counter, key.Kind);
        Assert.False(plan.TryGetKeys(calls[1], out _));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_CountedForInsideUsingStatement_UsesLoopVariable()
    {
        var source = """
            using System;
            using Kassyi.GSharp.Core;

            namespace TestNs;

            public struct ScopeGuard : IDisposable
            {
                public void Dispose() { }
            }

            public static class TestKernels
            {
                [GpuKernel]
                public static void TestKernel(uint[] result, int n)
                {
                    int t = ThreadIndex.X;
                    using (var gd = new ScopeGuard())
                    {
                        for (int i = 0; i < n; i++)
                        {
                            if (((t + i) & 1) == 0)
                            {
                                result[t + 32 * i] = Warp.ActiveMask();
                            }
                        }
                    }
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Last();
        var model = compilation.GetSemanticModel(tree);
        var methodDecl = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static m => m.Identifier.Text == "TestKernel");
        var methodSymbol = Assert.IsAssignableFrom<IMethodSymbol>(model.GetDeclaredSymbol(methodDecl));
        var plan = ActiveMaskIterationPlanner.Plan(methodDecl, model, methodSymbol, isDeviceFunction: false);
        var calls = methodDecl.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(static inv => inv.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "ActiveMask",
                Expression: IdentifierNameSyntax { Identifier.ValueText: "Warp" }
            })
            .ToImmutableArray();

        Assert.Single(calls);
        Assert.True(plan.TryGetKeys(calls[0], out var keys));
        var key = Assert.Single(keys);
        Assert.Equal(IterationKeyKind.LoopVariable, key.Kind);
        Assert.Equal("i", key.LoopVariable?.Name);
        Assert.Equal(0, plan.CounterCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_SwitchGotoCaseWithInnerFor_KeysInsideForAndPostForCall()
    {
        var (plan, method, _, calls) = PlanKernel("""
            int t = ThreadIndex.X;
            int n = 0;
            switch (0)
            {
                case 0:
                    result[t + 32 * n] = Warp.ActiveMask();
                    for (int i = 0; i < 2; i++)
                    {
                        if ((t & 3) != 0 && n++ < 2)
                        {
                            goto case 0;
                        }

                        result[t + 32 * (3 + i)] = Warp.ActiveMask();
                    }

                    result[t + 160] = Warp.ActiveMask();
                    break;
            }
            """);

        Assert.Equal(3, calls.Length);
        var switchStmt = method.DescendantNodes().OfType<SwitchStatementSyntax>().Single();
        var section = switchStmt.Sections[0];

        Assert.True(plan.TryGetSwitchResets(switchStmt, out var switchResets));
        var sectionCounter = Assert.Single(switchResets);
        Assert.True(plan.TryGetSwitchSectionCounter(section, out var incrementCounter));
        Assert.Equal(sectionCounter, incrementCounter);

        Assert.True(plan.TryGetKeys(calls[0], out var m0Keys));
        var m0Key = Assert.Single(m0Keys);
        Assert.Equal(IterationKeyKind.Counter, m0Key.Kind);
        Assert.Equal(sectionCounter, m0Key.CounterIndex);

        Assert.True(plan.TryGetKeys(calls[1], out var m1Keys));
        Assert.Equal(2, m1Keys.Length);
        Assert.Equal(IterationKeyKind.Counter, m1Keys[0].Kind);
        Assert.Equal(sectionCounter, m1Keys[0].CounterIndex);
        Assert.Equal(IterationKeyKind.LoopVariable, m1Keys[1].Kind);
        Assert.Equal("i", m1Keys[1].LoopVariable?.Name);

        Assert.True(plan.TryGetKeys(calls[2], out var m2Keys));
        var m2Key = Assert.Single(m2Keys);
        Assert.Equal(IterationKeyKind.Counter, m2Key.Kind);
        Assert.Equal(sectionCounter, m2Key.CounterIndex);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_SwitchGotoCaseWithElseBranch_KeysElseCall()
    {
        var (plan, _, _, calls) = PlanKernel("""
            int t = ThreadIndex.X;
            int k = 0;
            switch (0)
            {
                case 0:
                    result[t] = Warp.ActiveMask();
                    if ((t & 1) == 0 && k++ < 1)
                    {
                        goto case 0;
                    }
                    else
                    {
                        result[t + 32] = Warp.ActiveMask();
                    }

                    break;
            }
            """);

        Assert.Equal(2, calls.Length);
        Assert.True(plan.TryGetKeys(calls[0], out var inCycleKeys));
        var inCycleKey = Assert.Single(inCycleKeys);
        Assert.True(plan.TryGetKeys(calls[1], out var elseKeys));
        var elseKey = Assert.Single(elseKeys);
        Assert.Equal(IterationKeyKind.Counter, elseKey.Kind);
        Assert.Equal(inCycleKey.CounterIndex, elseKey.CounterIndex);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_SwitchMultiLabelGotoCase_KeysSectionAndResetsAtSwitch()
    {
        var (plan, method, _, calls) = PlanKernel("""
            int t = ThreadIndex.X;
            int k = 0;
            switch (n)
            {
                case 0:
                case 1:
                    result[t + 32 * k] = Warp.ActiveMask();
                    if ((t & 1) == 0 && k++ < 1)
                    {
                        goto case 1;
                    }

                    break;
            }
            """);

        Assert.Single(calls);
        var switchStmt = method.DescendantNodes().OfType<SwitchStatementSyntax>().Single();
        Assert.True(plan.TryGetSwitchResets(switchStmt, out var resets));
        var counter = Assert.Single(resets);
        Assert.True(plan.TryGetSwitchSectionCounter(switchStmt.Sections[0], out var inc));
        Assert.Equal(counter, inc);
        Assert.True(plan.TryGetKeys(calls[0], out var keys));
        var key = Assert.Single(keys);
        Assert.Equal(counter, key.CounterIndex);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_SwitchGotoCaseCrossingUsingAndFixed_KeysEntireSection()
    {
        var source = """
            using System;
            using Kassyi.GSharp.Core;

            namespace TestNs;

            public struct SwitchScopeGuard : IDisposable
            {
                public void Dispose() { }
            }

            public static class TestKernels
            {
                [GpuKernel]
                public static unsafe void TestKernel(uint[] result, int u)
                {
                    int t = ThreadIndex.X;
                    int k = 0;
                    switch (u)
                    {
                        case 0:
                            using (var gd = new SwitchScopeGuard())
                            {
                                result[t] = Warp.ActiveMask();
                                if ((t & 1) == 0 && k++ < 1)
                                {
                                    goto case 0;
                                }

                                result[t + 32] = Warp.ActiveMask();
                            }

                            result[t + 64] = Warp.ActiveMask();
                            break;
                        case 1:
                            fixed (uint* p = result)
                            {
                                p[t] = Warp.ActiveMask();
                                if ((t & 2) == 0 && k++ < 1)
                                {
                                    goto case 1;
                                }

                                p[t + 32] = Warp.ActiveMask();
                            }

                            break;
                    }
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Last();
        var model = compilation.GetSemanticModel(tree);
        var methodDecl = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static m => m.Identifier.Text == "TestKernel");
        var methodSymbol = Assert.IsAssignableFrom<IMethodSymbol>(model.GetDeclaredSymbol(methodDecl));
        var plan = ActiveMaskIterationPlanner.Plan(methodDecl, model, methodSymbol, isDeviceFunction: false);
        var calls = methodDecl.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(static inv => inv.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "ActiveMask",
                Expression: IdentifierNameSyntax { Identifier.ValueText: "Warp" }
            })
            .ToImmutableArray();

        Assert.Equal(5, calls.Length);
        Assert.True(plan.TryGetKeys(calls[0], out var c0Head));
        Assert.True(plan.TryGetKeys(calls[1], out var c0InUsing));
        Assert.True(plan.TryGetKeys(calls[2], out var c0AfterUsing));
        Assert.Equal(Assert.Single(c0Head).CounterIndex, Assert.Single(c0InUsing).CounterIndex);
        Assert.Equal(Assert.Single(c0Head).CounterIndex, Assert.Single(c0AfterUsing).CounterIndex);
        Assert.True(plan.TryGetKeys(calls[3], out var c1InFixed));
        Assert.True(plan.TryGetKeys(calls[4], out var c1PostGoto));
        Assert.Equal(Assert.Single(c1InFixed).CounterIndex, Assert.Single(c1PostGoto).CounterIndex);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_SwitchGotoCaseWithInnerCountedForWithContinueAtHead_KeepsSectionKeyAfterFor()
    {
        var (plan, method, _, calls) = PlanKernel("""
            int t = ThreadIndex.X;
            int n = 0;
            switch (0)
            {
                case 0:
                    for (int i = 0; i < 2; i++)
                    {
                        if ((t & 1) == 0)
                        {
                            continue;
                        }

                        result[t + 32 * i] = Warp.ActiveMask();
                    }

                    result[t + 64] = Warp.ActiveMask();
                    if ((t & 1) == 0 && n++ < 1)
                    {
                        goto case 0;
                    }

                    break;
            }
            """);

        Assert.Equal(2, calls.Length);
        var switchStmt = method.DescendantNodes().OfType<SwitchStatementSyntax>().Single();
        Assert.True(plan.TryGetSwitchResets(switchStmt, out var switchResets));
        var sectionCounter = Assert.Single(switchResets);

        Assert.True(plan.TryGetKeys(calls[1], out var postForKeys));
        var postForKey = Assert.Single(postForKeys);
        Assert.Equal(IterationKeyKind.Counter, postForKey.Kind);
        Assert.Equal(sectionCounter, postForKey.CounterIndex);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_UniformSwitchMultiSectionCycle_KeysDependentSectionUsingSccUniformity()
    {
        var (plan, method, _, calls) = PlanKernel("""
            int t = ThreadIndex.X;
            int k = 0;
            switch (n)
            {
                case 0:
                    if ((t & 1) == 0 && k++ < 1)
                    {
                        goto case 1;
                    }

                    break;
                case 1:
                    result[t + 32 * k] = Warp.ActiveMask();
                    goto case 0;
            }
            """);

        Assert.Single(calls);
        var switchStmt = method.DescendantNodes().OfType<SwitchStatementSyntax>().Single();
        Assert.Equal(1, plan.CounterCount);
        Assert.False(plan.TryGetSwitchSectionCounter(switchStmt.Sections[0], out _));
        Assert.True(plan.TryGetSwitchSectionCounter(switchStmt.Sections[1], out var counter));
        Assert.True(plan.TryGetKeys(calls[0], out var keys));
        var key = Assert.Single(keys);
        Assert.Equal(counter, key.CounterIndex);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_UniformSwitchCrossSectionGotoLabel_AttachesIncrementToTargetLabel()
    {
        var (plan, method, _, calls) = PlanKernel("""
            int t = ThreadIndex.X;
            int k = 0;
            switch (n)
            {
                case 0:
                    L0:
                    result[t + 32 * k] = Warp.ActiveMask();
                    if ((t & 1) == 0 && k++ < 1)
                    {
                        goto case 1;
                    }

                    break;
                case 1:
                    result[t + 64] = Warp.ActiveMask();
                    goto L0;
            }
            """);

        Assert.Equal(2, calls.Length);
        var switchStmt = method.DescendantNodes().OfType<SwitchStatementSyntax>().Single();
        var l0 = method.DescendantNodes().OfType<LabeledStatementSyntax>()
            .Single(static l => l.Identifier.ValueText == "L0");
        Assert.False(plan.TryGetSwitchSectionCounter(switchStmt.Sections[0], out _));
        Assert.True(plan.TryGetLabelCounters(l0, out var l0Counters));
        Assert.Single(l0Counters);
        Assert.True(plan.TryGetSwitchSectionCounter(switchStmt.Sections[1], out _));
        Assert.True(plan.TryGetKeys(calls[0], out _));
        Assert.True(plan.TryGetKeys(calls[1], out _));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ConditionalAccessInUniformFor_KeysByLoopVariable()
    {
        var source = """
            #nullable enable
            using Kassyi.GSharp.Core;

            namespace TestNs;

            public static class TestKernels
            {
                [GpuKernel]
                public static void TestKernel(uint[] result)
                {
                    int t = ThreadIndex.X;
                    uint[]? row = (t & 1) == 0 ? result : null;
                    for (int i = 0; i < 4; i++)
                    {
                        row?[t + 32 * i] = Warp.ActiveMask();
                    }
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Last();
        var model = compilation.GetSemanticModel(tree);
        var methodDecl = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static m => m.Identifier.Text == "TestKernel");
        var methodSymbol = Assert.IsAssignableFrom<IMethodSymbol>(model.GetDeclaredSymbol(methodDecl));
        var plan = ActiveMaskIterationPlanner.Plan(methodDecl, model, methodSymbol, isDeviceFunction: false);
        var calls = methodDecl.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(static inv => inv.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "ActiveMask",
                Expression: IdentifierNameSyntax { Identifier.ValueText: "Warp" }
            })
            .ToImmutableArray();

        Assert.Single(calls);
        Assert.True(plan.TryGetKeys(calls[0], out var keys));
        var key = Assert.Single(keys);
        Assert.Equal(IterationKeyKind.LoopVariable, key.Kind);
        Assert.Equal("i", key.LoopVariable?.Name);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_NonInvocationBodyCallsInUniformFor_KeysByLoopVariableAndSkipsBclProperties()
    {
        var source = """
            using Kassyi.GSharp.Core;

            namespace TestNs;

            public struct DivergentFlag
            {
                public int Lane;

                public bool IsEven => (Lane & 1) == 0;

                public static bool operator !(DivergentFlag value) => (value.Lane & 1) != 0;
            }

            public static class TestKernels
            {
                [GpuKernel]
                public static void TestKernel(uint[] result, int n)
                {
                    int t = ThreadIndex.X;
                    var flag = new DivergentFlag { Lane = t };
                    for (int i = 0; i < n; i++)
                    {
                        if (flag.IsEven)
                        {
                            result[t + 32 * i] = Warp.ActiveMask();
                        }
                    }

                    for (int j = 0; j < n; j++)
                    {
                        if (!flag)
                        {
                            result[t + 32 * j] = Warp.ActiveMask();
                        }
                    }

                    int? uniformNullable = n > 0 ? 1 : null;
                    for (int k = 0; k < n; k++)
                    {
                        if (uniformNullable.HasValue)
                        {
                            result[t + 32 * k] = Warp.ActiveMask();
                        }
                    }
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Last();
        var model = compilation.GetSemanticModel(tree);
        var methodDecl = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static m => m.Identifier.Text == "TestKernel");
        var methodSymbol = Assert.IsAssignableFrom<IMethodSymbol>(model.GetDeclaredSymbol(methodDecl));
        var plan = ActiveMaskIterationPlanner.Plan(methodDecl, model, methodSymbol, isDeviceFunction: false);
        var calls = methodDecl.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(static inv => inv.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "ActiveMask",
                Expression: IdentifierNameSyntax { Identifier.ValueText: "Warp" }
            })
            .ToImmutableArray();

        Assert.Equal(3, calls.Length);
        Assert.True(plan.TryGetKeys(calls[0], out var propKeys));
        Assert.Equal("i", Assert.Single(propKeys).LoopVariable?.Name);
        Assert.True(plan.TryGetKeys(calls[1], out var opKeys));
        Assert.Equal("j", Assert.Single(opKeys).LoopVariable?.Name);
        Assert.False(plan.TryGetKeys(calls[2], out _));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_GotoLoopWithForwardEntry_PlacesResetBeforeEarliestForwardJump()
    {
        var (plan, method, _, calls) = PlanKernel("""
            int t = ThreadIndex.X;
            for (int outer = 0; outer < 2; outer++)
            {
                int p = 0;
                if (outer == 1 && (t & 1) == 0)
                {
                    goto L1;
                }

            L0:
            L1:
                result[t + 32 * (2 * outer + p)] = Warp.ActiveMask();
                if ((t & 3) != 0 && p++ < 1)
                {
                    goto L0;
                }
            }
            """);

        Assert.Single(calls);
        var innerIf = method.DescendantNodes().OfType<IfStatementSyntax>().First();
        Assert.True(plan.TryGetResetCounters(innerIf, out var counters));
        Assert.Single(counters);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ChainedAndMiddleForwardEntryIntoGotoLoop_HoistsResetToEarliestJump()
    {
        var (plan, method, _, calls) = PlanKernel("""
            int t = ThreadIndex.X;
            int p = 0;
            if ((t & 1) == 0)
            {
                goto La;
            }

            La:
            if ((t & 2) == 0)
            {
                goto Lmid;
            }

            L0:
            result[t] = Warp.ActiveMask();
            Lmid:
            if ((t & 3) != 0 && p++ < 1)
            {
                goto L0;
            }
            """);

        Assert.Single(calls);
        var firstIf = method.DescendantNodes().OfType<IfStatementSyntax>().First();
        Assert.True(plan.TryGetResetCounters(firstIf, out var counters));
        Assert.Single(counters);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_EnclosingGotoLoopWithForwardEntry_UsesPostLabelAndDualResets()
    {
        var (plan, method, _, calls) = PlanKernel("""
            int t = ThreadIndex.X;
            int outer = 0;
            int inner = 0;
            if ((t & 4) == 0)
            {
                goto LInner;
            }

            LOuter:
            if ((t & 8) == 0)
            {
                goto LInner;
            }

            LInner:
            result[t] = Warp.ActiveMask();
            if ((t & 1) != 0 && inner++ < 1)
            {
                goto LInner;
            }

            if ((t & 2) != 0 && outer++ < 1)
            {
                goto LOuter;
            }
            """);

        Assert.Single(calls);
        var ifs = method.DescendantNodes().OfType<IfStatementSyntax>().ToArray();
        var lOuter = method.DescendantNodes().OfType<LabeledStatementSyntax>()
            .Single(static l => l.Identifier.ValueText == "LOuter");

        Assert.True(plan.TryGetKeys(calls[0], out var keys));
        Assert.Equal(2, keys.Length);
        var innerCounter = keys[1].CounterIndex;

        Assert.True(plan.TryGetResetCounters(ifs[0], out var outerIfResets));
        Assert.Contains(innerCounter, outerIfResets);
        Assert.True(plan.TryGetPostLabelResetCounters(lOuter, out var postLabelResets));
        Assert.Contains(innerCounter, postLabelResets);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_TargetedLabeledStructuredLoopWithoutHint_EmitsPostLabelReset()
    {
        var (plan, method, _, calls) = PlanKernel("""
            int t = ThreadIndex.X;
            int outer = 0;
            L0:
            int i = t & 1;
            L1:
            while (i < 4)
            {
                result[t + 32 * i] = Warp.ActiveMask();
                i++;
            }

            if (outer++ < 1)
            {
                goto L1;
            }

            if ((t & 2) != 0 && outer < 3)
            {
                goto L0;
            }
            """);

        Assert.Single(calls);
        var l1 = method.DescendantNodes().OfType<LabeledStatementSyntax>()
            .Single(static l => l.Identifier.ValueText == "L1");
        Assert.True(plan.TryGetPostLabelResetCounters(l1, out var postResets));
        Assert.Single(postResets);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ConditionalAccessBindReceiverClonedNode_TryGetKeysFindsKeysByOriginalSpan()
    {
        var source = """
            #nullable enable
            using Kassyi.GSharp.Core;

            namespace TestNs;

            public struct MaskBox
            {
                public uint[]? Row;
                public uint Last;

                public void Record(uint mask)
                {
                    Last = mask;
                }
            }

            public static class TestKernels
            {
                [GpuKernel]
                public static void TestKernel(uint[] result)
                {
                    int t = ThreadIndex.X;
                    uint[]? row = (t & 1) == 0 ? result : null;
                    MaskBox? box = (t & 1) == 0 ? new MaskBox { Row = result } : null;
                    for (int i = 0; i < 2; i++)
                    {
                        row?[t + 32 * i] = Warp.ActiveMask();
                        box?.Row?[t + 32 * i] = Warp.ActiveMask();
                        box?.Record(Warp.ActiveMask());
                    }
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        Assert.Empty(compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
        var tree = compilation.SyntaxTrees.Last();
        var model = compilation.GetSemanticModel(tree);
        var methodDecl = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static m => m.Identifier.Text == "TestKernel");
        var methodSymbol = Assert.IsAssignableFrom<IMethodSymbol>(model.GetDeclaredSymbol(methodDecl));
        var plan = ActiveMaskIterationPlanner.Plan(methodDecl, model, methodSymbol, isDeviceFunction: false);

        var accesses = methodDecl.DescendantNodes()
            .OfType<ConditionalAccessExpressionSyntax>()
            .Where(static c => c.Parent is not ConditionalAccessExpressionSyntax &&
                               c.Parent is not MemberBindingExpressionSyntax &&
                               c.Parent is not ElementAccessExpressionSyntax &&
                               c.Parent is not AssignmentExpressionSyntax)
            .ToArray();
        Assert.Equal(3, accesses.Length);

        var receiver = SyntaxFactory.IdentifierName("__gsharp_cond_receiver");

        // 1. row?[t + 32 * i] = Warp.ActiveMask()
        var boundSimple = ConditionalAccess.BindReceiver(accesses[0].WhenNotNull, receiver, compilation);
        var simpleCall = boundSimple.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        Assert.True(plan.TryGetKeys(simpleCall, out var simpleKeys));
        Assert.Equal("i", Assert.Single(simpleKeys).LoopVariable?.Name);

        // 2. boxes[t]?.Row?[t + 32 * i] = Warp.ActiveMask() (two-stage BindReceiver as in ConditionalAccessExpressionTranspilerRule)
        var boundOuter = ConditionalAccess.BindReceiver(accesses[1].WhenNotNull, receiver, compilation);
        var innerAccess = boundOuter.DescendantNodesAndSelf().OfType<ConditionalAccessExpressionSyntax>().Single();
        var boundInner = ConditionalAccess.BindReceiver(innerAccess.WhenNotNull, receiver, compilation);
        var chainedCall = boundInner.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        Assert.True(plan.TryGetKeys(chainedCall, out var chainedKeys));
        Assert.Equal("i", Assert.Single(chainedKeys).LoopVariable?.Name);

        // 3. boxes[t]?.Record(Warp.ActiveMask())
        var boundInvoke = ConditionalAccess.BindReceiver(accesses[2].WhenNotNull, receiver, compilation);
        var argCall = boundInvoke.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(static inv => inv.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "ActiveMask",
                Expression: IdentifierNameSyntax { Identifier.ValueText: "Warp" }
            });
        Assert.True(plan.TryGetKeys(argCall, out var argKeys));
        Assert.Equal("i", Assert.Single(argKeys).LoopVariable?.Name);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_UsingAndFixedBodyCallsInUniformFor_KeysOnlyEmittedUserMethods()
    {
        var source = """
            using System;
            using Kassyi.GSharp.Core;

            namespace TestNs;

            public struct ScopeGuard : IDisposable
            {
                public int Lane;

                public void Dispose()
                {
                }
            }

            public struct PinnableBuffer
            {
                public uint Value;

                public ref uint GetPinnableReference() => ref Value;
            }

            public static class TestKernels
            {
                [GpuKernel]
                public static unsafe void TestKernel(uint[] result, int n)
                {
                    int t = ThreadIndex.X;
                    for (int i = 0; i < n; i++)
                    {
                        using var a = new ScopeGuard { Lane = t };
                        result[t + 32 * i] = Warp.ActiveMask();
                    }

                    for (int j = 0; j < n; j++)
                    {
                        using (new ScopeGuard { Lane = t })
                        {
                            result[t + 32 * j] = Warp.ActiveMask();
                        }
                    }

                    var buf = new PinnableBuffer { Value = (uint)t };
                    for (int k = 0; k < n; k++)
                    {
                        fixed (uint* p = buf)
                        {
                            result[t + 32 * k] = Warp.ActiveMask();
                        }
                    }

                    for (int m = 0; m < n; m++)
                    {
                        fixed (uint* p = result)
                        {
                            p[t + 32 * m] = Warp.ActiveMask();
                        }
                    }
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Last();
        var model = compilation.GetSemanticModel(tree);
        var methodDecl = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static m => m.Identifier.Text == "TestKernel");
        var methodSymbol = Assert.IsAssignableFrom<IMethodSymbol>(model.GetDeclaredSymbol(methodDecl));
        var plan = ActiveMaskIterationPlanner.Plan(methodDecl, model, methodSymbol, isDeviceFunction: false);
        var calls = methodDecl.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(static inv => inv.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "ActiveMask",
                Expression: IdentifierNameSyntax { Identifier.ValueText: "Warp" }
            })
            .ToImmutableArray();

        Assert.Equal(4, calls.Length);
        Assert.True(plan.TryGetKeys(calls[0], out var usingDeclKeys));
        Assert.Equal("i", Assert.Single(usingDeclKeys).LoopVariable?.Name);
        Assert.True(plan.TryGetKeys(calls[1], out var usingStmtKeys));
        Assert.Equal("j", Assert.Single(usingStmtKeys).LoopVariable?.Name);
        Assert.True(plan.TryGetKeys(calls[2], out var fixedStructKeys));
        Assert.Equal("k", Assert.Single(fixedStructKeys).LoopVariable?.Name);
        Assert.False(plan.TryGetKeys(calls[3], out _));
    }
}
