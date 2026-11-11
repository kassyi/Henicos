#nullable enable
using System;
using Kassyi.GSharp.Core;
#if GSHARP_CUDA
using Kassyi.GSharp.Runtime;
#endif
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.MarshalingDifferentialTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for null and empty array marshaling across non-nullable,
/// nullable top-level, and struct fields.
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public sealed class MarshalingNullAndEmptyDifferentialTests
{
    private static readonly DifferentialCase s_nonNullableArrayNull = new(
        KernelName(nameof(MarshalingAliasingDifferentialKernels.NonNullableArrayKernel), "Null"))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        ExceptionPolicy = ExceptionPolicy.For<ArgumentNullException>(
            DiffPath.Managed | DiffPath.Native,
            paramName: "input"),
        Inputs = static (_, _) => new DiffBuffers()
            .InNullable<int>("input", null)
            .Out<int>("output", 1, OutputPolicy.Ignore),
        Launch = static (b, s) =>
        {
            int[]? input = b.GetNullable<int>("input");
            int[] output = b.Get<int>("output");
#pragma warning disable CS8604
            MarshalingAliasingDifferentialKernels.LaunchNonNullableArrayKernel(
                input, output, gridDimX: s.GridX, blockDimX: s.BlockX);
#pragma warning restore CS8604
        },
    };

    private static readonly DifferentialCase s_nonNullableArrayEmpty = new(
        KernelName(nameof(MarshalingAliasingDifferentialKernels.NonNullableArrayKernel), "Empty"))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        ExceptionPolicy = ExceptionPolicy.For<ArgumentException>(
            DiffPath.Managed | DiffPath.Native,
            paramName: "input"),
        Inputs = static (_, _) => new DiffBuffers()
            .In("input", Array.Empty<int>(), OutputPolicy.Ignore)
            .Out<int>("output", 1, OutputPolicy.Ignore),
        Launch = static (b, s) =>
        {
            int[] input = b.Get<int>("input");
            int[] output = b.Get<int>("output");
            MarshalingAliasingDifferentialKernels.LaunchNonNullableArrayKernel(
                input, output, gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void NullAndEmptyArrays_NonNullableNull_ThrowsArgumentNullException() =>
        DifferentialRunner.Run(s_nonNullableArrayNull);

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void NullAndEmptyArrays_NonNullableEmpty_ThrowsArgumentException() =>
        DifferentialRunner.Run(s_nonNullableArrayEmpty);

#if GSHARP_CUDA
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void NullAndEmptyArrays_NonNullableDirectLaunch_ThrowsOnCuda()
    {
        CudaCapability.EnsureGpuAvailable();
        int[] dummyOut = new int[1];
#pragma warning disable CS8625
        var nullEx = Assert.Throws<ArgumentNullException>(() =>
            MarshalingAliasingDifferentialKernels.LaunchNonNullableArrayKernel(null, dummyOut));
#pragma warning restore CS8625
        Assert.Equal("input", nullEx.ParamName);

        var emptyEx = Assert.Throws<ArgumentException>(() =>
            MarshalingAliasingDifferentialKernels.LaunchNonNullableArrayKernel(Array.Empty<int>(), dummyOut));
        Assert.Equal("input", emptyEx.ParamName);
    }
#endif

    // --- Nullable top-level array null vs empty (NullReadsAsEmpty #806, #838) ---
    private static DifferentialCase CreateNullableTopLevelCase(
        string label, int[]? optPrimInit, DiffDeepBag[]? optDeepInit)
    {
        var bagComparer = new StructElementComparer<DiffDeepBag>(CompareDeepBag);
        return new DifferentialCase(
            KernelName(nameof(MarshalingAliasingDifferentialKernels.NullableTopLevelArrayKernel), label))
        {
            Shape = LaunchShapes.SingleThread(),
            Seeds = [0],
            Inputs = (_, _) => new DiffBuffers()
                .InNullable("optPrim", optPrimInit, OutputPolicy.Exact, readOnly: true)
                .InNullable(
                    "optDeep",
                    optDeepInit,
                    OutputPolicy.ForStruct(bagComparer),
                    bagComparer,
                    readOnly: true)
                .Out<int>("output", 6, OutputPolicy.Exact),
            Oracle = static (b, _) =>
            {
                int[]? optPrim = b.GetNullable<int>("optPrim");
                DiffDeepBag[]? optDeep = b.GetNullable<DiffDeepBag>("optDeep");
                int[] output = b.Get<int>("output");
                int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
                if (tid == 0)
                {
                    // Per G# top-level nullable array contract (#806, #838),
                    // both null and Length == 0 normalize to the null/empty slice (data == null, length == 0)
                    bool primNullOrEmpty = optPrim is null || optPrim.Length == 0;
                    bool deepNullOrEmpty = optDeep is null || optDeep.Length == 0;
                    output[0] = primNullOrEmpty ? -1 : 1;
                    output[1] = primNullOrEmpty ? -1 : 1;
                    output[2] = optPrim?.Length ?? 0;
                    output[3] = deepNullOrEmpty ? -1 : 1;
                    output[4] = optDeep?.Length ?? 0;
                    output[5] = (optDeep is not null && optDeep.Length > 0 &&
                                 optDeep[0].Data is not null && optDeep[0].Data.Length > 0)
                        ? optDeep[0].Data[0] + optDeep[0].Tag
                        : -99;
                }
            },
            Launch = static (b, s) =>
            {
                int[]? optPrim = b.GetNullable<int>("optPrim");
                DiffDeepBag[]? optDeep = b.GetNullable<DiffDeepBag>("optDeep");
                int[] output = b.Get<int>("output");
                MarshalingAliasingDifferentialKernels.LaunchNullableTopLevelArrayKernel(
                    optPrim, optDeep, output, gridDimX: s.GridX, blockDimX: s.BlockX);
            },
        };
    }

    private static readonly DifferentialCase s_nullableTopLevelBothNull =
        CreateNullableTopLevelCase("both_null", null, null);
    private static readonly DifferentialCase s_nullableTopLevelBothEmpty =
        CreateNullableTopLevelCase("both_empty", Array.Empty<int>(), Array.Empty<DiffDeepBag>());
    private static readonly DifferentialCase s_nullableTopLevelPopulated =
        CreateNullableTopLevelCase("populated", [10, 20], [new DiffDeepBag { Data = [30, 40], Tag = 5 }]);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void NullAndEmptyArrays_NullableTopLevelBothNull_MatchesSequentialOracle() =>
        DifferentialRunner.Run(s_nullableTopLevelBothNull);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void NullAndEmptyArrays_NullableTopLevelBothEmpty_MatchesSequentialOracle() =>
        DifferentialRunner.Run(s_nullableTopLevelBothEmpty);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void NullAndEmptyArrays_NullableTopLevelPopulated_MatchesSequentialOracle() =>
        DifferentialRunner.Run(s_nullableTopLevelPopulated);

    // --- Struct field null array vs empty array (#706 EmptyArrayData + ArrayPool reuse) ---
    private static DiffNullableDeepBag CloneNullableBag(DiffNullableDeepBag src, DiffArrayCloneScope scope)
        => new()
        {
            Data = scope.CloneArray(src.Data),
            Fallback = src.Fallback,
        };

    private static void CompareNullableBag(StructFieldCheckContext ctx, DiffNullableDeepBag o, DiffNullableDeepBag a)
    {
        ctx.Field("Fallback", o.Fallback, a.Fallback);
        ctx.Field("DataIsNull", o.Data is null, a.Data is null);
        if (o.Data is not null && a.Data is not null)
        {
            ctx.Field("Data.Length", o.Data.Length, a.Data.Length);
            int len = Math.Min(o.Data.Length, a.Data.Length);
            for (int i = 0; i < len; i++)
            {
                ctx.Field($"Data[{i}]", o.Data[i], a.Data[i]);
            }
        }
    }

    private static DifferentialCase CreateStructFieldNullOrEmptySafeCase(
        string label, int[]? valData, int[]? refData)
    {
        return new DifferentialCase(
            KernelName(nameof(MarshalingAliasingDifferentialKernels.StructFieldNullOrEmptySafeKernel), label))
        {
            Shape = LaunchShapes.SingleThread(),
            Seeds = [0],
            Inputs = (_, _) => new DiffBuffers()
                .InDeepScalar(
                    "bagByVal",
                    new DiffNullableDeepBag { Data = valData, Fallback = 7 },
                    CloneNullableBag,
                    CompareNullableBag)
                .RefDeepScalar(
                    "bagByRef",
                    new DiffNullableDeepBag { Data = refData, Fallback = 11 },
                    CloneNullableBag,
                    CompareNullableBag)
                .Out<int>("output", 5, OutputPolicy.Exact),
            Oracle = static (b, _) =>
            {
                DiffNullableDeepBag bagByVal = b.GetScalar<DiffNullableDeepBag>("bagByVal");
                ref DiffNullableDeepBag bagByRef = ref b.GetScalarRef<DiffNullableDeepBag>("bagByRef");
                int[] output = b.Get<int>("output");
                MarshalingAliasingDifferentialKernels.StructFieldNullOrEmptySafeKernel(
                    bagByVal, ref bagByRef, output);
            },
            Launch = static (b, s) =>
            {
                DiffNullableDeepBag bagByVal = b.GetScalar<DiffNullableDeepBag>("bagByVal");
                ref DiffNullableDeepBag bagByRef = ref b.GetScalarRef<DiffNullableDeepBag>("bagByRef");
                int[] output = b.Get<int>("output");
                MarshalingAliasingDifferentialKernels.LaunchStructFieldNullOrEmptySafeKernel(
                    bagByVal, ref bagByRef, output, gridDimX: s.GridX, blockDimX: s.BlockX);
            },
        };
    }

    private static readonly DifferentialCase s_structFieldWarmupPopulated =
        CreateStructFieldNullOrEmptySafeCase("warmup_populated", [10, 20, 30], [40, 50]);
    private static readonly DifferentialCase s_structFieldNullVsEmpty =
        CreateStructFieldNullOrEmptySafeCase("null_vs_empty", null, Array.Empty<int>());
    private static readonly DifferentialCase s_structFieldEmptyVsNull =
        CreateStructFieldNullOrEmptySafeCase("empty_vs_null", Array.Empty<int>(), null);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void NullAndEmptyArrays_StructFieldWarmupPopulated_MatchesSequentialOracle() =>
        DifferentialRunner.Run(s_structFieldWarmupPopulated);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void NullAndEmptyArrays_StructFieldNullVsEmpty_MatchesSequentialOracle() =>
        DifferentialRunner.Run(s_structFieldNullVsEmpty);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void NullAndEmptyArrays_StructFieldEmptyVsNull_MatchesSequentialOracle() =>
        DifferentialRunner.Run(s_structFieldEmptyVsNull);

    // --- Struct field null array dereference throws NullReferenceException (#706) ---
    private static readonly DifferentialCase s_structFieldNullDereference = new(
        KernelName(nameof(MarshalingAliasingDifferentialKernels.StructFieldNullDereferenceKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        // Every path throws (#1409); CUDA, the only path of Tests.Cuda, also reports the place of bag.Data
        // (MarshalingAliasingDifferentialKernels.cu.cs line 502, column 25), which the managed exception lacks
#if GSHARP_CUDA
        ExceptionPolicy = new ExceptionPolicy(typeof(NullReferenceException), DiffPaths.CheckedPaths())
        {
            SourceFile = nameof(MarshalingAliasingDifferentialKernels),
            Line = 502,
            Column = 25,
        },
#else
        ExceptionPolicy = new ExceptionPolicy(typeof(NullReferenceException), DiffPaths.CheckedPaths()),
#endif
        Inputs = static (_, _) => new DiffBuffers()
            .InDeepScalar(
                "bag",
                new DiffNullableDeepBag { Data = null, Fallback = 5 },
                CloneNullableBag)
            .Out<int>("output", 1, OutputPolicy.Ignore),
        Launch = static (b, s) =>
        {
            DiffNullableDeepBag bag = b.GetScalar<DiffNullableDeepBag>("bag");
            int[] output = b.Get<int>("output");
            MarshalingAliasingDifferentialKernels.LaunchStructFieldNullDereferenceKernel(
                bag, output, gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void NullAndEmptyArrays_StructFieldNullDereference_ThrowsNullReferenceException() =>
        DifferentialRunner.Run(s_structFieldNullDereference);
}
