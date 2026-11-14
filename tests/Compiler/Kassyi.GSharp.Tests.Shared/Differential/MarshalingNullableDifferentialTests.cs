#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for <c>Nullable&lt;T&gt;</c> on every launch path, whose host and device layouts differ on CUDA
/// (#961) or whose value holds an array (#972).
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
public sealed class MarshalingNullableDifferentialTests
{
    private const int Rows = 64;
    private const int BlockX = 32;

    private sealed class NullableComparer<T> : IElementComparer<T?>
        where T : struct
    {
        public static readonly NullableComparer<T> Instance = new();

        public bool Equals(in T? expected, in T? actual, OutputPolicy policy) =>
            EqualityComparer<T?>.Default.Equals(expected, actual);

        public string Format(in T? value) => value.HasValue ? value.Value.ToString() ?? string.Empty : "null";
    }

    private static readonly IElementComparer<NullableDiffHolder> HolderComparer =
        new StructElementComparer<NullableDiffHolder>(static (ctx, e, a) =>
        {
            ctx.Field(nameof(NullableDiffHolder.A), e.A, a.A, OutputPolicy.Exact);
            ctx.Equal(nameof(NullableDiffHolder.V), e.V, a.V);
            ctx.Equal(nameof(NullableDiffHolder.Inner), e.Inner, a.Inner);
            ctx.Equal(nameof(NullableDiffHolder.N), e.N, a.N);
        });

    private const string Kernels = nameof(MarshalingNullableDifferentialKernels);

    private static NullableDiffInner Inner(int i) => new() { X = i * 0.5f, V = new Vector2(i + 0.25f, -i - 0.75f) };

