using System.Globalization;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// The device functions that split kernels call: two plain helpers, four suspending functions (a barrier or a
/// shuffle that reads the calling lane inside) and two iterators, with constants drawn from the batch fuzz seed.
/// </summary>
internal static class FuzzSplitHelperSources
{
    private const string Mask = FuzzSplitOracle.FullMaskLiteral;

    private const string SelfLane = FuzzSplitOracle.SelfLaneExpression;

    private const string IntSequence = "System.Collections.Generic.IEnumerable<int>";

    /// <summary>Builds the helper sources of the batch with <paramref name="fuzzSeed"/>.</summary>
    internal static IReadOnlyList<string> Build(ulong fuzzSeed)
    {
        var rng = new FuzzRandom(FuzzRandom.Mix(fuzzSeed ^ 0x5B11D3F1CE5UL));
        string c1 = Literal(rng.NextRange(3, 61));
        string c2 = Literal(rng.NextRange(5, 97));
        string c3 = Literal(rng.NextRange(3, 29));
        string c4 = Literal(rng.NextRange(1, 41));
        return
        [
            SideBump(),
            SideMix(c1, c2),
            SuspendSync(c3),
            SuspendRef(),
            SuspendShuffle(c4),
            SuspendOut(),
            SplitIterA(),
            SplitIterB(),
        ];
    }

    private static string Literal(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string SideBump() =>
        $$"""
        [GpuDeviceFunction]
        public static int {{FuzzSplitKernelEmitter.SideBump}}(ref int value, int delta)
        {
        value += delta;
        return (value & 15) + 1;
        }
        """;

    private static string SideMix(string c1, string c2) =>
        $$"""
        [GpuDeviceFunction]
        public static int {{FuzzSplitKernelEmitter.SideMix}}(int x, int y, int z)
        {
        return (x * 31) ^ (y * {{c1}}) ^ (z + {{c2}});
        }
        """;

    private static string SuspendSync(string c3) =>
        $$"""
        [GpuDeviceFunction]
        public static int {{FuzzSplitKernelEmitter.SuspendSync}}(int x, int y)
        {
        int t = (x * {{c3}}) + y;
        Gpu.SyncThreads();
        return t ^ (y >> 1);
        }
        """;

    private static string SuspendRef() =>
        $$"""
        [GpuDeviceFunction]
        public static int {{FuzzSplitKernelEmitter.SuspendRef}}(ref int value, int delta)
        {
        value += delta;
        Gpu.SyncThreads();
        return value * 3;
        }
        """;

    private static string SuspendShuffle(string c4) =>
        $$"""
        [GpuDeviceFunction]
        public static int {{FuzzSplitKernelEmitter.SuspendShuffle}}(int x)
        {
        return Warp.ShuffleSync({{Mask}}, x + {{c4}}, {{SelfLane}}) ^ x;
        }
        """;

    private static string SuspendOut() =>
        $$"""
        [GpuDeviceFunction]
        public static int {{FuzzSplitKernelEmitter.SuspendOut}}(int a, int b, out int c)
        {
        c = a ^ b;
        Gpu.SyncThreads();
        return c + b;
        }
        """;

    private static string SplitIterA() =>
        $$"""
        [GpuDeviceFunction]
        public static {{IntSequence}} {{FuzzSplitKernelEmitter.SplitIterA}}(int seed, int count)
        {
        for (int i = 0; i < count; i++)
        {
        seed = {{FuzzSplitKernelEmitter.SideMix}}(seed, i, count);
        if ((seed & 7) == 3)
        {
        yield break;
        }

        yield return seed ^ i;
        }
        }
        """;

    private static string SplitIterB() =>
        $$"""
        [GpuDeviceFunction]
        public static {{IntSequence}} {{FuzzSplitKernelEmitter.SplitIterB}}(int start, int step)
        {
        yield return start;
        int next = start + step;
        yield return next;
        if (next > start)
        {
        yield return {{FuzzSplitKernelEmitter.SideBump}}(ref next, step);
        }

        yield return next;
        }
        """;
}
