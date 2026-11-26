#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
public partial class StackScopeTests
{
    private static partial bool RunsOnAStack(DiffPath path);

    static partial void AssertManagedStackLaunch(DiffPathRunner path, Action launch);

    private static readonly Dictionary<string, Action<int[], uint[], uint, uint>> s_t1Launchers = new()
    {
        [nameof(StackScopeKernels.UsingDeclarationMaskKernel)] = static (d, r, g, b) =>
            StackScopeKernels.LaunchUsingDeclarationMaskKernel(d, r, gridDimX: g, blockDimX: b),
        [nameof(StackScopeKernels.ContinueBeforeUsingDeclarationKernel)] = static (d, r, g, b) =>
            StackScopeKernels.LaunchContinueBeforeUsingDeclarationKernel(d, r, gridDimX: g, blockDimX: b),
        [nameof(StackScopeKernels.ContinueInsideUsingStatementKernel)] = static (d, r, g, b) =>
            StackScopeKernels.LaunchContinueInsideUsingStatementKernel(d, r, gridDimX: g, blockDimX: b),
        [nameof(StackScopeKernels.BranchInsideUsingStatementKernel)] = static (d, r, g, b) =>
            StackScopeKernels.LaunchBranchInsideUsingStatementKernel(d, r, gridDimX: g, blockDimX: b),
        [nameof(StackScopeKernels.UsingDeclarationInBranchKernel)] = static (d, r, g, b) =>
            StackScopeKernels.LaunchUsingDeclarationInBranchKernel(d, r, gridDimX: g, blockDimX: b),
        [nameof(StackScopeKernels.UsingDeclarationHeaderCallKernel)] = static (d, r, g, b) =>
            StackScopeKernels.LaunchUsingDeclarationHeaderCallKernel(d, r, gridDimX: g, blockDimX: b),
        [nameof(StackScopeKernels.UsingDeclarationInDeviceFunctionKernel)] = static (d, r, g, b) =>
            StackScopeKernels.LaunchUsingDeclarationInDeviceFunctionKernel(d, r, gridDimX: g, blockDimX: b),
        [nameof(StackScopeKernels.ContinueAfterUsingDeclarationKernel)] = static (d, r, g, b) =>
            StackScopeKernels.LaunchContinueAfterUsingDeclarationKernel(d, r, gridDimX: g, blockDimX: b),
        [nameof(StackScopeKernels.TwoUsingDeclarationsWithBreakAndContinueKernel)] = static (d, r, g, b) =>
            StackScopeKernels.LaunchTwoUsingDeclarationsWithBreakAndContinueKernel(d, r, gridDimX: g, blockDimX: b),
        [nameof(StackScopeKernels.ContinueInsideFixedInLoopKernel)] = static (d, r, g, b) =>
            StackScopeKernels.LaunchContinueInsideFixedInLoopKernel(d, r, gridDimX: g, blockDimX: b),
        [nameof(StackScopeKernels.FixedMaskKernel)] = static (d, r, g, b) =>
            StackScopeKernels.LaunchFixedMaskKernel(d, r, gridDimX: g, blockDimX: b),
        [nameof(StackScopeKernels.BranchInsideFixedKernel)] = static (d, r, g, b) =>
            StackScopeKernels.LaunchBranchInsideFixedKernel(d, r, gridDimX: g, blockDimX: b),
        [nameof(StackScopeKernels.BarrierLoopContinueInsideFixedKernel)] = static (d, r, g, b) =>
            StackScopeKernels.LaunchBarrierLoopContinueInsideFixedKernel(d, r, gridDimX: g, blockDimX: b),
        [nameof(StackScopeKernels.GotoLoopCrossingUsingStatementKernel)] = static (d, r, g, b) =>
            StackScopeKernels.LaunchGotoLoopCrossingUsingStatementKernel(d, r, gridDimX: g, blockDimX: b),
        [nameof(StackScopeKernels.GotoLoopCrossingFixedStatementKernel)] = static (d, r, g, b) =>
            StackScopeKernels.LaunchGotoLoopCrossingFixedStatementKernel(d, r, gridDimX: g, blockDimX: b),
    };

