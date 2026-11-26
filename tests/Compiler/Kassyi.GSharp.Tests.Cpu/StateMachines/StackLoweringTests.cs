#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VerifyXunit;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// Tier 2 tests for <see cref="AstLoweringPipeline.LowerForStack"/>: the body that the CPU stack models run for a
/// method that reaches <c>Warp.ActiveMask()</c> has the reconvergence regions of the state machine, in the same places,
/// every labeled jump names a target in it, and it has nothing of the state machine.
/// </summary>
[Category(TestCategory.Compiler)]
public class StackLoweringTests
{
    public static TheoryData<string[]> ActiveMaskSources() =>
        new()
        {
            new[] { "Differential", "WarpActiveMaskDifferentialKernels.cu.cs" },
            new[] { "Differential", "LabeledJumpDifferentialKernels.cu.cs" },
            new[] { "TestCases", "StateMachines", "ReconvergenceCases.cs" }
        };

    // Every kernel and suspending device function: the same region calls (kind, region, place) in the stack body as in
    // the state machine, as many times each
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(ActiveMaskSources))]
    public void LowerForStack_ActiveMaskKernels_PlaceTheRegionsOfTheStateMachine(string[] source)
    {
        var plans = CreatePlans(source);
        var mismatches = new List<string>();
        var regionCalls = 0;
        foreach (var (name, plan) in plans)
        {
            var lowered = plan.Lowered ?? throw new InvalidOperationException($"{name} has no state machine.");
            var stack = plan.StackBody ?? throw new InvalidOperationException($"{name} has no stack body.");
            var expected = RegionCalls(lowered.LoweredBody, stackForm: false);
            var actual = RegionCalls(stack.Body, stackForm: true);
            regionCalls += actual.Length;
            if (!expected.SequenceEqual(actual))
            {
                mismatches.Add(
                    $"{name}: state machine [{string.Join("; ", expected)}], stack [{string.Join("; ", actual)}]");
            }
        }

        Assert.Empty(mismatches);
        Assert.NotEqual(0, regionCalls);
    }

    // Every labeled break or continue of a stack body names a loop or switch around it, and every goto a label of the
    // body. LabeledStatementSplitter detaches the label of a target that holds a split point (L: s becomes L: ; s), so
    // without LabeledJumpRewriter a jump to it would name no statement (a labeled break out of a switch with
    // Warp.ActiveMask(), a labeled continue around a barrier)
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ActiveMaskSources))]
    public void LowerForStack_LabeledJumps_NameATargetInTheBody(string[] source)
    {
        var unresolved = new List<string>();
        var labeledJumps = 0;
        var gotos = 0;
        foreach (var (name, plan) in CreatePlans(source))
        {
            var stack = plan.StackBody ?? throw new InvalidOperationException($"{name} has no stack body.");
            var labels = stack.Body
                .DescendantNodes()
                .OfType<LabeledStatementSyntax>()
                .Select(static labeled => labeled.Identifier.ValueText)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var jump in stack.Body.DescendantNodes().OfType<StatementSyntax>())
            {
                if (jump is BreakStatementSyntax or ContinueStatementSyntax &&
                    LabeledJumpSyntax.GetLabelName(jump) is { } label)
                {
                    labeledJumps++;
                    if (LabeledJumpSyntax.FindTarget(jump) == null)
                    {
                        unresolved.Add($"{name}: {label.Identifier.ValueText}");
                    }
                }
                else if (jump is GotoStatementSyntax
                         {
                             RawKind: (int)SyntaxKind.GotoStatement,
                             Expression: IdentifierNameSyntax { Identifier.ValueText: var target }
                         })
                {
                    gotos++;
                    if (!labels.Contains(target))
                    {
                        unresolved.Add($"{name}: goto {target}");
                    }
                }
            }
        }

        Assert.Empty(unresolved);
        Assert.NotEqual(0, labeledJumps + gotos);
    }

    // No state, no context, no root, and none of the calls of the state machine's regions
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(ActiveMaskSources))]
    public void LowerForStack_Body_HasNoStateMachine(string[] source)
    {
        string[] stateMachineNames =
        [
            GSharpInternalNames.StateMachine.State,
            GSharpInternalNames.StateMachine.Context,
            GSharpInternalNames.SuspendingCall.Root,
            GSharpInternalNames.Prelude.WarpRegionEnter,
            GSharpInternalNames.Prelude.WarpRegionLeave,
            GSharpInternalNames.Prelude.WarpReconvergeArrive,
            GSharpInternalNames.Prelude.WarpReconvergePass
        ];
        foreach (var (name, plan) in CreatePlans(source))
        {
            var stack = plan.StackBody ?? throw new InvalidOperationException($"{name} has no stack body.");

            var found = stack.Body
                .DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Select(static identifier => identifier.Identifier.ValueText)
                .Where(stateMachineNames.Contains)
                .Distinct()
                .ToArray();

            Assert.True(found.Length == 0, $"{name}: {string.Join(", ", found)}");
            Assert.Empty(stack.Body.DescendantNodes().OfType<SwitchStatementSyntax>()
                .Where(static s => s.Expression is IdentifierNameSyntax { Identifier.ValueText: var v } &&
                                   v == GSharpInternalNames.StateMachine.State));
            var highestRegion = RegionCalls(stack.Body, stackForm: true)
                .Select(static call => call.Region)
                .DefaultIfEmpty(-1)
                .Max();
            Assert.Equal(highestRegion + 1, stack.RegionCount);
        }
    }

    // The right operand of && is a branch only once it is a statement: the odd lanes enter a region before it and meet
    // the others where it ends, before Mark(20). The split pass of the IR writes it as an if; without it (a statement
    // the IR leaves to the syntax passes), the first round of ConditionalEvaluationRewriter does
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(true)]
    [InlineData(false)]
    public void LowerForStack_ActiveMaskInAndAlso_ReconvergesBeforeTheNextStatement(bool withIrSplitLowering)
    {
        StackLoweredBody stack;
        using (withIrSplitLowering ? null : AstLoweringPipeline.WithoutIrSplitLowering())
        {
            var (name, plan) = CreatePlans(["TestCases", "StateMachines", "ReconvergenceCases.cs"])
                .Single(static entry => entry.Name == "ActiveMaskInAndAlsoKernel");
            stack = plan.StackBody ?? throw new InvalidOperationException($"{name} has no stack body.");
        }

        var calls = stack.Body
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(static call => call.Expression is IdentifierNameSyntax { Identifier.ValueText: var callee }
                ? callee
                : null)
            .Where(static callee => callee is "Mark" ||
                                    callee == GSharpInternalNames.StackWarp.RegionEnter ||
                                    callee == GSharpInternalNames.StackWarp.Reconverge)
            .ToArray();

        Assert.Equal(
            [GSharpInternalNames.StackWarp.RegionEnter, GSharpInternalNames.StackWarp.Reconverge, "Mark"],
            calls);
        Assert.Equal(1, stack.RegionCount);
    }

    // The stack body is lowered once and shared by the copies of the plan
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void StackBody_ReadThroughCopiesOfThePlan_IsLoweredOnce()
    {
        var (_, plan) = CreatePlans(["TestCases", "StateMachines", "ReconvergenceCases.cs"])
            .Single(static entry => entry.Name == "IfKernel");
        var copy = plan;

        Assert.Same(plan.StackBody, copy.StackBody);
    }

    // The native stack eligibility lowers the body first; a new plan of the same method writes that same body
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void StackBody_AfterTheNativeStackEligibility_IsLoweredOnce()
    {
        var (_, plan) = CreatePlans(["TestCases", "StateMachines", "ReconvergenceCases.cs"])
            .Single(static entry => entry.Name == "IfKernel");
        var symbol = plan.Symbol ?? throw new InvalidOperationException("IfKernel has no symbol.");

        NativeStackEligibility.Classify(symbol, plan.Declaration, plan.SemanticModel);
        var eligibilityBody = StackBodies.Get(plan.Declaration, plan.SemanticModel, symbol, isDeviceFunction: false);
        var newPlan = MethodEmissionPlan.Create(plan.Declaration, plan.SemanticModel, isKernel: true);

        Assert.Same(eligibilityBody.Body, newPlan.StackBody);
        Assert.NotNull(eligibilityBody.Body);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task LowerForStack_UsingDeclarationAfterContinue_PlacesContinueTargetAfterUsing() =>
        VerifyStackLower(["Differential", "StackScopeKernels.cu.cs"], "ContinueBeforeUsingDeclarationKernel");

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task LowerForStack_TwoUsingDeclarationsInBlock_NestsInnerInsideOuter() =>
        VerifyStackLower(["Differential", "StackScopeKernels.cu.cs"], "TwoUsingDeclarationsWithBreakAndContinueKernel");

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task LowerForStack_LabeledUsingDeclaration_KeepsLabelOnUsingStatement() =>
        VerifyStackLower(["TestCases", "StateMachines", "StackScopeLoweringCases.cs"], "LabeledUsingDeclarationKernel");

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task LowerForStack_FixedInLoopWithContinue_LeavesRegionInsideFixed() =>
        VerifyStackLower(["Differential", "StackScopeKernels.cu.cs"], "ContinueInsideFixedInLoopKernel");

    private static Task VerifyStackLower(string[] source, string kernelName, [CallerMemberName] string methodName = "")
    {
        var (_, plan) = CreatePlans(source).Single(entry => entry.Name == kernelName);
        var stack = plan.StackBody ?? throw new InvalidOperationException($"{kernelName} has no stack body.");
#pragma warning disable RS0030 // The .verified.cs snapshot is the text of the lowered tree
        var formatted = stack.Body.NormalizeWhitespace().ToFullString();
#pragma warning restore RS0030
        var snapshot = $$"""
            public class {{methodName}}_Snapshot
            {
                public void LoweredMethod()
            {{formatted}}
            }
            """;
        return Verifier.Verify(snapshot, "cs")
            .UseFastHashSnapshotName(methodName);
    }

    // The plans of the kernels and the device functions that a state machine lowers, by name, with their stack bodies
    private static (string Name, MethodEmissionPlan Plan)[] CreatePlans(string[] source)
    {
        var path = RepositoryPaths.Combine(["tests", "Compiler", "Kassyi.GSharp.Tests.Shared", .. source]);
        var (compilation, tree) = RoslynTestUtility.GetCallerSourceContext(path);
        var model = compilation.GetSemanticModel(tree);
        return tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(static method => method.HasBody())
            .Select(method =>
            {
                var symbol = model.GetDeclaredSymbol<IMethodSymbol>(method);
                var isKernel = symbol != null && symbol.HasAttribute(KnownAttributes.GpuKernel);
                return (Name: method.Identifier.ValueText,
                    Plan: MethodEmissionPlan.Create(method, model, isKernel));
            })
            .Where(static entry => entry.Plan.Kind is MethodEmissionKind.Kernel or
                                       MethodEmissionKind.SuspendingDeviceFunction &&
                                   entry.Plan.StackBody != null)
            .ToArray();
    }

    // The region calls of a body, sorted: enter and leave with their region, the end of a region with its place
    private static RegionCall[] RegionCalls(BlockSyntax body, bool stackForm)
    {
        var (enter, leave, end) = stackForm
            ? (GSharpInternalNames.StackWarp.RegionEnter, GSharpInternalNames.StackWarp.RegionLeave,
                GSharpInternalNames.StackWarp.Reconverge)
            : (GSharpInternalNames.Prelude.WarpRegionEnter, GSharpInternalNames.Prelude.WarpRegionLeave,
                GSharpInternalNames.Prelude.WarpReconvergeArrive);
        var siteIndex = stackForm ? 1 : 2;
        return body.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(call =>
            {
                var callee = call.Expression is IdentifierNameSyntax { Identifier.ValueText: var text } ? text : null;
                var kind = callee == enter ? "enter" : callee == leave ? "leave" : callee == end ? "reconverge" : null;
                if (kind == null)
                {
                    return null;
                }

                var arguments = call.ArgumentList.Arguments;
                Assert.Equal(kind == "reconverge" ? siteIndex + 2 : siteIndex + 1, arguments.Count);
                var region = arguments[siteIndex].Expression is LiteralExpressionSyntax { Token.Value: int site }
                    ? site
                    : throw new InvalidOperationException($"The region of a call to {callee} is not a literal.");
                var place = kind == "reconverge" &&
                            arguments[siteIndex + 1].Expression is LiteralExpressionSyntax { Token.ValueText: var p }
                    ? p
                    : "";
                return new RegionCall(kind, region, place);
            })
            .OfType<RegionCall>()
            .OrderBy(static call => call.Region)
            .ThenBy(static call => call.Kind, StringComparer.Ordinal)
            .ThenBy(static call => call.Place, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed record RegionCall(string Kind, int Region, string Place)
    {
        public override string ToString() => $"{Kind} {Region} {Place}";
    }
}
