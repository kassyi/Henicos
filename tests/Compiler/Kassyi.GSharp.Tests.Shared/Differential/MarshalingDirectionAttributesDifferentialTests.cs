#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Kassyi.GSharp.Core;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.MarshalingDifferentialTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for parameter direction attributes ([In] array mutation isolation
/// and [Out] array full vs partial strided write semantics).
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public sealed class MarshalingDirectionAttributesDifferentialTests
{
    internal static readonly DifferentialCase s_inArrayMutationIsolation = new(
        KernelName(nameof(MarshalingAliasingDifferentialKernels.InArrayMutationIsolationKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) =>
        {
            int[] inPrim = [10, 20];
            DiffVec2Padded[] inConv =
            [
                new DiffVec2Padded { A = 1.0f, V = new Vector2(2.0f, 3.0f) },
            ];
            DiffNestedDeep[] inDeep =
            [
                new DiffNestedDeep
                {
                    Indices = [1, 2],
                    Inner = new DiffInnerBag { Weights = [5.0f, 6.0f], Bias = 7 },
                    Scale = 8,
                },
            ];

            return new DiffBuffers()
                .In("inPrimitives", inPrim, OutputPolicy.Exact, readOnly: true)
                .In(
                    "inConvertStructs",
                    inConv,
                    OutputPolicy.ForStruct(Vec2PaddedComparer),
                    Vec2PaddedComparer,
                    readOnly: true)
                .InDeep("inDeepStructs", inDeep, CloneNestedDeep, CompareNestedDeep, readOnly: true)
                .Out<float>("outObserved", 3, OutputPolicy.Bitwise);
        },
        Reference = static (b, s) =>
        {
            int[] primCopy = (int[])b.Get<int>("inPrimitives").Clone();
            DiffVec2Padded[] convCopy = (DiffVec2Padded[])b.Get<DiffVec2Padded>("inConvertStructs").Clone();
            DiffNestedDeep[] inDeep = b.Get<DiffNestedDeep>("inDeepStructs");
            DiffArrayCloneScope scope = new(
                new Dictionary<object, object>(ReferenceEqualityComparer.Instance));
            DiffNestedDeep[] deepCopy = new DiffNestedDeep[inDeep.Length];
            for (int i = 0; i < inDeep.Length; i++)
            {
                deepCopy[i] = CloneNestedDeep(inDeep[i], scope);
            }

            float[] outObserved = b.Get<float>("outObserved");
            SequentialOracle.Run(s, ThreadOrder.Forward, () =>
                MarshalingAliasingDifferentialKernels.LaunchInArrayMutationIsolationKernel(
                    primCopy, convCopy, deepCopy, outObserved, gridDimX: s.GridX, blockDimX: s.BlockX));
        },
        Launch = static (b, s) =>
        {
            int[] inPrim = b.Get<int>("inPrimitives");
            DiffVec2Padded[] inConv = b.Get<DiffVec2Padded>("inConvertStructs");
            DiffNestedDeep[] inDeep = b.Get<DiffNestedDeep>("inDeepStructs");
            float[] outObserved = b.Get<float>("outObserved");
            MarshalingAliasingDifferentialKernels.LaunchInArrayMutationIsolationKernel(
                inPrim, inConv, inDeep, outObserved, gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_outArrayFullWrite = new(
        KernelName(nameof(MarshalingAliasingDifferentialKernels.OutArrayStridedWriteKernel), "Full"))
    {
        Shape = LaunchShapes.Linear(64, blockX: 32),
        Seeds = [0],
        Inputs = static (_, _) =>
        {
            const int n = 64;
            int[] src = new int[n];
            for (int i = 0; i < n; i++)
            {
                src[i] = i + 1;
            }

            return new DiffBuffers()
                .In("src", src, OutputPolicy.Exact, readOnly: true)
                .Out<int>("dstPrim", n, OutputPolicy.Exact)
                .Out<DiffVec2Padded>("dstConvert", n, OutputPolicy.ForStruct(Vec2PaddedComparer), Vec2PaddedComparer);
        },
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            int[] src = b.Get<int>("src");
            int[] dstPrim = b.Get<int>("dstPrim");
            DiffVec2Padded[] dstConvert = b.Get<DiffVec2Padded>("dstConvert");
            MarshalingAliasingDifferentialKernels.LaunchOutArrayStridedWriteKernel(
                src, dstPrim, dstConvert, 1, gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_outArrayPartialWrite = new(
        KernelName(nameof(MarshalingAliasingDifferentialKernels.OutArrayStridedWriteKernel), "Partial"))
    {
        Shape = LaunchShapes.Linear(64, blockX: 32),
        Seeds = [0],
        Inputs = static (_, _) =>
        {
            const int n = 64;
            int[] src = new int[n];
            for (int i = 0; i < n; i++)
            {
                src[i] = i + 1;
            }

            return new DiffBuffers()
                .In("src", src, OutputPolicy.Exact, readOnly: true)
                .Out<int>(
                    "dstPrim",
                    rows: n / 2,
                    columns: [("even_written", OutputPolicy.Exact), ("odd_unwritten", OutputPolicy.Ignore)])
                .Out<DiffVec2Padded>(
                    "dstConvert",
                    rows: n / 2,
                    columns:
                    [
                        ("even_written", OutputPolicy.ForStruct(Vec2PaddedComparer)),
                        ("odd_unwritten", OutputPolicy.Ignore),
                    ],
                    Vec2PaddedComparer);
        },
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            int[] src = b.Get<int>("src");
            int[] dstPrim = b.Get<int>("dstPrim");
            DiffVec2Padded[] dstConvert = b.Get<DiffVec2Padded>("dstConvert");
            MarshalingAliasingDifferentialKernels.LaunchOutArrayStridedWriteKernel(
                src, dstPrim, dstConvert, 2, gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void DirectionAttributes_InArrayMutationIsolation_ProtectsCallerBuffers() =>
        DifferentialRunner.Run(s_inArrayMutationIsolation);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void DirectionAttributes_OutArrayFullWrite_MatchesSequentialOracle() =>
        DifferentialRunner.Run(s_outArrayFullWrite);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void DirectionAttributes_OutArrayPartialWrite_MatchesSequentialOracle() =>
        DifferentialRunner.Run(s_outArrayPartialWrite);
}
