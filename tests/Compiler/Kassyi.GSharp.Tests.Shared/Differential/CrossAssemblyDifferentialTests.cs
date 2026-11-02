#nullable enable
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.DeviceLibrary;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for cross-assembly structs, explicit/packed layouts, functions, operator macros (#711),
/// and barrier-suspending device functions (#433) from <c>Kassyi.GSharp.Tests.DeviceLibrary</c> (Lane D6 Stage 2).
/// </summary>
public class CrossAssemblyDifferentialTests
{
    private const int Rows = 128;

    private static readonly StructElementComparer<SwapPair> s_swapPairComparer = new(
        static (ctx, e, a) =>
        {
            ctx.Field(nameof(SwapPair.A), e.A, a.A);
            ctx.Field(nameof(SwapPair.B), e.B, a.B);
        });

    private static readonly StructElementComparer<PackedPair> s_packedPairComparer = new(
        static (ctx, e, a) =>
        {
            ctx.Field(nameof(PackedPair.Tag), e.Tag, a.Tag);
            ctx.Field(nameof(PackedPair.Value), e.Value, a.Value);
        });

    private static readonly StructElementComparer<Sized> s_sizedComparer = new(
        static (ctx, e, a) => ctx.Field(nameof(Sized.A), e.A, a.A));

    private static readonly StructElementComparer<IntFloatUnion> s_unionComparer = new(
        static (ctx, e, a) =>
        {
            ctx.Field(nameof(IntFloatUnion.Bits), e.Bits, a.Bits);
            ctx.Field(nameof(IntFloatUnion.Tag), e.Tag, a.Tag);
        });

    private static readonly DifferentialCase s_structsAndLayouts = new(
        $"{nameof(CrossAssemblyDifferentialKernels)}.{nameof(CrossAssemblyDifferentialKernels.CrossAssemblyStructsAndLayoutsKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .Out<int>("r", rows, CrossAssemblyDifferentialKernels.StructsAndLayoutsColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => CrossAssemblyDifferentialKernels.LaunchCrossAssemblyStructsAndLayoutsKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => CrossAssemblyDifferentialKernels.CrossAssemblyStructsAndLayoutsKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_functionsAndMacros = new(
        $"{nameof(CrossAssemblyDifferentialKernels)}.{nameof(CrossAssemblyDifferentialKernels.CrossAssemblyFunctionsAndMacrosKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .Out<int>("r", rows, CrossAssemblyDifferentialKernels.FunctionsAndMacrosColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => CrossAssemblyDifferentialKernels.LaunchCrossAssemblyFunctionsAndMacrosKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, _) =>
        {
            var xs = b.Get<int>("xs");
            var ys = b.Get<int>("ys");
            var r = b.Get<int>("r");
            for (int tid = 0; tid < xs.Length; tid++)
            {
                int x = xs[tid];
                int y = ys[tid];
                uint odd = ((uint)x * 2u) | 1u;
                uint mu = LibMath.Mu(odd);
                var pair = new Pair { A = x, B = y };
                int o = tid * 10;

                r[o + 0] = unchecked((int)mu);
                r[o + 1] = unchecked((int)mu);
                r[o + 2] = x + y;
                r[o + 3] = (x * 3) - y;
                r[o + 4] = x * 2;
                r[o + 5] = LibLanguageExports.CombineOverload(x, y);
                r[o + 6] = unchecked((int)LibLanguageExports.CombineOverload((long)x, (long)y));
                r[o + 7] = pair.A + pair.B;
                r[o + 8] = 1;
                r[o + 9] = pair.A - pair.B;
            }
        },
    };

    private static readonly DifferentialCase s_explicitLayoutArrays = new(
        $"{nameof(CrossAssemblyDifferentialKernels)}.{nameof(CrossAssemblyDifferentialKernels.CrossAssemblyExplicitLayoutArrayKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            var fs = DiffValues.Single(rng, rows);
            var swaps = new SwapPair[rows];
            var packeds = new PackedPair[rows];
            var sizeds = new Sized[rows];
            var unions = new IntFloatUnion[rows];
            for (int i = 0; i < rows; i++)
            {
                swaps[i] = new SwapPair { A = xs[i], B = ys[i] };
                packeds[i] = new PackedPair { Tag = unchecked((byte)xs[i]), Value = ys[i] };
                sizeds[i] = new Sized { A = xs[i] ^ ys[i] };
                float f = float.IsFinite(fs[i]) ? fs[i] : ((i & 1) == 0 ? 1.5f : -2.25f);
                unions[i] = new IntFloatUnion { Value = f, Tag = ys[i] };
            }

            return new DiffBuffers()
                .In("swaps", swaps, OutputPolicy.Exact, s_swapPairComparer)
                .In("packeds", packeds, OutputPolicy.Exact, s_packedPairComparer)
                .In("sizeds", sizeds, OutputPolicy.Exact, s_sizedComparer)
                .In("unions", unions, OutputPolicy.Exact, s_unionComparer)
                .Out<int>("r", rows, ["swap_combo", "packed_combo", "sized_orig", "union_bits_xor_tag"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => CrossAssemblyDifferentialKernels.LaunchCrossAssemblyExplicitLayoutArrayKernel(
            b.Get<SwapPair>("swaps"),
            b.Get<PackedPair>("packeds"),
            b.Get<Sized>("sizeds"),
            b.Get<IntFloatUnion>("unions"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => CrossAssemblyDifferentialKernels.CrossAssemblyExplicitLayoutArrayKernel(
            b.Get<SwapPair>("swaps"),
            b.Get<PackedPair>("packeds"),
            b.Get<Sized>("sizeds"),
            b.Get<IntFloatUnion>("unions"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_barrierExchange = new(
        $"{nameof(CrossAssemblyDifferentialKernels)}.{nameof(CrossAssemblyDifferentialKernels.CrossAssemblyBarrierExchangeKernel)}")
    {
        Shape = LaunchShapes.Grid1D(gridX: 1, blockX: 64, rows: 64),
        Inputs = static (rng, shape) =>
        {
            var inputs = DiffValues.Int32(rng, shape.Rows);
            return new DiffBuffers()
                .In("inputs", inputs)
                .Out<int>("scratch", shape.Rows, OutputPolicy.Exact)
                .Out<int>("r", shape.Rows, OutputPolicy.Exact);
        },
        Launch = static (b, s) => CrossAssemblyDifferentialKernels.LaunchCrossAssemblyBarrierExchangeKernel(
            b.Get<int>("inputs"),
            b.Get<int>("scratch"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, s) =>
        {
            var inputs = b.Get<int>("inputs");
            var scratch = b.Get<int>("scratch");
            var r = b.Get<int>("r");
            int blockDim = (int)s.BlockX;
            for (int t = 0; t < blockDim; t++)
            {
                scratch[t] = inputs[t];
            }

            for (int t = 0; t < blockDim; t++)
            {
                int next = scratch[(t + 1) % blockDim];
                r[t] = next + inputs[t];
            }
        },
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CrossAssemblyStructsAndLayouts_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_structsAndLayouts);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CrossAssemblyFunctionsAndMacros_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_functionsAndMacros);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CrossAssemblyExplicitLayoutArray_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_explicitLayoutArrays);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CrossAssemblyBarrierExchange_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_barrierExchange);
}
