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
            foreach (var read in (ArrayArgumentRead[])[ArrayArgumentRead.IsNull, ArrayArgumentRead.Length, ArrayArgumentRead.ForEach])
            {
                yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.SpanKernel), valueName, read,
                    (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchSpanKernel(
                        new Span<int>(Array()), read, o, gridDimX: s.GridX, blockDimX: s.BlockX));
            }

            foreach (var read in (ArrayArgumentRead[])[ArrayArgumentRead.IsNull, ArrayArgumentRead.Length, ArrayArgumentRead.IsEmpty])
            {
                yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.MemoryKernel), valueName, read,
                    (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchMemoryKernel(
                        new Memory<int>(Array()), read, o, gridDimX: s.GridX, blockDimX: s.BlockX));
            }

            foreach (var read in (ArrayArgumentRead[])[ArrayArgumentRead.Length, ArrayArgumentRead.ForEach])
            {
                yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.SegmentKernel), valueName, read,
                    (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchSegmentKernel(
                        Segment(Array()), read, o, gridDimX: s.GridX, blockDimX: s.BlockX));
            }

            yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.SegmentForEachBarrierKernel), valueName, ArrayArgumentRead.ForEach,
                (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchSegmentForEachBarrierKernel(
                    Segment(Array()), o, gridDimX: s.GridX, blockDimX: s.BlockX),
                o => SegmentForEachModel(Segment(Array()), o));

            yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.ImmutableForEachBarrierKernel), valueName, ArrayArgumentRead.ForEach,
                (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchImmutableForEachBarrierKernel(
                    Immutable(Array()), o, gridDimX: s.GridX, blockDimX: s.BlockX),
                o => ImmutableForEachModel(Immutable(Array()), o));

            foreach (var read in (ArrayArgumentRead[])[ArrayArgumentRead.IsNull, ArrayArgumentRead.Length, ArrayArgumentRead.Element, ArrayArgumentRead.ForEach, ArrayArgumentRead.IsEmpty, ArrayArgumentRead.IsDefault,
                         ArrayArgumentRead.IsDefaultOrEmpty, ArrayArgumentRead.DefaultLiteral])
            {
                yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.ImmutableKernel), valueName, read,
                    (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchImmutableKernel(
                        Immutable(Array()), read, o, gridDimX: s.GridX, blockDimX: s.BlockX));
            }

            foreach (var read in (ArrayArgumentRead[])[ArrayArgumentRead.IsNull, ArrayArgumentRead.Length, ArrayArgumentRead.ForEach, ArrayArgumentRead.InNullableFunction])
            {
                yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.ReadOnlySpanKernel), valueName, read,
                    (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchReadOnlySpanKernel(
                        new ReadOnlySpan<int>(Array()), read, o, gridDimX: s.GridX, blockDimX: s.BlockX));
            }

            foreach (var read in (ArrayArgumentRead[])[ArrayArgumentRead.IsNull, ArrayArgumentRead.Length, ArrayArgumentRead.IsEmpty])
            {
                yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.ReadOnlyMemoryKernel), valueName, read,
                    (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchReadOnlyMemoryKernel(
                        new ReadOnlyMemory<int>(Array()), read, o, gridDimX: s.GridX, blockDimX: s.BlockX));
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
            foreach (var read in (ArrayArgumentRead[])[ArrayArgumentRead.IsNull, ArrayArgumentRead.Length])
            {
                yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.NullableMemoryKernel), valueName, read,
                    (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchNullableMemoryKernel(
                        create(), read, o, gridDimX: s.GridX, blockDimX: s.BlockX));
            }
        }
    }

    // The nullable views: null, a default value, a value over an empty array, a value over an array with elements
    private static IEnumerable<DifferentialCase> CreateNullableViewCases()
    {
        foreach (var (valueName, create) in ((string, Func<ArraySegment<int>?>)[])
                 [
                     ("Null", static () => null),
                     ("DefaultValue", static () => default(ArraySegment<int>)),
                     ("EmptyArrayValue", static () => ArraySegment<int>.Empty),
                     ("One", static () => new ArraySegment<int>([7])),
                 ])
        {
            yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.NullableSegmentKernel), valueName, ArrayArgumentRead.Length,
                (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchNullableSegmentKernel(create(), o, gridDimX: s.GridX, blockDimX: s.BlockX));
        }

        foreach (var (valueName, create) in ((string, Func<ImmutableArray<int>?>)[])
                 [
                     ("Null", static () => null),
                     ("DefaultValue", static () => default(ImmutableArray<int>)),
                     ("EmptyArrayValue", static () => ImmutableArray<int>.Empty),
                     ("One", static () => ImmutableArray.Create(7)),
                 ])
        {
            yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.NullableImmutableKernel), valueName, ArrayArgumentRead.Length,
                (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchNullableImmutableKernel(create(), o, gridDimX: s.GridX, blockDimX: s.BlockX));
        }

        // Each of the two arguments in turn; the other holds a value of length 1 or is null
        foreach (var (valueName, create) in ((string, Func<Memory<int>?>)[])
                 [
                     ("Null", static () => null),
                     ("DefaultValue", static () => default(Memory<int>)),
                     ("Two", static () => new Memory<int>([7, 8])),
                 ])
        {
            yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.TwoNullableMemoryKernel), "First" + valueName, ArrayArgumentRead.Length,
                (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchTwoNullableMemoryKernel(
                    create(), new Memory<int>([1]), o, gridDimX: s.GridX, blockDimX: s.BlockX));
            yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.TwoNullableMemoryKernel), "Second" + valueName, ArrayArgumentRead.Length,
                (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchTwoNullableMemoryKernel(
                    null, create(), o, gridDimX: s.GridX, blockDimX: s.BlockX));
        }
    }

    // The body of SegmentForEachBarrierKernel for its one thread without the barrier, which the C# execution cannot
    // run: a default segment throws InvalidOperationException when the loop starts
    private static void SegmentForEachModel(ArraySegment<int> g, int[] o)
    {
        o[0] = 100;
        foreach (var v in g)
        {
            o[0] += v + 1;
        }
    }

    // The body of ImmutableForEachBarrierKernel for its one thread without the barrier: a default ImmutableArray<T>
    // throws NullReferenceException when the loop starts
    private static void ImmutableForEachModel(ImmutableArray<int> g, int[] o)
    {
        o[0] = 100;
        foreach (var v in g)
        {
            o[0] += v + 1;
        }
    }

    // null: default (no array); []: ArraySegment<int>.Empty (an empty array)
    private static ArraySegment<int> Segment(int[]? array) => array is null ? default : new ArraySegment<int>(array);

    // null: default (IsDefault); []: ImmutableArray<int>.Empty
    private static ImmutableArray<int> Immutable(int[]? array) =>
        array is null ? default : ImmutableArray.Create(array);
}
