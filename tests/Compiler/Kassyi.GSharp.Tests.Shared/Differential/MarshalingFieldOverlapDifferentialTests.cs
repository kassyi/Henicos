#nullable enable
using System;
using System.Collections.Immutable;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for an array that a struct argument holds in a field and a slice of the same array: one memory,
/// as in C#. Each form of the struct argument (by value, <c>in</c>, <c>ref</c>, <c>out</c>, nullable, an element of an
/// array or of a read-only container) is one kernel, run on the managed path, the native CPU and CUDA against C#.
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public sealed class MarshalingFieldOverlapDifferentialTests
{
    private static string FieldKernelName(string kernelMemberName) =>
        DiffKernelNames.Of(typeof(MarshalingFieldOverlapDifferentialKernels), kernelMemberName);

    private static int[] Backing(int length)
    {
        var values = new int[length];
        for (var i = 0; i < length; i++)
        {
            values[i] = i + 1;
        }

        return values;
    }

    // A case whose kernel reads and writes the four-element backing array through a struct and the slice [1, 3) of it,
    // and is its own oracle
    private static DifferentialCase BackedCase(string kernelMemberName, int observedCount,
        Action<int[], int[], LaunchShape> launch) =>
        new(FieldKernelName(kernelMemberName))
        {
            Shape = LaunchShapes.SingleThread(),
            Seeds = [0],
            Inputs = (_, _) => new DiffBuffers()
                .In("backing", Backing(4), OutputPolicy.Exact)
                .Out<int>("observed", observedCount, OutputPolicy.Exact),
            OracleIsLaunch = true,
            Launch = (b, s) => launch(b.Get<int>("backing"), b.Get<int>("observed"), s),
        };

    // The array that the struct holds is the array that the span slices: one memory, as in C#
    internal static readonly DifferentialCase s_fieldAndSlice = new(
        FieldKernelName(nameof(MarshalingFieldOverlapDifferentialKernels.FieldAndSliceKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(4), OutputPolicy.Exact)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            var bag = new DiffDeepBag { Data = backing, Tag = 1 };
            MarshalingFieldOverlapDifferentialKernels.LaunchFieldAndSliceKernel(
                bag, backing.AsSpan(1, 2), b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_elementFieldAndSlice = new(
        FieldKernelName(nameof(MarshalingFieldOverlapDifferentialKernels.ElementFieldAndSliceKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(4), OutputPolicy.Exact)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            DiffDeepBag[] bags = [new DiffDeepBag { Data = backing, Tag = 1 }];
            MarshalingFieldOverlapDifferentialKernels.LaunchElementFieldAndSliceKernel(
                bags, backing.AsSpan(1, 2), b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_refFieldAndReadOnlySlice = new(
        FieldKernelName(nameof(MarshalingFieldOverlapDifferentialKernels.RefFieldAndReadOnlySliceKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(4), OutputPolicy.Exact)
            .Out<int>("observed", 1, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            var bag = new DiffDeepBag { Data = backing, Tag = 1 };
            MarshalingFieldOverlapDifferentialKernels.LaunchRefFieldAndReadOnlySliceKernel(
                ref bag, backing.AsSpan(1, 2), b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_StructFieldArrayAndASliceOfIt_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_fieldAndSlice);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_ElementFieldArrayAndASliceOfIt_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_elementFieldAndSlice);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_RefStructFieldArrayAndAReadOnlySliceOfIt_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_refFieldAndReadOnlySlice);

    internal static readonly DifferentialCase s_inFieldAndInSlice = new(
        FieldKernelName(nameof(MarshalingFieldOverlapDifferentialKernels.InFieldAndInSliceKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(4), OutputPolicy.Exact, readOnly: true)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        Reference = static (b, _) =>
        {
            // One array for both holders: the write through the field is read through the slice, and the other way
            b.Get<int>("observed")[0] = 12;
            b.Get<int>("observed")[1] = 103;
        },
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            DiffDeepBag[] bags = [new DiffDeepBag { Data = backing, Tag = 1 }];
            MarshalingFieldOverlapDifferentialKernels.LaunchInFieldAndInSliceKernel(
                bags, backing.AsSpan(1, 2), b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_convertedFieldAndSlice = new(
        FieldKernelName(nameof(MarshalingFieldOverlapDifferentialKernels.ConvertedFieldAndSliceKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .Out<float>("observed", 2, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            DiffVec2Padded[] items =
            [
                new DiffVec2Padded { A = 1f },
                new DiffVec2Padded { A = 2f },
                new DiffVec2Padded { A = 3f },
                new DiffVec2Padded { A = 4f }
            ];
            MarshalingFieldOverlapDifferentialKernels.LaunchConvertedFieldAndSliceKernel(
                new DiffPaddedHolder { Items = items },
                items.AsSpan(1, 2),
                b.Get<float>("observed"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX);
        },
    };

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_InStructFieldArrayAndAnInSliceOfIt_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_inFieldAndInSlice);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_ConvertedElementFieldArrayAndASliceOfIt_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_convertedFieldAndSlice);

    internal static readonly DifferentialCase s_inStructAndSlice = BackedCase(
        nameof(MarshalingFieldOverlapDifferentialKernels.InStructAndSliceKernel), 2,
        static (backing, observed, s) =>
        {
            var bag = new DiffDeepBag { Data = backing, Tag = 1 };
            MarshalingFieldOverlapDifferentialKernels.LaunchInStructAndSliceKernel(
                bag, backing.AsSpan(1, 2), observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        });

    internal static readonly DifferentialCase s_nullableStructAndSlice = BackedCase(
        nameof(MarshalingFieldOverlapDifferentialKernels.NullableStructAndSliceKernel), 2,
        static (backing, observed, s) =>
        {
            DiffDeepBag? bag = new DiffDeepBag { Data = backing, Tag = 1 };
            MarshalingFieldOverlapDifferentialKernels.LaunchNullableStructAndSliceKernel(
                bag, backing.AsSpan(1, 2), observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        });

    internal static readonly DifferentialCase s_outStructAndSlice = BackedCase(
        nameof(MarshalingFieldOverlapDifferentialKernels.OutStructAndSliceKernel), 2,
        static (backing, observed, s) =>
        {
            var bag = new DiffDeepBag { Data = backing, Tag = 5 };
            MarshalingFieldOverlapDifferentialKernels.LaunchOutStructAndSliceKernel(
                out bag, backing.AsSpan(1, 2), observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        });

    internal static readonly DifferentialCase s_readOnlySpanElementsAndSlice = BackedCase(
        nameof(MarshalingFieldOverlapDifferentialKernels.ReadOnlySpanElementsAndSliceKernel), 2,
        static (backing, observed, s) =>
        {
            DiffDeepBag[] bags = [new DiffDeepBag { Data = backing, Tag = 1 }];
            MarshalingFieldOverlapDifferentialKernels.LaunchReadOnlySpanElementsAndSliceKernel(
                bags, backing.AsSpan(1, 2), observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        });

    internal static readonly DifferentialCase s_immutableArrayElementsAndSlice = BackedCase(
        nameof(MarshalingFieldOverlapDifferentialKernels.ImmutableArrayElementsAndSliceKernel), 2,
        static (backing, observed, s) =>
        {
            var bags = ImmutableArray.Create(new DiffDeepBag { Data = backing, Tag = 1 });
            MarshalingFieldOverlapDifferentialKernels.LaunchImmutableArrayElementsAndSliceKernel(
                bags, backing.AsSpan(1, 2), observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        });

    internal static readonly DifferentialCase s_readOnlyMemoryElementsAndSlice = BackedCase(
        nameof(MarshalingFieldOverlapDifferentialKernels.ReadOnlyMemoryElementsAndSliceKernel), 2,
        static (backing, observed, s) =>
        {
            DiffDeepBag[] bags = [new DiffDeepBag { Data = backing, Tag = 1 }];
            MarshalingFieldOverlapDifferentialKernels.LaunchReadOnlyMemoryElementsAndSliceKernel(
                bags.AsMemory(), backing.AsSpan(1, 2), observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        });

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_InParameterStructFieldArrayAndASliceOfIt_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_inStructAndSlice);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_NullableStructFieldArrayAndASliceOfIt_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullableStructAndSlice);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_OutStructThatHeldTheSliceArray_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_outStructAndSlice);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_ReadOnlySpanOfStructsAndASliceOfTheirArray_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_readOnlySpanElementsAndSlice);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_ImmutableArrayOfStructsAndASliceOfTheirArray_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_immutableArrayElementsAndSlice);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_ReadOnlyMemoryOfStructsAndASliceOfTheirArray_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_readOnlyMemoryElementsAndSlice);
}