    public static TheoryData<string, int, int, uint[]> StackScopeCases()
    {
        var data = new TheoryData<string, int, int, uint[]>();

        Add(data, nameof(StackScopeKernels.UsingDeclarationMaskKernel),
            Repeat([0x10000Fu], 8),
            [0x100001u],
            Repeat([0x10000Fu], 80));

        Add(data, nameof(StackScopeKernels.ContinueBeforeUsingDeclarationKernel),
            Repeat([0x100005u, 0x200014u], 4),
            [0x100001u],
            Repeat([0x100005u, 0x200014u], 40));

        Add(data, nameof(StackScopeKernels.UsingDeclarationInBranchKernel),
            Repeat([0x10FF55u, 0xFF00u], 4),
            [0x100101u],
            Repeat([0x10FF55u, 0xFF00u], 40));

        Add(data, nameof(StackScopeKernels.BranchInsideUsingStatementKernel),
            Repeat([0x10FF55u, 0x10FF00u], 4),
            [0x100101u],
            Repeat([0x10FF55u, 0x10FF00u], 40));

        Add(data, nameof(StackScopeKernels.ContinueInsideUsingStatementKernel),
            [0x200049u, 0x200092u, 0x200024u, 0x200049u, 0x200092u, 0x200024u, 0x200049u, 0x200092u],
            [0x200001u],
            [
                0x200049u, 0x200092u, 0x200024u, 0x200049u, 0x200092u, 0x200024u, 0x200049u, 0x200092u,
                0x200024u, 0x200049u, 0x200092u, 0x200024u, 0x200049u, 0x200092u, 0x200024u, 0x200049u,
                0x200092u, 0x200024u, 0x200049u, 0x200092u, 0x200024u, 0x200049u, 0x200092u, 0x200024u,
                0x200049u, 0x200092u, 0x200024u, 0x200049u, 0x200092u, 0x200024u, 0x200049u, 0x200092u,
                0x200049u, 0x200092u, 0x200024u, 0x200049u, 0x200092u, 0x200024u, 0x200049u, 0x200092u,
                0x200049u, 0x200092u, 0x200024u, 0x200049u, 0x200092u, 0x200024u, 0x200049u, 0x200092u,
                0x200024u, 0x200049u, 0x200092u, 0x200024u, 0x200049u, 0x200092u, 0x200024u, 0x200049u,
                0x200092u, 0x200024u, 0x200049u, 0x200092u, 0x200024u, 0x200049u, 0x200092u, 0x200024u,
                0x200049u, 0x200092u, 0x200024u, 0x200049u, 0x200092u, 0x200024u, 0x200049u, 0x200092u,
                0x200049u, 0x200092u, 0x200024u, 0x200049u, 0x200092u, 0x200024u, 0x200049u, 0x200092u
            ]);

        Add(data, nameof(StackScopeKernels.UsingDeclarationHeaderCallKernel),
            Repeat([0x10FF05u, 0x10FF00u], 4),
            [0x100101u],
            Repeat([0x10FF05u, 0x10FF00u], 40));

        Add(data, nameof(StackScopeKernels.UsingDeclarationInDeviceFunctionKernel),
            [0x10FFF9u, 0xFF07u, 0x10FF08u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u],
            [0x100101u],
            [
                0x10FFF9u, 0xFF07u, 0x10FF08u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u,
                0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u,
                0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u,
                0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u,
                0x10FFFFu, 0x10FFFFu, 0x10FFFFu, 0x10FFFFu, 0x10FFFFu, 0x10FFFFu, 0x10FFFFu, 0x10FFFFu,
                0x10FFF9u, 0xFF07u, 0x10FF08u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u,
                0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u,
                0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u,
                0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u, 0x10FFF9u,
                0x10FFFFu, 0x10FFFFu, 0x10FFFFu, 0x10FFFFu, 0x10FFFFu, 0x10FFFFu, 0x10FFFFu, 0x10FFFFu
            ]);

        Add(data, nameof(StackScopeKernels.ContinueAfterUsingDeclarationKernel),
            Repeat([0x300005u, 0x300014u], 4),
            [0x300001u],
            Repeat([0x300005u, 0x300014u], 40));

        Add(data, nameof(StackScopeKernels.TwoUsingDeclarationsWithBreakAndContinueKernel),
            [0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x30FF0Au, 0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x60FF0Cu],
            [0x600101u],
            [
                0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x30FF0Au, 0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x60FF0Cu,
                0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x60FF0Cu,
                0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x60FF0Cu,
                0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x60FF0Cu,
                0x60FF05u, 0x60FF14u, 0x60FF05u, 0x60FF14u, 0x60FF05u, 0x60FF14u, 0x60FF05u, 0x60FF14u,
                0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x30FF0Au, 0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x60FF0Cu,
                0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x60FF0Cu,
                0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x60FF0Cu,
                0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x60FF0Cu, 0x60FF05u, 0x60FF0Cu,
                0x60FF05u, 0x60FF14u, 0x60FF05u, 0x60FF14u, 0x60FF05u, 0x60FF14u, 0x60FF05u, 0x60FF14u
            ]);

        Add(data, nameof(StackScopeKernels.ContinueInsideFixedInLoopKernel),
            [0xCu, 0x13u, 0xFu, 0x4Au, 0x99u, 0x25u, 0x49u, 0x98u],
            [0xCu],
            [
                0xCu, 0x13u, 0xFu, 0x4924924Au, 0x92492499u, 0x24924925u, 0x49249249u, 0x92492498u,
                0x24924924u, 0x49249248u, 0x92492497u, 0x24924923u, 0x49249252u, 0x92492496u, 0x24924922u, 0x49249251u,
                0x92492495u, 0x24924921u, 0x49249250u, 0x92492494u, 0x24924920u, 0x4924924Fu, 0x92492493u, 0x2492492Au,
                0x4924924Eu, 0x92492492u, 0x24924929u, 0x4924924Du, 0x92492491u, 0x24924928u, 0x4924924Cu, 0x92492490u,
                0x50u, 0x95u, 0x2Eu, 0x4Fu, 0x94u, 0x2Du, 0x4Eu, 0x93u,
                0x11u, 0xDu, 0x9u, 0x4924924Fu, 0x92492493u, 0x2492492Au, 0x4924924Eu, 0x92492492u,
                0x24924929u, 0x4924924Du, 0x92492491u, 0x24924928u, 0x4924924Cu, 0x92492490u, 0x24924927u, 0x4924924Bu,
                0x9249249Au, 0x24924926u, 0x4924924Au, 0x92492499u, 0x24924925u, 0x49249249u, 0x92492498u, 0x24924924u,
                0x49249248u, 0x92492497u, 0x24924923u, 0x49249252u, 0x92492496u, 0x24924922u, 0x49249251u, 0x92492495u,
                0x4Au, 0x9Au, 0x28u, 0x49u, 0x99u, 0x27u, 0x53u, 0x98u
            ]);

        Add(data, nameof(StackScopeKernels.FixedMaskKernel),
            [0x30FFu, 0xA0FFu, 0x60FFu, 0x20FFu, 0x90FFu, 0x50FFu, 0x10FFu, 0x80FFu],
            [0x3001u],
            [
                0x30FFu, 0xA0FFu, 0x60FFu, 0x20FFu, 0x90FFu, 0x50FFu, 0x10FFu, 0x80FFu,
                0x40FFu, 0xFFu, 0x70FFu, 0x30FFu, 0xA0FFu, 0x60FFu, 0x20FFu, 0x90FFu,
                0x50FFu, 0x10FFu, 0x80FFu, 0x40FFu, 0xFFu, 0x70FFu, 0x30FFu, 0xA0FFu,
                0x60FFu, 0x20FFu, 0x90FFu, 0x50FFu, 0x10FFu, 0x80FFu, 0x40FFu, 0xFFu,
                0x70FFu, 0x30FFu, 0xA0FFu, 0x60FFu, 0x20FFu, 0x90FFu, 0x50FFu, 0x10FFu,
                0x80FFu, 0x40FFu, 0xFFu, 0x70FFu, 0x30FFu, 0xA0FFu, 0x60FFu, 0x20FFu,
                0x90FFu, 0x50FFu, 0x10FFu, 0x80FFu, 0x40FFu, 0xFFu, 0x70FFu, 0x30FFu,
                0xA0FFu, 0x60FFu, 0x20FFu, 0x90FFu, 0x50FFu, 0x10FFu, 0x80FFu, 0x40FFu,
                0xFFu, 0x70FFu, 0x30FFu, 0xA0FFu, 0x60FFu, 0x20FFu, 0x90FFu, 0x50FFu,
                0x10FFu, 0x80FFu, 0x40FFu, 0xFFu, 0x70FFu, 0x30FFu, 0xA0FFu, 0x60FFu
            ]);

        Add(data, nameof(StackScopeKernels.BranchInsideFixedKernel),
            Repeat([0xFF55u, 0xFF00u], 4),
            [0x101u],
            Repeat([0xFF55u, 0xFF00u], 40));

        Add(data, nameof(StackScopeKernels.BarrierLoopContinueInsideFixedKernel),
            [0xCu, 0x28u, 0x18u, 0x8u, 0x24u, 0x14u, 0x4u, 0x20u],
            [0xCu],
            [
                0xCu, 0x28u, 0x18u, 0x8u, 0x24u, 0x14u, 0x4u, 0x20u,
                0x10u, 0x0u, 0x1Cu, 0xCu, 0x28u, 0x18u, 0x8u, 0x24u,
                0x14u, 0x4u, 0x20u, 0x10u, 0x0u, 0x1Cu, 0xCu, 0x28u,
                0x18u, 0x8u, 0x24u, 0x14u, 0x4u, 0x20u, 0x10u, 0x0u,
                0x1Cu, 0xCu, 0x28u, 0x18u, 0x8u, 0x24u, 0x14u, 0x4u,
                0x20u, 0x10u, 0x0u, 0x1Cu, 0xCu, 0x28u, 0x18u, 0x8u,
                0x24u, 0x14u, 0x4u, 0x20u, 0x10u, 0x0u, 0x1Cu, 0xCu,
                0x28u, 0x18u, 0x8u, 0x24u, 0x14u, 0x4u, 0x20u, 0x10u,
                0x0u, 0x1Cu, 0xCu, 0x28u, 0x18u, 0x8u, 0x24u, 0x14u,
                0x4u, 0x20u, 0x10u, 0x0u, 0x1Cu, 0xCu, 0x28u, 0x18u
            ]);

        Add(data, nameof(StackScopeKernels.GotoLoopCrossingUsingStatementKernel),
            Repeat([0xFF1000FFu, 0xFF20EEFFu, 0xFF20EEFFu, 0xFF20EEFFu], 2),
            [0x1100001u],
            Repeat([0xFF1000FFu, 0xFF20EEFFu, 0xFF20EEFFu, 0xFF20EEFFu], 20));

        Add(data, nameof(StackScopeKernels.GotoLoopCrossingFixedStatementKernel),
            [0xFF0300FFu, 0xFF0AEEFFu, 0xFF06EEFFu, 0xFF02EEFFu, 0xFF0900FFu, 0xFF05EEFFu, 0xFF01EEFFu, 0xFF08EEFFu],
            [0x1030001u],
            [
                0xFF0300FFu, 0xFF0AEEFFu, 0xFF06EEFFu, 0xFF02EEFFu, 0xFF0900FFu, 0xFF05EEFFu, 0xFF01EEFFu, 0xFF08EEFFu,
                0xFF0400FFu, 0xFF00EEFFu, 0xFF07EEFFu, 0xFF03EEFFu, 0xFF0A00FFu, 0xFF06EEFFu, 0xFF02EEFFu, 0xFF09EEFFu,
                0xFF0500FFu, 0xFF01EEFFu, 0xFF08EEFFu, 0xFF04EEFFu, 0xFF0000FFu, 0xFF07EEFFu, 0xFF03EEFFu, 0xFF0AEEFFu,
                0xFF0600FFu, 0xFF02EEFFu, 0xFF09EEFFu, 0xFF05EEFFu, 0xFF0100FFu, 0xFF08EEFFu, 0xFF04EEFFu, 0xFF00EEFFu,
                0xFF0700FFu, 0xFF03EEFFu, 0xFF0AEEFFu, 0xFF06EEFFu, 0xFF0200FFu, 0xFF09EEFFu, 0xFF05EEFFu, 0xFF01EEFFu,
                0xFF0800FFu, 0xFF04EEFFu, 0xFF00EEFFu, 0xFF07EEFFu, 0xFF0300FFu, 0xFF0AEEFFu, 0xFF06EEFFu, 0xFF02EEFFu,
                0xFF0900FFu, 0xFF05EEFFu, 0xFF01EEFFu, 0xFF08EEFFu, 0xFF0400FFu, 0xFF00EEFFu, 0xFF07EEFFu, 0xFF03EEFFu,
                0xFF0A00FFu, 0xFF06EEFFu, 0xFF02EEFFu, 0xFF09EEFFu, 0xFF0500FFu, 0xFF01EEFFu, 0xFF08EEFFu, 0xFF04EEFFu,
                0xFF0000FFu, 0xFF07EEFFu, 0xFF03EEFFu, 0xFF0AEEFFu, 0xFF0600FFu, 0xFF02EEFFu, 0xFF09EEFFu, 0xFF05EEFFu,
                0xFF0100FFu, 0xFF08EEFFu, 0xFF04EEFFu, 0xFF00EEFFu, 0xFF0700FFu, 0xFF03EEFFu, 0xFF0AEEFFu, 0xFF06EEFFu
            ]);

        return data;
    }

