#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.DifferentialHarnessFakes;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Tests of the comparison policies of <see cref="DiffBuffers"/>: bitwise and exact cells, deep struct aliasing in
/// clones, multiset columns and ULP tolerances. Each check names, above it, the one-line change of the harness (mutant)
/// that makes it fail.
/// </summary>
public class DifferentialPolicyTests
{
    // ---- Comparison policies ----

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0x3F800000u, 0x3F800000u, true, true)] // 1f, 1f
    [InlineData(0x3F800000u, 0x3F800001u, false, false)] // 1f, the next float: the last mantissa bit
    [InlineData(0x00000000u, 0x80000000u, false, false)] // 0f, -0f: the sign bit (== would call them equal)
    [InlineData(0x7FC00000u, 0x7FC00000u, true, true)] // the same NaN
    [InlineData(0x7FC00000u, 0xFFC00001u, true, false)] // NaN, a negative NaN with a payload
    [InlineData(0x7FC00000u, 0x7F800000u, false, false)] // NaN, +infinity
    [InlineData(0x7FC00000u, 0x3F800000u, false, false)] // NaN, 1f
    public void DiffPolicy_SinglePrecision_ComparesBitwise(uint left, uint right, bool bitwiseMatch, bool exactMatch)
    {
        // Bitwise: one differing bit is a mismatch, but any NaN matches any NaN (.NET passes a NaN through, PTX returns
        // the canonical one). Exact: every bit, NaN payloads included
        // mutant: DiffBuffers.cs CellsMatch `|| (IsNaN(...) && IsNaN(...))` removed (the NaN rows fail)
        // mutant: DiffBuffers.cs BitsEqual compared through `x.Equals(y)` (0f and -0f match, NaN never does)
        Assert.Equal(bitwiseMatch, CellsMatch(BitConverter.UInt32BitsToSingle(left), BitConverter.UInt32BitsToSingle(right), OutputPolicy.Bitwise));
        Assert.Equal(exactMatch, CellsMatch(BitConverter.UInt32BitsToSingle(left), BitConverter.UInt32BitsToSingle(right), OutputPolicy.Exact));
        Assert.True(CellsMatch(BitConverter.UInt32BitsToSingle(left), BitConverter.UInt32BitsToSingle(right), OutputPolicy.Ignore));
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0x3FF0000000000000ul, 0x3FF0000000000001ul, false)] // 1.0, the next double
    [InlineData(0x0000000000000000ul, 0x8000000000000000ul, false)] // 0.0, -0.0
    [InlineData(0x7FF8000000000000ul, 0xFFF8000000000001ul, true)] // two NaNs
    [InlineData(0x7FF8000000000000ul, 0x7FF0000000000000ul, false)] // NaN, +infinity
    public void DiffPolicy_DoublePrecision_ComparesBitwise(ulong left, ulong right, bool bitwiseMatch)
    {
        // As for float, on 64 bits
        // mutant: DiffBuffers.cs IsNaN `double d => double.IsNaN(d)` removed
        Assert.Equal(bitwiseMatch, CellsMatch(BitConverter.UInt64BitsToDouble(left), BitConverter.UInt64BitsToDouble(right), OutputPolicy.Bitwise));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void DiffPolicy_IntegerTypes_ComparesEveryBit()
    {
        // The lowest and the highest bit of each width are mismatches
        // mutant: DiffBuffers.cs BitsEqual compares only the first byte (`.Slice(0, 1)` on both spans)
        Assert.False(CellsMatch(0, 1, OutputPolicy.Exact));
        Assert.False(CellsMatch(0, int.MinValue, OutputPolicy.Exact));
        Assert.False(CellsMatch(0L, long.MinValue, OutputPolicy.Bitwise));
        Assert.False(CellsMatch((byte)0, (byte)0x80, OutputPolicy.Exact));
        Assert.True(CellsMatch(-1L, -1L, OutputPolicy.Exact));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void DiffBuffers_Clone_PreservesArrayAndDeepStructAliasing()
    {
        // Cloning DiffBuffers must preserve ReferenceEquals between top-level array buffers and deep-struct array fields
        // while isolating the cloned arrays from the original buffers
        // mutant: StructElementComparer.cs DiffArrayCloneScope.CloneArray remove `if (_objectMap.TryGetValue(source, out var existing)) return (TArray[])existing;`
        int[] shared = [10, 20];
        var buffers = new DiffBuffers()
            .In("a", shared)
            .In("b", shared)
            .InDeepScalar(
                "bag",
                new DeepBagProbe(shared, 7),
                static (bag, scope) => new DeepBagProbe(scope.CloneArray(bag.Data), bag.Tag),
                static (ctx, expected, actual) =>
                {
                    ctx.Equal("Tag", expected.Tag, actual.Tag);
                    ctx.Array("Data", expected.Data, actual.Data);
                }
            );

        var copy = buffers.Clone();
        var copyA = copy.Get<int>("a");
        var copyB = copy.Get<int>("b");
        var copyBag = copy.GetScalar<DeepBagProbe>("bag");

        Assert.NotSame(shared, copyA);
        Assert.Same(copyA, copyB);
        Assert.Same(copyA, copyBag.Data);
        Assert.Equal([10, 20], copyA);

        copyA[0] = 99;
        Assert.Equal(10, shared[0]);
        Assert.NotNull(copyBag.Data);
        Assert.Equal(99, copyBag.Data[0]);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public unsafe void StructElementComparer_PaddedStructWithPaddingBytes_IgnoresPaddingBytes()
    {
        // Two instances of a padded struct with identical fields (B = 1, I = 42) but different alignment padding bytes
        // must match under StructElementComparer / OutputPolicy.ForStruct while raw byte sequence comparison differs
        // mutant: StructElementComparer.cs Equals calls `MemoryMarshal.AsBytes` sequence comparison instead of `_fieldPredicate`
        PaddedStructProbe left = default;
        PaddedStructProbe right = default;
        new Span<byte>(&left, sizeof(PaddedStructProbe)).Fill(0xAA);
        new Span<byte>(&right, sizeof(PaddedStructProbe)).Fill(0x55);
        left.B = 1;
        left.I = 42;
        right.B = 1;
        right.I = 42;

        Assert.False(
            MemoryMarshal.AsBytes(new ReadOnlySpan<PaddedStructProbe>(in left))
                .SequenceEqual(MemoryMarshal.AsBytes(new ReadOnlySpan<PaddedStructProbe>(in right)))
        );

        var policy = OutputPolicy.ForStruct<PaddedStructProbe>(static (ctx, e, a) =>
        {
            ctx.Field("B", e.B, a.B);
            ctx.Field("I", e.I, a.I);
        });

        var bufA = new DiffBuffers().OutScalar<PaddedStructProbe>("p", policy);
        var bufB = bufA.Clone();
        bufA.SetScalar("p", left);
        bufB.SetScalar("p", right);
        Assert.True(bufA.Find("p").CellsMatch(bufB.Find("p"), 0));

        right.I = 43;
        bufB.SetScalar("p", right);
        Assert.False(bufA.Find("p").CellsMatch(bufB.Find("p"), 0));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void OutputPolicy_Multiset_SortsPerColumnAndHandlesFloatTotalOrder()
    {
        // 1. Multi-column Multiset sorts each column independently: swapping values between column 0 and column 1 must fail
        // mutant: DiffBuffers.cs CreateSortedCopy sorts the entire flat `sorted` array without separating columns
        string[] cols = ["col0", "col1"];
        var permutedWithinCols = new DifferentialCase("HarnessTests.MultisetColumns")
        {
            Shape = LaunchShape.ForRows(2, blockX: 2),
            Inputs = static (_, _) => new DiffBuffers().Out<int>("r", 2, ["col0", "col1"], OutputPolicy.Multiset),
            Reference = static (b, _) =>
            {
                var r = b.Get<int>("r");
                // Row 0: (10, 20), Row 1: (30, 40) -> col0={10,30}, col1={20,40}
                r[0] = 10;
                r[1] = 20;
                r[2] = 30;
                r[3] = 40;
            },
            Launch = static (b, _) =>
            {
                var r = b.Get<int>("r");
                // Row 0: (30, 40), Row 1: (10, 20) -> col0={30,10}, col1={40,20} (same per-column multiset)
                r[0] = 30;
                r[1] = 40;
                r[2] = 10;
                r[3] = 20;
            },
        };
        Assert.False(DifferentialRunner.Evaluate(permutedWithinCols, [0], NativeOnly, NoEntries).HasFailures);

        var swappedAcrossCols = permutedWithinCols with
        {
            Launch = static (b, _) =>
            {
                var r = b.Get<int>("r");
                // Overall flat multiset is {10,20,30,40}, but col0={10,20} != {10,30} and col1={30,40} != {20,40}
                r[0] = 10;
                r[1] = 30;
                r[2] = 20;
                r[3] = 40;
            },
        };
        Assert.True(DifferentialRunner.Evaluate(swappedAcrossCols, [0], NativeOnly, NoEntries).HasFailures);
        _ = cols;

        // 2. Float Multiset total order: -0.0f and +0.0f are kept distinct and ordered deterministically alongside NaNs
        // mutant: DiffBuffers.cs SortSpanTotalOrder uses `span.Sort()` (IEEE value comparison leaves -0.0f/+0.0f in arbitrary order)
        float negNaN = BitConverter.UInt32BitsToSingle(0xFFC00001u);
        float posNaN = BitConverter.UInt32BitsToSingle(0x7FC00000u);
        var floatMultiset = new DifferentialCase("HarnessTests.FloatMultiset")
        {
            Shape = LaunchShape.ForRows(4, blockX: 4),
            Inputs = static (_, _) => new DiffBuffers().Out<float>("f", 4, OutputPolicy.Multiset),
            Reference = (b, _) =>
            {
                var f = b.Get<float>("f");
                f[0] = +0.0f;
                f[1] = -0.0f;
                f[2] = negNaN;
                f[3] = -1.5f;
            },
            Launch = (b, _) =>
            {
                var f = b.Get<float>("f");
                f[0] = posNaN;
                f[1] = -1.5f;
                f[2] = -0.0f;
                f[3] = +0.0f;
            },
        };
        Assert.False(DifferentialRunner.Evaluate(floatMultiset, [0], NativeOnly, NoEntries).HasFailures);

        // Replacing -0.0f with +0.0f must fail under Multiset
        var wrongZeroSign = floatMultiset with
        {
            Launch = (b, _) =>
            {
                var f = b.Get<float>("f");
                f[0] = posNaN;
                f[1] = -1.5f;
                f[2] = +0.0f;
                f[3] = +0.0f;
            },
        };
        Assert.True(DifferentialRunner.Evaluate(wrongZeroSign, [0], NativeOnly, NoEntries).HasFailures);

        // 3. Verify and VerifyAgainstInitial work without Oracle/Reference when output buffers use OutputPolicy.Ignore
        var verifyCase = new DifferentialCase("HarnessTests.VerifyAgainstInitial")
        {
            Shape = LaunchShape.ForRows(2, blockX: 2),
            Inputs = static (_, _) => new DiffBuffers().In("state", [5, 10], OutputPolicy.Ignore).Out<int>("old", 2, OutputPolicy.Ignore),
            Launch = static (b, _) =>
            {
                var state = b.Get<int>("state");
                var old = b.Get<int>("old");
                old[0] = state[0];
                old[1] = state[1];
                state[0] += 1;
                state[1] += 2;
            },
            Verify = static (actual, _) => actual.Get<int>("state")[0] == 6 ? PropertyResult.Passed : PropertyResult.Failed("state[0] != 6"),
            VerifyAgainstInitial = static (initial, actual, _) =>
                actual.Get<int>("old")[0] == initial.Get<int>("state")[0] && actual.Get<int>("old")[1] == initial.Get<int>("state")[1]
                    ? PropertyResult.Passed
                    : PropertyResult.Failed("old values did not match initial state"),
        };
        Assert.False(DifferentialRunner.Evaluate(verifyCase, [0], NativeOnly, NoEntries).HasFailures);

        var failingVerify = verifyCase with
        {
            Verify = static (_, _) => PropertyResult.Failed("invariant broken"),
        };
        var failReport = DifferentialRunner.Evaluate(failingVerify, [0], NativeOnly, NoEntries);
        Assert.True(failReport.HasFailures);
        Assert.Single(failReport.PropertyFailures);
        Assert.Contains("failed property check: invariant broken", failReport.ToString());
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void OutputPolicy_UlpTolerance_RejectsOneSidedNaNAndMeasuresAcrossZero()
    {
        // (1) One-sided NaN (0x7F800001) is Only 2 integer steps above float.MaxValue (0x7F7FFFFF) in raw IEEE bits,
        //     but must never match a finite or infinite number under any ULP tolerance
        // mutant: DiffBuffers.cs UlpEqualValue `float.IsNaN(fx) || float.IsNaN(fy)` -> `float.IsNaN(fx) && float.IsNaN(fy)`
        float smallestPositiveNaN = BitConverter.Int32BitsToSingle(0x7F80_0001);
        var ulp10 = OutputPolicy.Ulp(10, "NaN boundary probe", allowInfinityBoundary: true);
        Assert.False(CellsMatch(float.MaxValue, smallestPositiveNaN, ulp10));
        Assert.False(CellsMatch(smallestPositiveNaN, float.MaxValue, ulp10));
        Assert.True(CellsMatch(smallestPositiveNaN, float.NaN, ulp10));

        // (2) Sign-Magnitude linear mapping places -0.0f and +0.0f at 0, so -float.Epsilon (-1) and +float.Epsilon (+1)
        //     are 2 ULP apart across zero
        // mutant: DiffBuffers.cs ToLinearIndex32 returns raw two's complement `bits` instead of `-(long)(bits & 0x7FFF_FFFF)`
        Assert.True(CellsMatch(-0.0f, +0.0f, OutputPolicy.Ulp(0, "signed zero")));
        Assert.False(CellsMatch(-float.Epsilon, +float.Epsilon, OutputPolicy.Ulp(1, "across zero 1 ulp")));
        Assert.True(CellsMatch(-float.Epsilon, +float.Epsilon, OutputPolicy.Ulp(2, "across zero 2 ulp")));

        // (3) Infinity boundary is rejected by default and accepted only when allowInfinityBoundary is true
        Assert.False(CellsMatch(float.MaxValue, float.PositiveInfinity, OutputPolicy.Ulp(2, "strict infinity")));
        Assert.True(CellsMatch(float.MaxValue, float.PositiveInfinity, OutputPolicy.Ulp(1, "adjacent infinity", allowInfinityBoundary: true)));

        // (4) Multi-column ColumnPolicies applies distinct policies per column
        var expected = new DiffBuffers().Out<float>(
            "r",
            rows: 1,
            [
                ("bitwise", OutputPolicy.Bitwise),
                ("ulp2", OutputPolicy.Ulp(2, "column ulp probe")),
            ]
        );
        var actual = expected.Clone();
        expected.Get<float>("r")[0] = 1.0f;
        expected.Get<float>("r")[1] = -float.Epsilon;
        actual.Get<float>("r")[0] = 1.0f;
        actual.Get<float>("r")[1] = +float.Epsilon;

        Assert.True(expected.Find("r").CellsMatch(actual.Find("r"), 0));
        Assert.True(expected.Find("r").CellsMatch(actual.Find("r"), 1));

        actual.Get<float>("r")[0] = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(1.0f) + 1);
        Assert.False(expected.Find("r").CellsMatch(actual.Find("r"), 0));
    }

    // ---- Helpers ----

    private readonly record struct DeepBagProbe(int[]? Data, int Tag);

    [StructLayout(LayoutKind.Sequential)]
    private struct PaddedStructProbe
    {
        public byte B;
        public int I;
    }

    private static bool CellsMatch<T>(T left, T right, OutputPolicy policy)
        where T : unmanaged
    {
        var a = new DiffBuffers().Out<T>("x", 1, policy);
        var b = a.Clone();
        a.Get<T>("x")[0] = left;
        b.Get<T>("x")[0] = right;
        return a.Find("x").CellsMatch(b.Find("x"), 0);
    }
}
