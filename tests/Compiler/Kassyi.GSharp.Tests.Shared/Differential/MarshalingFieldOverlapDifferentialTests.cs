#nullable enable
using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Interop;
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
    private static string FieldKernelName(string kernelMemberName, string? suffix = null) =>
        DiffKernelNames.Of(typeof(MarshalingFieldOverlapDifferentialKernels), kernelMemberName, suffix);

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

    internal static readonly DifferentialCase s_outConvertedStructAndSlice = new(
        FieldKernelName(nameof(MarshalingFieldOverlapDifferentialKernels.OutConvertedStructAndSliceKernel)))
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
            var holder = new DiffPaddedHolder { Items = items };
            MarshalingFieldOverlapDifferentialKernels.LaunchOutConvertedStructAndSliceKernel(
                out holder, items.AsSpan(1, 2), b.Get<float>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

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
    public void FieldOverlap_OutStructThatHeldConvertedElementsOfTheSlice_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_outConvertedStructAndSlice);

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

    internal static readonly DifferentialCase s_memoryFieldAndSlice = BackedCase(
        nameof(MarshalingFieldOverlapDifferentialKernels.MemoryFieldAndSliceKernel), 2,
        static (backing, observed, s) =>
        {
            var bag = new DiffMemoryBag { Data = backing, Tag = 1 };
            MarshalingFieldOverlapDifferentialKernels.LaunchMemoryFieldAndSliceKernel(
                bag, backing.AsSpan(1, 2), observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        });

    internal static readonly DifferentialCase s_segmentFieldAndSlice = BackedCase(
        nameof(MarshalingFieldOverlapDifferentialKernels.SegmentFieldAndSliceKernel), 2,
        static (backing, observed, s) =>
        {
            var bag = new DiffSegmentBag { Data = new ArraySegment<int>(backing), Tag = 1 };
            MarshalingFieldOverlapDifferentialKernels.LaunchSegmentFieldAndSliceKernel(
                bag, backing.AsSpan(1, 2), observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        });

    internal static readonly DifferentialCase s_readOnlyMemoryFieldAndSlice = BackedCase(
        nameof(MarshalingFieldOverlapDifferentialKernels.ReadOnlyMemoryFieldAndSliceKernel), 1,
        static (backing, observed, s) =>
        {
            var bag = new DiffReadOnlyMemoryBag { Data = backing, Tag = 1 };
            MarshalingFieldOverlapDifferentialKernels.LaunchReadOnlyMemoryFieldAndSliceKernel(
                bag, backing.AsSpan(1, 2), observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        });

    internal static readonly DifferentialCase s_immutableArrayFieldAndReadOnlySlice = BackedCase(
        nameof(MarshalingFieldOverlapDifferentialKernels.ImmutableArrayFieldAndReadOnlySliceKernel), 2,
        static (backing, observed, s) =>
        {
            var bag = new DiffImmutableArrayBag
            {
                Data = ImmutableCollectionsMarshal.AsImmutableArray(backing),
                Tag = 1,
            };
            MarshalingFieldOverlapDifferentialKernels.LaunchImmutableArrayFieldAndReadOnlySliceKernel(
                bag, backing.AsSpan(1, 2), backing.AsSpan(1, 2), observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        });

    internal static readonly DifferentialCase s_twoMemoryFields = BackedCase(
        nameof(MarshalingFieldOverlapDifferentialKernels.TwoMemoryFieldsKernel), 2,
        static (backing, observed, s) =>
        {
            var views = new DiffTwoMemoryViews { A = backing.AsMemory(0, 3), B = backing.AsMemory(1, 3) };
            MarshalingFieldOverlapDifferentialKernels.LaunchTwoMemoryFieldsKernel(
                views, observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        });

    internal static readonly DifferentialCase s_structWithTwoViewsAlone = new(
        FieldKernelName(nameof(MarshalingFieldOverlapDifferentialKernels.StructWithTwoViewsAloneKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(4), OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            var views = new DiffTwoMemoryViews { A = backing.AsMemory(0, 3), B = backing.AsMemory(1, 3) };
            MarshalingFieldOverlapDifferentialKernels.LaunchStructWithTwoViewsAloneKernel(
                views, gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_arrayFieldAndMemoryField = BackedCase(
        nameof(MarshalingFieldOverlapDifferentialKernels.ArrayFieldAndMemoryFieldKernel), 2,
        static (backing, observed, s) =>
        {
            var bag = new DiffDeepBag { Data = backing, Tag = 1 };
            var view = new DiffMemoryBag { Data = backing.AsMemory(1, 2), Tag = 2 };
            MarshalingFieldOverlapDifferentialKernels.LaunchArrayFieldAndMemoryFieldKernel(
                bag, view, observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        });

    internal static readonly DifferentialCase s_memoryFieldAndMemoryArgument = BackedCase(
        nameof(MarshalingFieldOverlapDifferentialKernels.MemoryFieldAndMemoryArgumentKernel), 2,
        static (backing, observed, s) =>
        {
            var bag = new DiffMemoryBag { Data = backing.AsMemory(1, 2), Tag = 1 };
            MarshalingFieldOverlapDifferentialKernels.LaunchMemoryFieldAndMemoryArgumentKernel(
                bag, backing.AsMemory(), observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        });

    internal static readonly DifferentialCase s_memoryFieldAndWholeArray = BackedCase(
        nameof(MarshalingFieldOverlapDifferentialKernels.MemoryFieldAndWholeArrayKernel), 2,
        static (backing, observed, s) =>
        {
            var bag = new DiffMemoryBag { Data = backing.AsMemory(1, 2), Tag = 1 };
            MarshalingFieldOverlapDifferentialKernels.LaunchMemoryFieldAndWholeArrayKernel(
                bag, backing, observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        });

    internal static readonly DifferentialCase s_elementMemoryFieldAndSlice = BackedCase(
        nameof(MarshalingFieldOverlapDifferentialKernels.ElementMemoryFieldAndSliceKernel), 2,
        static (backing, observed, s) =>
        {
            DiffMemoryBag[] bags = [new DiffMemoryBag { Data = backing, Tag = 1 }];
            MarshalingFieldOverlapDifferentialKernels.LaunchElementMemoryFieldAndSliceKernel(
                bags, backing.AsSpan(1, 2), observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        });

    internal static readonly DifferentialCase s_inElementMemoryFieldAndInSlice = new(
        FieldKernelName(nameof(MarshalingFieldOverlapDifferentialKernels.InElementMemoryFieldAndInSliceKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(4), OutputPolicy.Exact, readOnly: true)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        Reference = static (b, _) =>
        {
            b.Get<int>("observed")[0] = 12;
            b.Get<int>("observed")[1] = 103;
        },
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            DiffMemoryBag[] bags = [new DiffMemoryBag { Data = backing, Tag = 1 }];
            MarshalingFieldOverlapDifferentialKernels.LaunchInElementMemoryFieldAndInSliceKernel(
                bags, backing.AsSpan(1, 2), b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_nullableMemoryFieldAndSlice = BackedCase(
        nameof(MarshalingFieldOverlapDifferentialKernels.NullableMemoryFieldAndSliceKernel), 2,
        static (backing, observed, s) =>
        {
            var bag = new DiffNullableMemoryBag { Data = backing.AsMemory(), Tag = 1 };
            MarshalingFieldOverlapDifferentialKernels.LaunchNullableMemoryFieldAndSliceKernel(
                bag, backing.AsSpan(1, 2), observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        });

    internal static readonly DifferentialCase s_nestedMemoryFieldAndSlice = BackedCase(
        nameof(MarshalingFieldOverlapDifferentialKernels.NestedMemoryFieldAndSliceKernel), 2,
        static (backing, observed, s) =>
        {
            var outer = new DiffNestedMemoryBag
            {
                Inner = new DiffMemoryBag { Data = backing, Tag = 1 },
                Scale = 2,
            };
            MarshalingFieldOverlapDifferentialKernels.LaunchNestedMemoryFieldAndSliceKernel(
                outer, backing.AsSpan(1, 2), observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        });

    internal static readonly DifferentialCase s_twoMemoryFieldsSeparateHalves = BackedCase(
        nameof(MarshalingFieldOverlapDifferentialKernels.TwoMemoryFieldsSeparateHalvesKernel), 2,
        static (backing, observed, s) =>
        {
            var views = new DiffTwoMemoryViews { A = backing.AsMemory(0, 2), B = backing.AsMemory(2, 2) };
            MarshalingFieldOverlapDifferentialKernels.LaunchTwoMemoryFieldsSeparateHalvesKernel(
                views, observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        });

    internal static readonly DifferentialCase s_threeViewsChained = new(
        FieldKernelName(nameof(MarshalingFieldOverlapDifferentialKernels.ThreeViewsChainedKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(6), OutputPolicy.Exact)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            var views = new DiffTwoMemoryViews { A = backing.AsMemory(0, 2), B = backing.AsMemory(4, 2) };
            MarshalingFieldOverlapDifferentialKernels.LaunchThreeViewsChainedKernel(
                views, backing.AsSpan(1, 4), b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_identicalMemoryFields = new(
        FieldKernelName(nameof(MarshalingFieldOverlapDifferentialKernels.TwoMemoryFieldsKernel), "Identical"))
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
            var views = new DiffTwoMemoryViews { A = backing.AsMemory(0, 3), B = backing.AsMemory(0, 3) };
            MarshalingFieldOverlapDifferentialKernels.LaunchTwoMemoryFieldsKernel(
                views, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_inElementsWithIdenticalMemory = new(
        FieldKernelName(nameof(MarshalingFieldOverlapDifferentialKernels.InElementsWithIdenticalMemoryKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(4), OutputPolicy.Exact, readOnly: true)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        Reference = static (b, _) =>
        {
            b.Get<int>("observed")[0] = 12;
            b.Get<int>("observed")[1] = 103;
        },
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            DiffMemoryBag[] bags =
            [
                new DiffMemoryBag { Data = backing.AsMemory(0, 3), Tag = 1 },
                new DiffMemoryBag { Data = backing.AsMemory(0, 3), Tag = 2 },
            ];
            MarshalingFieldOverlapDifferentialKernels.LaunchInElementsWithIdenticalMemoryKernel(
                bags, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_MemoryFieldAndASliceOfItsArray_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_memoryFieldAndSlice);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_ArraySegmentFieldAndASliceOfItsArray_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_segmentFieldAndSlice);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_ReadOnlyMemoryFieldAndAWritableSlice_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_readOnlyMemoryFieldAndSlice);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_ImmutableArrayFieldAndAReadOnlySlice_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_immutableArrayFieldAndReadOnlySlice);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_TwoMemoryFieldsOfOneStruct_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_twoMemoryFields);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_StructWithTwoViewsAlone_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_structWithTwoViewsAlone);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_ArrayFieldAndAMemoryFieldOfAnotherStruct_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_arrayFieldAndMemoryField);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_MemoryFieldAndAMemoryArgument_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_memoryFieldAndMemoryArgument);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_MemoryFieldAndTheWholeArray_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_memoryFieldAndWholeArray);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_ElementMemoryFieldAndASlice_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_elementMemoryFieldAndSlice);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_InElementMemoryFieldAndAnInSlice_SeeEachOthersWritesOnEveryPath() =>
        DifferentialRunner.Run(s_inElementMemoryFieldAndInSlice);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_NullableMemoryFieldAndASlice_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullableMemoryFieldAndSlice);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_NestedStructMemoryFieldAndASlice_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nestedMemoryFieldAndSlice);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_TwoMemoryFieldsOverSeparateHalves_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_twoMemoryFieldsSeparateHalves);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_ThreeViewsChained_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_threeViewsChained);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_IdenticalMemoryFieldsOfOneStruct_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_identicalMemoryFields);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_InElementsWithIdenticalMemory_SeeEachOthersWritesOnEveryPath() =>
        DifferentialRunner.Run(s_inElementsWithIdenticalMemory);

    internal static readonly DifferentialCase s_inShortMemoryFieldAndInIntSegment = new(
        FieldKernelName(nameof(MarshalingFieldOverlapDifferentialKernels.InShortMemoryFieldAndInIntSegmentKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(4), OutputPolicy.Exact, readOnly: true)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        Reference = static (b, _) =>
        {
            b.Get<int>("observed")[0] = 12;
            b.Get<int>("observed")[1] = 103;
        },
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            var manager = new HostAliasMemoryManager<short>(backing, 0, backing.Length * 2);
            DiffShortMemoryBag[] mems = [new DiffShortMemoryBag { Data = manager.Memory, Tag = 1 }];
            DiffSegmentBag[] segs = [new DiffSegmentBag { Data = new ArraySegment<int>(backing), Tag = 2 }];
            MarshalingFieldOverlapDifferentialKernels.LaunchInShortMemoryFieldAndInIntSegmentKernel(
                mems, segs, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_inShortMemoryFieldAndInIntArrayCandidate = new(
        FieldKernelName(
            nameof(MarshalingFieldOverlapDifferentialKernels.InShortMemoryFieldAndInIntArrayCandidateKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(4), OutputPolicy.Exact, readOnly: true)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        Reference = static (b, _) =>
        {
            b.Get<int>("observed")[0] = 12;
            b.Get<int>("observed")[1] = 103;
        },
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            var manager = new HostAliasMemoryManager<short>(backing, 0, backing.Length * 2);
            DiffShortMemoryBag[] mems = [new DiffShortMemoryBag { Data = manager.Memory, Tag = 1 }];
            MarshalingFieldOverlapDifferentialKernels.LaunchInShortMemoryFieldAndInIntArrayCandidateKernel(
                mems, backing, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_inImmutableFieldAndInShortMemoryField = new(
        FieldKernelName(
            nameof(MarshalingFieldOverlapDifferentialKernels.InImmutableFieldAndInShortMemoryFieldKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(4), OutputPolicy.Exact, readOnly: true)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        Reference = static (b, _) =>
        {
            b.Get<int>("observed")[0] = 12;
            b.Get<int>("observed")[1] = 103;
        },
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            var manager = new HostAliasMemoryManager<short>(backing, 0, backing.Length * 2);
            DiffShortMemoryBag[] mems = [new DiffShortMemoryBag { Data = manager.Memory, Tag = 1 }];
            DiffImmutableArrayBag[] imms =
            [
                new DiffImmutableArrayBag
                {
                    Data = ImmutableCollectionsMarshal.AsImmutableArray(backing),
                    Tag = 2,
                },
            ];
            MarshalingFieldOverlapDifferentialKernels.LaunchInImmutableFieldAndInShortMemoryFieldKernel(
                mems, imms, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_inReadOnlyMemoryOfBagsAndInSlice = new(
        FieldKernelName(nameof(MarshalingFieldOverlapDifferentialKernels.InReadOnlyMemoryOfBagsAndInSliceKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(4), OutputPolicy.Exact, readOnly: true)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        Reference = static (b, _) =>
        {
            b.Get<int>("observed")[0] = 12;
            b.Get<int>("observed")[1] = 103;
        },
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            DiffReadOnlyMemoryOfBagsHolder[] holders =
            [
                new DiffReadOnlyMemoryOfBagsHolder
                {
                    Bags = new[] { new DiffMemoryBag { Data = backing, Tag = 1 } }.AsMemory(),
                    Tag = 2,
                },
            ];
            MarshalingFieldOverlapDifferentialKernels.LaunchInReadOnlyMemoryOfBagsAndInSliceKernel(
                holders, backing.AsSpan(1, 2), b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_inImmutableArrayOfBagsAndInSlice = new(
        FieldKernelName(nameof(MarshalingFieldOverlapDifferentialKernels.InImmutableArrayOfBagsAndInSliceKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(4), OutputPolicy.Exact, readOnly: true)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        Reference = static (b, _) =>
        {
            b.Get<int>("observed")[0] = 12;
            b.Get<int>("observed")[1] = 103;
        },
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            DiffImmutableArrayOfBagsHolder[] holders =
            [
                new DiffImmutableArrayOfBagsHolder
                {
                    Bags = ImmutableArray.Create(new DiffMemoryBag { Data = backing, Tag = 1 }),
                    Tag = 2,
                },
            ];
            MarshalingFieldOverlapDifferentialKernels.LaunchInImmutableArrayOfBagsAndInSliceKernel(
                holders, backing.AsSpan(1, 2), b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_InShortMemoryFieldBeforeInIntSegment_SeeEachOthersWritesOnEveryPath() =>
        DifferentialRunner.Run(s_inShortMemoryFieldAndInIntSegment);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_InShortMemoryFieldBeforeInIntArrayCandidate_SeeEachOthersWritesOnEveryPath() =>
        DifferentialRunner.Run(s_inShortMemoryFieldAndInIntArrayCandidate);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_InShortMemoryFieldBeforeInImmutableField_SeeEachOthersWritesOnEveryPath() =>
        DifferentialRunner.Run(s_inImmutableFieldAndInShortMemoryField);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_InReadOnlyMemoryOfBagsAndInSlice_SeeEachOthersWritesOnEveryPath() =>
        DifferentialRunner.Run(s_inReadOnlyMemoryOfBagsAndInSlice);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FieldOverlap_InImmutableArrayOfBagsAndInSlice_SeeEachOthersWritesOnEveryPath() =>
        DifferentialRunner.Run(s_inImmutableArrayOfBagsAndInSlice);
}