    internal static readonly DifferentialCase s_scalars = new(
        $"{Kernels}.{nameof(MarshalingNullableDifferentialKernels.ScalarsKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: BlockX),
        Seeds = [0, 1],
        Inputs = static (rng, s) =>
        {
            bool present = (rng.NextInt32() & 1) == 0;
            return new DiffBuffers()
                .InScalar<Vector2?>("v", present ? new Vector2(1.5f, -2.25f) : null, NullableComparer<Vector2>.Instance)
                .InScalar<NullableDiffInner?>(
                    "inner", present ? Inner(3) : null, NullableComparer<NullableDiffInner>.Instance)
                .InScalar<bool>("bagPresent", present)
                .In("bagData", new[] { 4, 5, 6 })
                .InScalar<int?>("i", present ? null : 17, NullableComparer<int>.Instance)
                .Out<float>("probe", s.Rows, MarshalingNullableDifferentialKernels.ScalarColumns, OutputPolicy.Exact);
        },
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            NullableDiffBag? bag = b.GetScalar<bool>("bagPresent")
                ? new NullableDiffBag { Tag = 9, Data = b.Get<int>("bagData") }
                : null;
            MarshalingNullableDifferentialKernels.LaunchScalarsKernel(
                b.GetScalar<Vector2?>("v"),
                b.GetScalar<NullableDiffInner?>("inner"),
                bag,
                b.GetScalar<int?>("i"),
                b.Get<float>("probe"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_refs = new(
        $"{Kernels}.{nameof(MarshalingNullableDifferentialKernels.RefsKernel)}")
    {
        // One thread: every thread assigns the out parameter
        Shape = LaunchShape.ForRows(1, blockX: 1),
        Seeds = [0, 1],
        Inputs = static (rng, _) =>
        {
            bool present = (rng.NextInt32() & 1) == 0;
            return new DiffBuffers()
                .RefScalar<Vector2?>("v", present ? new Vector2(1f, 2f) : null,
                    OutputPolicy.ForStruct(NullableComparer<Vector2>.Instance), NullableComparer<Vector2>.Instance)
                .RefScalar<NullableDiffInner?>("inner", present ? Inner(5) : null,
                    OutputPolicy.ForStruct(NullableComparer<NullableDiffInner>.Instance),
                    NullableComparer<NullableDiffInner>.Instance)
                .RefScalar<int?>("i", present ? 3 : null,
                    OutputPolicy.ForStruct(NullableComparer<int>.Instance), NullableComparer<int>.Instance)
                .OutScalar<Vector2?>("made",
                    OutputPolicy.ForStruct(NullableComparer<Vector2>.Instance), NullableComparer<Vector2>.Instance)
                .Out<int>("o", 1, OutputPolicy.Exact);
        },
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            MarshalingNullableDifferentialKernels.LaunchRefsKernel(
                ref b.GetScalarRef<Vector2?>("v"),
                ref b.GetScalarRef<NullableDiffInner?>("inner"),
                ref b.GetScalarRef<int?>("i"),
                out Vector2? made,
                b.Get<int>("o"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX);
            b.SetScalar("made", made);
        },
    };

    internal static readonly DifferentialCase s_arrays = new(
        $"{Kernels}.{nameof(MarshalingNullableDifferentialKernels.ArraysKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: BlockX),
        Inputs = static (rng, s) =>
        {
            var a = new Vector2?[s.Rows];
            var b = new NullableDiffInner?[s.Rows];
            var c = new int?[s.Rows];
            for (int i = 0; i < s.Rows; i++)
            {
                int r = rng.NextInt32();
                a[i] = (r & 1) == 0 ? null : new Vector2(i, r & 0xFF);
                b[i] = (r & 2) == 0 ? null : Inner(r & 0x7F);
                c[i] = (r & 4) == 0 ? null : (r >> 8) & 0xFFFF;
            }

            return new DiffBuffers()
                .In("a", a, OutputPolicy.ForStruct(NullableComparer<Vector2>.Instance),
                    NullableComparer<Vector2>.Instance)
                .In("b", b, OutputPolicy.ForStruct(NullableComparer<NullableDiffInner>.Instance),
                    NullableComparer<NullableDiffInner>.Instance)
                .In("c", c, OutputPolicy.ForStruct(NullableComparer<int>.Instance), NullableComparer<int>.Instance)
                .Out<float>("probe", s.Rows, MarshalingNullableDifferentialKernels.ArrayColumns, OutputPolicy.Exact);
        },
        OracleIsLaunch = true,
        Launch = static (b, s) => MarshalingNullableDifferentialKernels.LaunchArraysKernel(
            b.Get<Vector2?>("a"),
            b.Get<NullableDiffInner?>("b"),
            b.Get<int?>("c"),
            b.Get<float>("probe"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
    };

    internal static readonly DifferentialCase s_returnVector2 = new(
        $"{Kernels}.{nameof(MarshalingNullableDifferentialKernels.ReturnVector2Kernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: BlockX),
        Seeds = [0],
        Inputs = static (_, s) => new DiffBuffers()
            .OutAutoResult<Vector2?>(s.Rows, OutputPolicy.ForStruct(NullableComparer<Vector2>.Instance),
                NullableComparer<Vector2>.Instance),
        OracleIsLaunch = true,
        Launch = static (b, s) => b.SetAutoResult(
            MarshalingNullableDifferentialKernels.LaunchReturnVector2Kernel(gridDimX: s.GridX, blockDimX: s.BlockX)),
    };

    internal static readonly DifferentialCase s_returnInner = new(
        $"{Kernels}.{nameof(MarshalingNullableDifferentialKernels.ReturnInnerKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: BlockX),
        Seeds = [0],
        Inputs = static (_, s) => new DiffBuffers()
            .OutAutoResult<NullableDiffInner?>(
                s.Rows,
                OutputPolicy.ForStruct(NullableComparer<NullableDiffInner>.Instance),
                NullableComparer<NullableDiffInner>.Instance),
        OracleIsLaunch = true,
        Launch = static (b, s) => b.SetAutoResult(
            MarshalingNullableDifferentialKernels.LaunchReturnInnerKernel(gridDimX: s.GridX, blockDimX: s.BlockX)),
    };

    internal static readonly DifferentialCase s_fields = new(
        $"{Kernels}.{nameof(MarshalingNullableDifferentialKernels.FieldsKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: BlockX),
        Inputs = static (rng, s) =>
        {
            var h = new NullableDiffHolder[s.Rows];
            for (int i = 0; i < s.Rows; i++)
            {
                int r = rng.NextInt32();
                h[i] = new NullableDiffHolder
                {
                    A = i + 0.5f,
                    V = (r & 1) == 0 ? null : new Vector2(i, -(r & 0xFF)),
                    Inner = (r & 2) == 0 ? null : Inner(r & 0x3F),
                    N = (r & 4) == 0 ? null : r >> 16,
                };
            }

            return new DiffBuffers()
                .In("h", h, OutputPolicy.ForStruct(HolderComparer), HolderComparer)
                .Out<float>("probe", s.Rows, MarshalingNullableDifferentialKernels.FieldColumns, OutputPolicy.Exact);
        },
        OracleIsLaunch = true,
        Launch = static (b, s) => MarshalingNullableDifferentialKernels.LaunchFieldsKernel(
            b.Get<NullableDiffHolder>("h"),
            b.Get<float>("probe"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
    };

    internal static readonly DifferentialCase s_arrayFields = new(
        $"{Kernels}.{nameof(MarshalingNullableDifferentialKernels.ArrayFieldsKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: BlockX),
        Inputs = static (rng, s) =>
        {
            var items = new Vector2?[s.Rows];
            var inners = new NullableDiffInner?[s.Rows];
            for (int i = 0; i < s.Rows; i++)
            {
                int r = rng.NextInt32();
                items[i] = (r & 1) == 0 ? null : new Vector2(r & 0xFF, i);
                inners[i] = (r & 2) == 0 ? null : Inner(i);
            }

            return new DiffBuffers()
                .In("items", items, OutputPolicy.ForStruct(NullableComparer<Vector2>.Instance),
                    NullableComparer<Vector2>.Instance)
                .In("inners", inners, OutputPolicy.ForStruct(NullableComparer<NullableDiffInner>.Instance),
                    NullableComparer<NullableDiffInner>.Instance)
                .Out<float>(
                    "probe", s.Rows, MarshalingNullableDifferentialKernels.ArrayFieldColumns, OutputPolicy.Exact);
        },
        OracleIsLaunch = true,
        Launch = static (b, s) => MarshalingNullableDifferentialKernels.LaunchArrayFieldsKernel(
            new NullableDiffArrayHolder
            {
                Items = b.Get<Vector2?>("items"),
                Inners = b.Get<NullableDiffInner?>("inners"),
            },
            b.Get<float>("probe"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
    };

    internal static readonly DifferentialCase s_bags = new(
        $"{Kernels}.{nameof(MarshalingNullableDifferentialKernels.BagsKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: BlockX),
        Seeds = [0, 1],
        Inputs = static (rng, s) =>
        {
            bool present = (rng.NextInt32() & 1) == 0;
            var holderData = new int[s.Rows];
            var holderPresent = new int[s.Rows];
            for (int i = 0; i < s.Rows; i++)
            {
                int r = rng.NextInt32();
                holderData[i] = r & 0xFFFF;
                holderPresent[i] = r & 1;
            }

            return new DiffBuffers()
                .InScalar<bool>("bagPresent", present)
                .In("bagData", new[] { 11, 12, 13, 14, 15 })
                .In("holderData", holderData)
                .In("holderPresent", holderPresent)
                .Out<int>("probe", s.Rows, MarshalingNullableDifferentialKernels.BagColumns, OutputPolicy.Exact);
        },
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            NullableDiffBag? bag = b.GetScalar<bool>("bagPresent")
                ? new NullableDiffBag { Tag = 21, Data = b.Get<int>("bagData") }
                : null;
            var holderData = b.Get<int>("holderData");
            var holderPresent = b.Get<int>("holderPresent");
            var holders = new NullableDiffBagHolder[holderData.Length];
            for (int i = 0; i < holders.Length; i++)
            {
                holders[i] = new NullableDiffBagHolder
                {
                    A = i,
                    Bag = holderPresent[i] == 0 ? null : new NullableDiffBag { Tag = i, Data = [holderData[i]] },
                };
            }

            MarshalingNullableDifferentialKernels.LaunchBagsKernel(
                ref bag,
                holders,
                b.Get<int>("probe"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX);

            // The kernel's writes into the arrays that the holders' values hold
            for (int i = 0; i < holders.Length; i++)
            {
                if (holders[i].Bag is { } written)
                {
                    holderData[i] = written.Data[0];
                }
            }
        },
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NullableMarshaling_ScalarArguments_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_scalars);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NullableMarshaling_RefAndOutArguments_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_refs);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NullableMarshaling_ArrayArguments_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_arrays);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NullableMarshaling_ReturnedVector2_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_returnVector2);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NullableMarshaling_ReturnedUserStruct_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_returnInner);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NullableMarshaling_StructFields_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_fields);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NullableMarshaling_ArrayFieldElements_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_arrayFields);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NullableMarshaling_ValuesThatHoldArrays_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_bags);
}
