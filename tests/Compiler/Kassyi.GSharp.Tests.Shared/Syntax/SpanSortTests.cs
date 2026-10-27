using System;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// <c>MemoryExtensions.Sort</c> in device code (#625): the port of the .NET introsort in <c>Prelude/Sort.hpp</c> must give
/// the same order as .NET, which the managed path runs itself. Expected values come from the host calling .NET on a copy of
/// the input the test holds, never from the device.
/// </summary>
/// <remarks>
/// Properties: (1) the result equals .NET element by element (for floating point, bit by bit, so the order of -0.0 and 0.0
/// and of NaN payloads is checked); (2) adjacent elements are in order by <see cref="Comparer{T}.Default"/>, which does not
/// depend on the .NET sort. Each input runs on the CPU dialect natively and on the managed path, and on CUDA.
/// </remarks>
[Collection(TestCollections.GlobalStateSequential)]
public class SpanSortTests : TranspilerTestBase
{
    private const uint GridDimX = 2;
    private const uint BlockDimX = 32;

    // Lengths around the insertion sort threshold (16), the 2- and 3-element paths and larger partitions
    private static readonly int[] s_lengths = [1, 2, 3, 4, 15, 16, 17, 31, 32, 33, 64, 100, 1000];

    public static TheoryData<int, SortPattern, bool> IntCases()
    {
        var data = new TheoryData<int, SortPattern, bool>();
        foreach (var managed in new[] { false, true })
        {
            foreach (var length in s_lengths)
            {
                foreach (var pattern in Enum.GetValues<SortPattern>())
                {
                    data.Add(length, pattern, managed);
                }
            }
        }

        return data;
    }

    public static TheoryData<int, bool> TypedCases()
    {
        var data = new TheoryData<int, bool>();
        foreach (var managed in new[] { false, true })
        {
            foreach (var length in new[] { 1, 2, 3, 16, 17, 33, 100, 1000 })
            {
                data.Add(length, managed);
            }
        }

        return data;
    }

    public static TheoryData<bool> Paths() => new() { false, true };

