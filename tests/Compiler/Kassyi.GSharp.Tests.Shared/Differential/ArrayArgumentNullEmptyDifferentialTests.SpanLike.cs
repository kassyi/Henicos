#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

using K = Kassyi.GSharp.Tests.Shared.Differential.ArrayArgumentNullEmptyDifferentialKernels;
using R = Kassyi.GSharp.Tests.Shared.Differential.ArrayArgumentReads;

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
            foreach (int read in (int[])[R.IsNull, R.Length, R.ForEach])
            {
                yield return Create(nameof(K.SpanKernel), valueName, read,
                    (o, s) => K.LaunchSpanKernel(
                        new Span<int>(Array()), read, o, gridDimX: s.GridX, blockDimX: s.BlockX),
                    o => K.SpanKernel(new Span<int>(Array()), read, o));
            }

            foreach (int read in (int[])[R.IsNull, R.Length])
            {
                yield return Create(nameof(K.MemoryKernel), valueName, read,
                    (o, s) => K.LaunchMemoryKernel(
                        new Memory<int>(Array()), read, o, gridDimX: s.GridX, blockDimX: s.BlockX),
                    o => K.MemoryKernel(new Memory<int>(Array()), read, o));
                if (read == R.Length)
                {
                    yield return Create(nameof(K.SegmentKernel), valueName, read,
                        (o, s) => K.LaunchSegmentKernel(
                            Segment(Array()), read, o, gridDimX: s.GridX, blockDimX: s.BlockX),
                        o => K.SegmentKernel(Segment(Array()), read, o));
                }
            }

            // ImmutableArray<T>.Length has no device form yet: the element and foreach reads stand for it
            foreach (int read in (int[])[R.IsNull, R.Element, R.ForEach])
            {
                yield return Create(nameof(K.ImmutableKernel), valueName, read,
                    (o, s) => K.LaunchImmutableKernel(
                        Immutable(Array()), read, o, gridDimX: s.GridX, blockDimX: s.BlockX),
                    o => K.ImmutableKernel(Immutable(Array()), read, o));
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
            foreach (int read in (int[])[R.IsNull, R.Length])
            {
                yield return Create(nameof(K.NullableMemoryKernel), valueName, read,
                    (o, s) => K.LaunchNullableMemoryKernel(
                        create(), read, o, gridDimX: s.GridX, blockDimX: s.BlockX),
                    o => K.NullableMemoryKernel(create(), read, o));
            }
        }
    }

    // null: default (no array); []: ArraySegment<int>.Empty (an empty array)
    private static ArraySegment<int> Segment(int[]? array) => array is null ? default : new ArraySegment<int>(array);

    // null: default (IsDefault); []: ImmutableArray<int>.Empty
    private static ImmutableArray<int> Immutable(int[]? array) =>
        array is null ? default : ImmutableArray.Create(array);
}