    [SkippableTheory]
    [MemberData(nameof(StackScopeCases))]
    [Scenario(TestScenario.Boundary)]
    public void StackScopeKernels_OnStackAndCuda_MatchExpectedValues(
        string kernelName,
        int gridDimX,
        int blockDimX,
        uint[] expected)
    {
        var paths = StackPaths();
        Skip.If(paths.Length == 0, "Runs only on a stack build or CUDA.");

        var count = gridDimX * blockDimX;
        var input = new int[count];
        for (var i = 0; i < count; i++)
        {
            input[i] = (i * 7 + 3) % 11;
        }

        var launch = s_t1Launchers[kernelName];
        foreach (var path in paths)
        {
            path.Preflight?.Invoke();
            var result = new uint[count];
            AssertManagedStackLaunch(path, () => launch(input, result, (uint)gridDimX, (uint)blockDimX));
            Assert.True(expected.SequenceEqual(result),
                $"{kernelName} {gridDimX}x{blockDimX} {path.Name}: expected [{Format(expected)}], got [{Format(result)}]");
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void UsingDeclarationDisposeOrder_SingleGuardWithActiveMask_DisposesAfterBody()
    {
        var paths = StackPaths();
        Skip.If(paths.Length == 0, "Runs only on a stack build or CUDA.");

        uint[] expected = Repeat([0xFDu], 8);
        foreach (var path in paths)
        {
            path.Preflight?.Invoke();
            var result = new uint[8];
            AssertManagedStackLaunch(path, () =>
                StackScopeKernels.LaunchUsingDeclarationDisposeOrderSingleKernel(result, blockDimX: 8));
            Assert.True(expected.SequenceEqual(result),
                $"{path.Name}: expected [{Format(expected)}], got [{Format(result)}]");
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void UsingDeclarationDisposeOrder_TwoGuardsWithActiveMask_DisposesInReverseOrder()
    {
        var paths = StackPaths();
        Skip.If(paths.Length == 0, "Runs only on a stack build or CUDA.");

        uint[] expected = Repeat([0xF21u], 8);
        foreach (var path in paths)
        {
            path.Preflight?.Invoke();
            var result = new uint[8];
            AssertManagedStackLaunch(path, () =>
                StackScopeKernels.LaunchUsingDeclarationDisposeOrderNestedKernel(result, blockDimX: 8));
            Assert.True(expected.SequenceEqual(result),
                $"{path.Name}: expected [{Format(expected)}], got [{Format(result)}]");
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LocalFunctionAfterUsingDeclaration_CalledBeforeDeclaration_RunsAndDisposesOnce()
    {
        var paths = StackPaths();
        Skip.If(paths.Length == 0, "Runs only on a stack build or CUDA.");

        uint[] expected = Repeat([0x100011u], 8);
        foreach (var path in paths)
        {
            path.Preflight?.Invoke();
            var result = new uint[8];
            AssertManagedStackLaunch(path, () =>
                StackScopeKernels.LaunchLocalFunctionAfterUsingDeclarationKernel(result, blockDimX: 8));
            Assert.True(expected.SequenceEqual(result),
                $"{path.Name}: expected [{Format(expected)}], got [{Format(result)}]");
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LocalFunctionAfterUsingDeclaration_CapturingPriorConst_RunsAndDisposesOnce()
    {
        var paths = StackPaths();
        Skip.If(paths.Length == 0, "Runs only on a stack build or CUDA.");

        uint[] expected = Repeat([0x100015u], 8);
        foreach (var path in paths)
        {
            path.Preflight?.Invoke();
            var result = new uint[8];
            AssertManagedStackLaunch(path, () =>
                StackScopeKernels.LaunchLocalFunctionCapturingPriorConstAfterUsingDeclarationKernel(
                    result, blockDimX: 8));
            Assert.True(expected.SequenceEqual(result),
                $"{path.Name}: expected [{Format(expected)}], got [{Format(result)}]");
        }
    }

    private static DiffPathRunner[] StackPaths() =>
        DiffPaths.Available.Where(static p => RunsOnAStack(p.Path)).ToArray();

    private static void Add(TheoryData<string, int, int, uint[]> data, string kernel,
        uint[] g1b8, uint[] g1b1, uint[] g2b40)
    {
        data.Add(kernel, 1, 8, g1b8);
        data.Add(kernel, 1, 1, g1b1);
        data.Add(kernel, 2, 40, g2b40);
    }

    private static uint[] Repeat(uint[] pattern, int count)
    {
        var result = new uint[pattern.Length * count];
        for (var i = 0; i < count; i++)
        {
            Array.Copy(pattern, 0, result, i * pattern.Length, pattern.Length);
        }

        return result;
    }

    private static string Format(uint[] values) =>
        string.Join(", ", values.Select(static v => $"0x{v:X}"));
}
