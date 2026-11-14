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
                        new Span<int>(Array()), read, o, gridDimX: s.GridX, blockDimX: s.BlockX));
            }

            foreach (int read in (int[])[R.IsNull, R.Length])
            {
                yield return Create(nameof(K.MemoryKernel), valueName, read,
                    (o, s) => K.LaunchMemoryKernel(
                        new Memory<int>(Array()), read, o, gridDimX: s.GridX, blockDimX: s.BlockX));
            }

            foreach (int read in (int[])[R.Length, R.ForEach])
            {
                yield return Create(nameof(K.SegmentKernel), valueName, read,
                    (o, s) => K.LaunchSegmentKernel(
                        Segment(Array()), read, o, gridDimX: s.GridX, blockDimX: s.BlockX));
            }

            foreach (int read in (int[])[R.IsNull, R.Length, R.Element, R.ForEach])
            {
                yield return Create(nameof(K.ImmutableKernel), valueName, read,
                    (o, s) => K.LaunchImmutableKernel(
                        Immutable(Array()), read, o, gridDimX: s.GridX, blockDimX: s.BlockX));
            }

            foreach (int read in (int[])[R.IsNull, R.Length, R.ForEach, R.InNullableFunction])
            {
                yield return Create(nameof(K.ReadOnlySpanKernel), valueName, read,
                    (o, s) => K.LaunchReadOnlySpanKernel(
                        new ReadOnlySpan<int>(Array()), read, o, gridDimX: s.GridX, blockDimX: s.BlockX));
            }

            foreach (int read in (int[])[R.IsNull, R.Length])
            {
                yield return Create(nameof(K.ReadOnlyMemoryKernel), valueName, read,
                    (o, s) => K.LaunchReadOnlyMemoryKernel(
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
            foreach (int read in (int[])[R.IsNull, R.Length])
            {
                yield return Create(nameof(K.NullableMemoryKernel), valueName, read,
                    (o, s) => K.LaunchNullableMemoryKernel(
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
            yield return Create(nameof(K.NullableSegmentKernel), valueName, R.Length,
                (o, s) => K.LaunchNullableSegmentKernel(create(), o, gridDimX: s.GridX, blockDimX: s.BlockX));
        }

        foreach (var (valueName, create) in ((string, Func<ImmutableArray<int>?>)[])
                 [
                     ("Null", static () => null),
                     ("DefaultValue", static () => default(ImmutableArray<int>)),
                     ("EmptyArrayValue", static () => ImmutableArray<int>.Empty),
                     ("One", static () => ImmutableArray.Create(7)),
                 ])
        {
            yield return Create(nameof(K.NullableImmutableKernel), valueName, R.Length,
                (o, s) => K.LaunchNullableImmutableKernel(create(), o, gridDimX: s.GridX, blockDimX: s.BlockX));
        }

        // Each of the two arguments in turn; the other holds a value of length 1 or is null
        foreach (var (valueName, create) in ((string, Func<Memory<int>?>)[])
                 [
                     ("Null", static () => null),
                     ("DefaultValue", static () => default(Memory<int>)),
                     ("Two", static () => new Memory<int>([7, 8])),
                 ])
        {
            yield return Create(nameof(K.TwoNullableMemoryKernel), "First" + valueName, R.Length,
                (o, s) => K.LaunchTwoNullableMemoryKernel(
                    create(), new Memory<int>([1]), o, gridDimX: s.GridX, blockDimX: s.BlockX));
            yield return Create(nameof(K.TwoNullableMemoryKernel), "Second" + valueName, R.Length,
                (o, s) => K.LaunchTwoNullableMemoryKernel(
                    null, create(), o, gridDimX: s.GridX, blockDimX: s.BlockX));
        }
    }

    // null: default (no array); []: ArraySegment<int>.Empty (an empty array)
    private static ArraySegment<int> Segment(int[]? array) => array is null ? default : new ArraySegment<int>(array);

    // null: default (IsDefault); []: ImmutableArray<int>.Empty
    private static ImmutableArray<int> Immutable(int[]? array) =>
        array is null ? default : ImmutableArray.Create(array);
}
