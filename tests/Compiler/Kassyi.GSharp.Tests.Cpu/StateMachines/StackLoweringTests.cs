#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// Tier 2 tests for <see cref="AstLoweringPipeline.LowerForStack"/>: the body that the CPU stack models run for a method
/// that reaches <c>Warp.ActiveMask()</c> has the reconvergence regions of the state machine, in the same places, and
/// nothing of the state machine.
/// </summary>
[Category(TestCategory.Compiler)]
public class StackLoweringTests
{
    public static TheoryData<string[]> ActiveMaskSources() =>
        new()
        {
            new[] { "Differential", "WarpActiveMaskDifferentialKernels.cu.cs" },
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
                mismatches.Add($"{name}: state machine [{string.Join("; ", expected)}], stack [{string.Join("; ", actual)}]");
            }
        }

        Assert.Empty(mismatches);
        Assert.NotEqual(0, regionCalls);
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
    // the others where it ends, before Mark(20). The split pass of the IR writes it as an if; without it (a statement the
    // IR leaves to the syntax passes), the first round of ConditionalEvaluationRewriter does
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

    // The plans of the kernels and the device functions that a state machine lowers, by name
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
                                   entry.Plan.Lowered != null)
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