    [SkippableTheory]
    [MemberData(nameof(IntCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortInt_MatchesDotNet(int length, SortPattern pattern, bool managed)
    {
        var input = SortInputs.Int(length, pattern, seed: 625 + length);
        var data = (int[])input.Clone();

        Run(managed, () => SpanSortKernels.LaunchSortIntKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        // mutant: Sort.hpp __gsharp_sort_insertion `j >= 0` -> `j > 0` (the first element never moves; descending, length 4)
        AssertSameAsDotNet(input, data, $"int length {length} {pattern}");
    }

    // The input that McIlroy's adversary builds against the .NET introsort exhausts the depth limit, so the heap sort runs
    [SkippableTheory]
    [InlineData(64, false)]
    [InlineData(1000, false)]
    [InlineData(64, true)]
    [InlineData(1000, true)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SortInt_DepthLimitInput_UsesHeapSortAndMatchesDotNet(int length, bool managed)
    {
        var input = SortInputs.HeapSortKiller(length);
        var data = (int[])input.Clone();

        Run(managed, () => SpanSortKernels.LaunchSortIntKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        // mutant: Sort.hpp __gsharp_sort_down_heap `child < n` -> `child <= n` (reads past the heap; order breaks)
        AssertSameAsDotNet(input, data, $"heap sort input of length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(TypedCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortLong_MatchesDotNet(int length, bool managed)
    {
        var random = new Random(6251 + length);
        var input = new long[length];
        for (var i = 0; i < length; i++)
        {
            input[i] = random.NextInt64(long.MinValue, long.MaxValue) >> random.Next(0, 63);
        }

        var data = (long[])input.Clone();
        Run(managed, () => SpanSortKernels.LaunchSortLongKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        AssertSameAsDotNet(input, data, $"long length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(TypedCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortUInt_MatchesDotNet(int length, bool managed)
    {
        var random = new Random(6252 + length);
        var input = new uint[length];
        for (var i = 0; i < length; i++)
        {
            // Values above int.MaxValue order differently as signed integers
            input[i] = (uint)random.NextInt64(0, uint.MaxValue + 1L);
        }

        var data = (uint[])input.Clone();
        Run(managed, () => SpanSortKernels.LaunchSortUIntKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        AssertSameAsDotNet(input, data, $"uint length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(TypedCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortChar_MatchesDotNet(int length, bool managed)
    {
        var random = new Random(6253 + length);
        var input = new char[length];
        for (var i = 0; i < length; i++)
        {
            input[i] = (char)random.Next(0, char.MaxValue + 1);
        }

        var data = (char[])input.Clone();
        Run(managed, () => SpanSortKernels.LaunchSortCharKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        AssertSameAsDotNet(input, data, $"char length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(TypedCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortEnum_OrdersByUnderlyingValue(int length, bool managed)
    {
        SortRank[] ranks = [SortRank.High, SortRank.Low, SortRank.Middle];
        var random = new Random(6254 + length);
        var input = new SortRank[length];
        for (var i = 0; i < length; i++)
        {
            input[i] = ranks[random.Next(ranks.Length)];
        }

        var data = (SortRank[])input.Clone();
        Run(managed, () => SpanSortKernels.LaunchSortEnumKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        AssertSameAsDotNet(input, data, $"enum length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(TypedCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SortFloat_NaNFirstAndSignedZerosInDotNetOrder(int length, bool managed)
    {
        var input = SortInputs.Single(length, seed: 6255 + length);
        var data = (float[])input.Clone();

        Run(managed, () => SpanSortKernels.LaunchSortFloatKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        // mutant: Sort.hpp __gsharp_sort_default without __gsharp_sort_move_nans_to_front (NaN is not first)
        AssertSameBits(input, data, BitConverter.SingleToInt32Bits, $"float length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(TypedCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SortDouble_NaNFirstAndSignedZerosInDotNetOrder(int length, bool managed)
    {
        var input = SortInputs.Double(length, seed: 6256 + length);
        var data = (double[])input.Clone();

        Run(managed, () => SpanSortKernels.LaunchSortDoubleKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        AssertSameBits(input, data, BitConverter.DoubleToInt64Bits, $"double length {length}");
    }

    [SkippableTheory]
    [InlineData(new[] { float.NaN, 1f, float.NaN, -0f, 0f, float.NegativeInfinity, float.PositiveInfinity, 2f }, false)]
    [InlineData(new[] { float.NaN, 1f, float.NaN, -0f, 0f, float.NegativeInfinity, float.PositiveInfinity, 2f }, true)]
    [InlineData(new[] { float.NaN, float.NaN, float.NaN }, false)]
    [InlineData(new[] { float.NaN, float.NaN, float.NaN }, true)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SortFloat_SpecialValues(float[] input, bool managed)
    {
        var data = (float[])input.Clone();

        Run(managed, () => SpanSortKernels.LaunchSortFloatKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        AssertSameBits(input, data, BitConverter.SingleToInt32Bits, "special values");
    }

    // Each of the 64 threads sorts a different segment at the same time
    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortSegments_EveryThreadSortsItsOwnBuffer(bool managed)
    {
        const int Segments = (int)(GridDimX * BlockDimX);
        const int Length = SpanSortKernels.SegmentLength;
        var random = new Random(6257);
        var input = new int[Segments * Length];
        for (var i = 0; i < input.Length; i++)
        {
            input[i] = random.Next(-50, 50);
        }

        var data = (int[])input.Clone();
        Run(managed, () => SpanSortKernels.LaunchSortSegmentsKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        for (var s = 0; s < Segments; s++)
        {
            AssertSameAsDotNet(input.AsSpan(s * Length, Length).ToArray(), data.AsSpan(s * Length, Length).ToArray(),
                $"segment {s}");
        }
    }

    private void Run(bool managed, Action launch)
    {
        Skip.If(managed && IsCudaDialect, "The managed path runs on the CPU test assembly.");
#if GSHARP_CPU
        if (managed)
        {
            ManagedKernelRun.Run(launch);
            return;
        }
#endif
        launch();
    }

    private static void AssertSameAsDotNet<T>(T[] input, T[] actual, string what)
    {
        var expected = (T[])input.Clone();
        MemoryExtensions.Sort(expected.AsSpan());

        // (1) the order of .NET, element by element
        Assert.True(expected.AsSpan().SequenceEqual(actual), $"{what}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");

        // (2) ascending by the default comparer, which does not depend on the order .NET picks among equal keys
        var comparer = Comparer<T>.Default;
        for (var i = 1; i < actual.Length; i++)
        {
            Assert.True(comparer.Compare(actual[i - 1], actual[i]) <= 0, $"{what}: elements {i - 1} and {i} are out of order");
        }
    }

    // Equal keys of floating point (-0.0 and 0.0, NaNs with different payloads) differ in their bits, so the bits show
    // whether the port moves them exactly as .NET does
    private static void AssertSameBits<T, TBits>(T[] input, T[] actual, Func<T, TBits> bits, string what)
        where TBits : IEquatable<TBits>
    {
        var expected = (T[])input.Clone();
        MemoryExtensions.Sort(expected.AsSpan());
        var expectedBits = Array.ConvertAll(expected, x => bits(x));
        var actualBits = Array.ConvertAll(actual, x => bits(x));
        Assert.True(expectedBits.AsSpan().SequenceEqual(actualBits),
            $"{what}: expected [{string.Join(", ", expectedBits)}], got [{string.Join(", ", actualBits)}]");
    }
}

/// <summary>The value patterns of <see cref="SortInputs.Int"/>.</summary>
public enum SortPattern
{
    Random,
    FewDistinct,
    Ascending,
    Descending,
    AllEqual,
}

/// <summary>Deterministic sort inputs (fixed seeds).</summary>
internal static class SortInputs
{
    public static int[] Int(int length, SortPattern pattern, int seed)
    {
        var random = new Random(seed);
        var values = new int[length];
        for (var i = 0; i < length; i++)
        {
            values[i] = pattern switch
            {
                SortPattern.Random => random.Next(int.MinValue, int.MaxValue),
                SortPattern.FewDistinct => random.Next(0, 4),
                SortPattern.Ascending => i,
                SortPattern.Descending => length - i,
                _ => 7,
            };
        }

        return values;
    }

    // Many equal keys with different bits: -0.0 / 0.0 and NaNs with different payloads, mixed with a few ordinary values
    public static float[] Single(int length, int seed)
    {
        float[] pool =
        [
            -0f, 0f, 1f, -1f, float.PositiveInfinity, float.NegativeInfinity, float.NaN,
            BitConverter.Int32BitsToSingle(0x7fc00001), BitConverter.Int32BitsToSingle(unchecked((int)0xffc00002)),
        ];
        var random = new Random(seed);
        var values = new float[length];
        for (var i = 0; i < length; i++)
        {
            values[i] = pool[random.Next(pool.Length)];
        }

        return values;
    }

    public static double[] Double(int length, int seed)
    {
        double[] pool =
        [
            -0d, 0d, 1d, -1d, double.PositiveInfinity, double.NegativeInfinity, double.NaN,
            BitConverter.Int64BitsToDouble(0x7ff8000000000001), BitConverter.Int64BitsToDouble(unchecked((long)0xfff8000000000002)),
        ];
        var random = new Random(seed);
        var values = new double[length];
        for (var i = 0; i < length; i++)
        {
            values[i] = pool[random.Next(pool.Length)];
        }

        return values;
    }

    /// <summary>
    /// McIlroy's adversary ("A Killer Adversary for Quicksort", 1999) run against the .NET introsort: the values are fixed
    /// lazily, so that each partition peels off only a few elements and the depth limit runs out (the heap sort path).
    /// </summary>
    public static int[] HeapSortKiller(int length)
    {
        var gas = length;
        var values = new int[length];
        Array.Fill(values, gas);
        var order = new int[length];
        for (var i = 0; i < length; i++)
        {
            order[i] = i;
        }

        var solid = 0;
        var candidate = 0;
        MemoryExtensions.Sort(order.AsSpan(), (x, y) =>
        {
            if (values[x] == gas && values[y] == gas)
            {
                values[x == candidate ? x : y] = solid++;
            }

            if (values[x] == gas)
            {
                candidate = x;
            }
            else if (values[y] == gas)
            {
                candidate = y;
            }

            return values[x].CompareTo(values[y]);
        });

        // The values never compared with each other while gas are larger than every frozen one, in any order
        for (var i = 0; i < length; i++)
        {
            if (values[i] == gas)
            {
                values[i] = solid++;
            }
        }

        return values;
    }
}
