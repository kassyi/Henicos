#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public sealed partial class ArrayArgumentNullEmptyDifferentialTests
{
    private static IEnumerable<DifferentialCase> CreateSpanLikeCases()
    {
        // A span-like value: default has no array, one over an empty array has one (an empty array, ArraySegment.Empty,
        // ImmutableArray.Empty); Memory<T>.Empty is default
        foreach (var (valueName, create) in s_arrayValues)
        {
            int[]? Array() => create();
            foreach (int read in (int[])[ArrayArgumentReads.IsNull, ArrayArgumentReads.Length, ArrayArgumentReads.ForEach])
            {
                yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.SpanKernel), valueName, read,
                    (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchSpanKernel(
                        new Span<int>(Array()), read, o, gridDimX: s.GridX, blockDimX: s.BlockX),
                    o => ArrayArgumentNullEmptyDifferentialKernels.SpanKernel(new Span<int>(Array()), read, o));
            }

            foreach (int read in (int[])[ArrayArgumentReads.IsNull, ArrayArgumentReads.Length])
            {
                yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.MemoryKernel), valueName, read,
                    (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchMemoryKernel(
                        new Memory<int>(Array()), read, o, gridDimX: s.GridX, blockDimX: s.BlockX),
                    o => ArrayArgumentNullEmptyDifferentialKernels.MemoryKernel(new Memory<int>(Array()), read, o));
                if (read == ArrayArgumentReads.Length)
                {
                    yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.SegmentKernel), valueName, read,
                        (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchSegmentKernel(
                            Segment(Array()), read, o, gridDimX: s.GridX, blockDimX: s.BlockX),
                        o => ArrayArgumentNullEmptyDifferentialKernels.SegmentKernel(Segment(Array()), read, o));
                }
            }

            // ImmutableArray<T>.Length has no device form yet: the element and foreach reads stand for it
            foreach (int read in (int[])[ArrayArgumentReads.IsNull, ArrayArgumentReads.Element, ArrayArgumentReads.ForEach])
            {
                yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.ImmutableKernel), valueName, read,
                    (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchImmutableKernel(
                        Immutable(Array()), read, o, gridDimX: s.GridX, blockDimX: s.BlockX),
                    o => ArrayArgumentNullEmptyDifferentialKernels.ImmutableKernel(Immutable(Array()), read, o));
            }
        }

        foreach (var (valueName, create) in ((string, Func<Memory<int>?>)[])
                 [
                     ("Null", static () => null),
                     ("DefaultValue", static () => default(Memory<int>)),
                     ("EmptyArrayValue", static () => new Memory<int>([])),
                     ("One", static () => new Memory<int>([7])),
                 ])
        {
            foreach (int read in (int[])[ArrayArgumentReads.IsNull, ArrayArgumentReads.Length])
            {
                yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.NullableMemoryKernel), valueName, read,
                    (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchNullableMemoryKernel(
                        create(), read, o, gridDimX: s.GridX, blockDimX: s.BlockX),
                    o => ArrayArgumentNullEmptyDifferentialKernels.NullableMemoryKernel(create(), read, o));
            }
        }
    }

    // null: default (no array); []: ArraySegment<int>.Empty (an empty array)
    private static ArraySegment<int> Segment(int[]? array) => array is null ? default : new ArraySegment<int>(array);

    // null: default (IsDefault); []: ImmutableArray<int>.Empty
    private static ImmutableArray<int> Immutable(int[]? array) =>
        array is null ? default : ImmutableArray.Create(array);
}
