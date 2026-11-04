#nullable enable
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.DeviceLibrary;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for cross-assembly structs, explicit/packed layouts, functions, operator macros (#711),
/// and barrier-suspending device functions (#433) from <c>Kassyi.GSharp.Tests.DeviceLibrary</c>.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
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

    private static DifferentialCase CreateStructCase(
        string kernelName,
        string[] columns,
        Action<int[], int[], int[], uint, uint> launch,
        Action<int[], int[], int[]> oracle) =>
        new($"{nameof(CrossAssemblyDifferentialKernels)}.{kernelName}")
        {
            Shape = LaunchShape.ForRows(Rows, blockX: 64),
            Inputs = (rng, rows) =>
            {
                var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
                return new DiffBuffers()
                    .In("xs", xs)
                    .In("ys", ys)
                    .Out<int>("r", rows, columns, OutputPolicy.Exact);
            },
            Launch = (b, s) => launch(b.Get<int>("xs"), b.Get<int>("ys"), b.Get<int>("r"), s.GridX, s.BlockX),
            Oracle = (b, _) => oracle(b.Get<int>("xs"), b.Get<int>("ys"), b.Get<int>("r")),
        };

    private static readonly DifferentialCase s_vec2 = CreateStructCase(
        nameof(CrossAssemblyDifferentialKernels.CrossAssemblyVec2Kernel),
        ["vec2_x", "vec2_y", "vec2_sum"],
        static (xs, ys, r, gx, bx) => CrossAssemblyDifferentialKernels.LaunchCrossAssemblyVec2Kernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        CrossAssemblyDifferentialKernels.CrossAssemblyVec2Kernel);

    private static readonly DifferentialCase s_particle = CreateStructCase(
        nameof(CrossAssemblyDifferentialKernels.CrossAssemblyParticleKernel),
        ["particle_weight"],
        static (xs, ys, r, gx, bx) => CrossAssemblyDifferentialKernels.LaunchCrossAssemblyParticleKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        CrossAssemblyDifferentialKernels.CrossAssemblyParticleKernel);

    private static readonly DifferentialCase s_scaled = CreateStructCase(
        nameof(CrossAssemblyDifferentialKernels.CrossAssemblyScaledKernel),
        ["scaled_val"],
        static (xs, ys, r, gx, bx) => CrossAssemblyDifferentialKernels.LaunchCrossAssemblyScaledKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        CrossAssemblyDifferentialKernels.CrossAssemblyScaledKernel);

    private static readonly DifferentialCase s_transitiveStruct = CreateStructCase(
        nameof(CrossAssemblyDifferentialKernels.CrossAssemblyTransitiveStructKernel),
        ["alpha_inner_v", "alpha_probe"],
        static (xs, ys, r, gx, bx) => CrossAssemblyDifferentialKernels.LaunchCrossAssemblyTransitiveStructKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        CrossAssemblyDifferentialKernels.CrossAssemblyTransitiveStructKernel);

    private static readonly DifferentialCase s_float3Functions = CreateStructCase(
        nameof(CrossAssemblyDifferentialKernels.CrossAssemblyFloat3FunctionsKernel),
        ["length3_sq", "dot3_val"],
        static (xs, ys, r, gx, bx) => CrossAssemblyDifferentialKernels.LaunchCrossAssemblyFloat3FunctionsKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        CrossAssemblyDifferentialKernels.CrossAssemblyFloat3FunctionsKernel);

    private static readonly DifferentialCase s_explicitPackedLayout = CreateStructCase(
        nameof(CrossAssemblyDifferentialKernels.CrossAssemblyExplicitPackedLayoutKernel),
        ["swap_pair_a", "packed_pair_val"],
        static (xs, ys, r, gx, bx) => CrossAssemblyDifferentialKernels.LaunchCrossAssemblyExplicitPackedLayoutKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        CrossAssemblyDifferentialKernels.CrossAssemblyExplicitPackedLayoutKernel);

    private static readonly DifferentialCase s_disposableGuard = CreateStructCase(
        nameof(CrossAssemblyDifferentialKernels.CrossAssemblyDisposableGuardKernel),
        ["disposed_lib_guard"],
        static (xs, ys, r, gx, bx) => CrossAssemblyDifferentialKernels.LaunchCrossAssemblyDisposableGuardKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        CrossAssemblyDifferentialKernels.CrossAssemblyDisposableGuardKernel);

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
    public void Test_CrossAssemblyVec2_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_vec2);

    [SkippableFact]
    public void Test_CrossAssemblyParticle_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_particle);

    [SkippableFact]
    public void Test_CrossAssemblyScaled_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_scaled);

    [SkippableFact]
    public void Test_CrossAssemblyTransitiveStruct_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_transitiveStruct);

    [SkippableFact]
    public void Test_CrossAssemblyFloat3Functions_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_float3Functions);

    [SkippableFact]
    public void Test_CrossAssemblyExplicitPackedLayout_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_explicitPackedLayout);

    [SkippableFact]
    public void Test_CrossAssemblyDisposableGuard_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_disposableGuard);

    [SkippableFact]
    public void Test_CrossAssemblyFunctionsAndMacros_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_functionsAndMacros);

    [SkippableFact]
    public void Test_CrossAssemblyExplicitLayoutArray_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_explicitLayoutArrays);

    [SkippableFact]
    public void Test_CrossAssemblyBarrierExchange_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_barrierExchange);
}
