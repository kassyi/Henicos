#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.DifferentialHarnessFakes;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public sealed class DiffRecordWriterTests
{
    private readonly record struct DeepPair(int Tag, int[] Values);

    [StructLayout(LayoutKind.Sequential)]
    private struct PaddedPair
    {
        public byte Tag;
        public int Value;
    }

    private sealed class NullableIntComparer : IElementComparer<int?>
    {
        public bool Equals(in int? expected, in int? actual, OutputPolicy policy) =>
            expected == actual;

        public string Format(in int? value) =>
            value.HasValue ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null";
    }

    private sealed class DeepPairComparer : IElementComparer<DeepPair>
    {
        public bool Equals(in DeepPair expected, in DeepPair actual, OutputPolicy policy) =>
            expected.Tag == actual.Tag && expected.Values.AsSpan().SequenceEqual(actual.Values);

        public string Format(in DeepPair value) =>
            $"{value.Tag}:[{string.Join(',', value.Values)}]";
    }

    private sealed class PaddedPairComparer : IElementComparer<PaddedPair>
    {
        public bool Equals(in PaddedPair expected, in PaddedPair actual, OutputPolicy policy) =>
            expected.Tag == actual.Tag && expected.Value == actual.Value;

        public string Format(in PaddedPair value) =>
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{value.Tag}:{value.Value}");
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void ComputeOutputsHash_OutputsOnlyInNameOrder_IgnoresInputsAndAddOrder()
    {
        var baseA = new DiffBuffers()
            .In("inA", [1, 2, 3])
            .Out<int>("beta", 2, OutputPolicy.Exact)
            .Out<int>("alpha", 2, OutputPolicy.Exact);
        baseA.Get<int>("alpha")[0] = 10;
        baseA.Get<int>("beta")[1] = 20;

        var changedInputSameOutputDifferentOrder = new DiffBuffers()
            .In("inA", [99, 88, 77])
            .Out<int>("alpha", 2, OutputPolicy.Exact)
            .Out<int>("beta", 2, OutputPolicy.Exact);
        changedInputSameOutputDifferentOrder.Get<int>("alpha")[0] = 10;
        changedInputSameOutputDifferentOrder.Get<int>("beta")[1] = 20;

        string? hashBase = DiffRecordWriter.ComputeOutputsHash(baseA);
        string? hashReordered = DiffRecordWriter.ComputeOutputsHash(changedInputSameOutputDifferentOrder);
        Assert.NotNull(hashBase);
        Assert.StartsWith("sha256:", hashBase, StringComparison.Ordinal);
        Assert.Equal(hashBase, hashReordered);

        // Changing only an output value changes the hash.
        changedInputSameOutputDifferentOrder.Get<int>("alpha")[1] = 1;
        Assert.NotEqual(hashBase, DiffRecordWriter.ComputeOutputsHash(changedInputSameOutputDifferentOrder));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void ComputeOutputsHash_SwappedBuffersAndFramingAndDeepStructs_ProduceDistinctDeterministicHashes()
    {
        var first = new DiffBuffers()
            .Out<int>("a", 2, OutputPolicy.Exact)
            .Out<int>("b", 2, OutputPolicy.Exact);
        first.Get<int>("a")[0] = 1;
        first.Get<int>("b")[0] = 2;

        var swapped = new DiffBuffers()
            .Out<int>("a", 2, OutputPolicy.Exact)
            .Out<int>("b", 2, OutputPolicy.Exact);
        swapped.Get<int>("a")[0] = 2;
        swapped.Get<int>("b")[0] = 1;

        Assert.NotEqual(DiffRecordWriter.ComputeOutputsHash(first), DiffRecordWriter.ComputeOutputsHash(swapped));

        // Framing boundary: [1, 2] + [3] vs [1] + [2, 3].
        var splitLeft = new DiffBuffers()
            .Out<int>("a", 2, OutputPolicy.Exact)
            .Out<int>("b", 1, OutputPolicy.Exact);
        splitLeft.Get<int>("a")[0] = 1;
        splitLeft.Get<int>("a")[1] = 2;
        splitLeft.Get<int>("b")[0] = 3;

        var splitRight = new DiffBuffers()
            .Out<int>("a", 1, OutputPolicy.Exact)
            .Out<int>("b", 2, OutputPolicy.Exact);
        splitRight.Get<int>("a")[0] = 1;
        splitRight.Get<int>("b")[0] = 2;
        splitRight.Get<int>("b")[1] = 3;

        Assert.NotEqual(DiffRecordWriter.ComputeOutputsHash(splitLeft), DiffRecordWriter.ComputeOutputsHash(splitRight));

        // RefDeepScalar<T> (!CanPinAsBytes): two distinct managed arrays with equal values must hash identically.
        var comparer = new DeepPairComparer();
        static DeepPair ClonePair(DeepPair p, DiffArrayCloneScope s) => new(p.Tag, s.CloneArray(p.Values) ?? []);
        var deepOne = new DiffBuffers().RefDeepScalar("deep", new DeepPair(7, [3, 4]), ClonePair, comparer);
        var deepTwo = new DiffBuffers().RefDeepScalar("deep", new DeepPair(7, [3, 4]), ClonePair, comparer);
        var deepDiff = new DiffBuffers().RefDeepScalar("deep", new DeepPair(7, [3, 5]), ClonePair, comparer);

        string? deepHashOne = DiffRecordWriter.ComputeOutputsHash(deepOne);
        string? deepHashTwo = DiffRecordWriter.ComputeOutputsHash(deepTwo);
        string? deepHashDiff = DiffRecordWriter.ComputeOutputsHash(deepDiff);
        Assert.Equal(deepHashOne, deepHashTwo);
        Assert.NotEqual(deepHashOne, deepHashDiff);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void ComputeOutputsHash_IgnorePolicy_ExcludesIgnoredBufferAndColumnsFromHash()
    {
        var withoutIgnored = new DiffBuffers()
            .Out<int>("kept", 2, OutputPolicy.Exact);
        withoutIgnored.Get<int>("kept")[0] = 11;
        withoutIgnored.Get<int>("kept")[1] = 22;

        var ignoredOne = new DiffBuffers()
            .Out<int>("kept", 2, OutputPolicy.Exact)
            .Out<int>("ignored", 2, OutputPolicy.Ignore);
        ignoredOne.Get<int>("kept")[0] = 11;
        ignoredOne.Get<int>("kept")[1] = 22;
        ignoredOne.Get<int>("ignored")[0] = 100;
        ignoredOne.Get<int>("ignored")[1] = 200;

        var ignoredTwo = new DiffBuffers()
            .Out<int>("kept", 2, OutputPolicy.Exact)
            .Out<int>("ignored", 2, OutputPolicy.Ignore);
        ignoredTwo.Get<int>("kept")[0] = 11;
        ignoredTwo.Get<int>("kept")[1] = 22;
        ignoredTwo.Get<int>("ignored")[0] = 999;
        ignoredTwo.Get<int>("ignored")[1] = 888;

        string? expectedHash = DiffRecordWriter.ComputeOutputsHash(withoutIgnored);
        Assert.Equal(expectedHash, DiffRecordWriter.ComputeOutputsHash(ignoredOne));
        Assert.Equal(expectedHash, DiffRecordWriter.ComputeOutputsHash(ignoredTwo));

        // All-Ignore outputs still produce a deterministic non-null hash independent of cell contents.
        var onlyIgnoreA = new DiffBuffers().Out<ulong>("clock", 2, OutputPolicy.Ignore);
        onlyIgnoreA.Get<ulong>("clock")[0] = 12345UL;
        var onlyIgnoreB = new DiffBuffers().Out<ulong>("clock", 2, OutputPolicy.Ignore);
        onlyIgnoreB.Get<ulong>("clock")[0] = 98765UL;
        string? onlyIgnoreHashA = DiffRecordWriter.ComputeOutputsHash(onlyIgnoreA);
        Assert.NotNull(onlyIgnoreHashA);
        Assert.Equal(onlyIgnoreHashA, DiffRecordWriter.ComputeOutputsHash(onlyIgnoreB));

        // Per-column Ignore excludes only the ignored column while remaining sensitive to deterministic columns.
        var colA = new DiffBuffers()
            .Out<int>("cols", 2, [("det", OutputPolicy.Exact), ("nondet", OutputPolicy.Ignore)]);
        colA.Get<int>("cols")[0] = 5;
        colA.Get<int>("cols")[1] = 111;
        colA.Get<int>("cols")[2] = 9;
        colA.Get<int>("cols")[3] = 222;

        var colB = new DiffBuffers()
            .Out<int>("cols", 2, [("det", OutputPolicy.Exact), ("nondet", OutputPolicy.Ignore)]);
        colB.Get<int>("cols")[0] = 5;
        colB.Get<int>("cols")[1] = 777;
        colB.Get<int>("cols")[2] = 9;
        colB.Get<int>("cols")[3] = 888;

        string? colHashA = DiffRecordWriter.ComputeOutputsHash(colA);
        Assert.Equal(colHashA, DiffRecordWriter.ComputeOutputsHash(colB));

        colB.Get<int>("cols")[2] = 10;
        Assert.NotEqual(colHashA, DiffRecordWriter.ComputeOutputsHash(colB));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void ComputeOutputsHash_MultisetPolicy_HashesSortedCopyAndDetectsDifferentValues()
    {
        var multisetA = new DiffBuffers().Out<int>("m", 4, OutputPolicy.Multiset);
        multisetA.Get<int>("m")[0] = 4;
        multisetA.Get<int>("m")[1] = 1;
        multisetA.Get<int>("m")[2] = 3;
        multisetA.Get<int>("m")[3] = 2;

        var multisetPermuted = new DiffBuffers().Out<int>("m", 4, OutputPolicy.Multiset);
        multisetPermuted.Get<int>("m")[0] = 2;
        multisetPermuted.Get<int>("m")[1] = 4;
        multisetPermuted.Get<int>("m")[2] = 1;
        multisetPermuted.Get<int>("m")[3] = 3;

        var multisetDifferent = new DiffBuffers().Out<int>("m", 4, OutputPolicy.Multiset);
        multisetDifferent.Get<int>("m")[0] = 2;
        multisetDifferent.Get<int>("m")[1] = 4;
        multisetDifferent.Get<int>("m")[2] = 1;
        multisetDifferent.Get<int>("m")[3] = 9;

        string? hashA = DiffRecordWriter.ComputeOutputsHash(multisetA);
        Assert.Equal(hashA, DiffRecordWriter.ComputeOutputsHash(multisetPermuted));
        Assert.NotEqual(hashA, DiffRecordWriter.ComputeOutputsHash(multisetDifferent));

        // Total-order sorting distinguishes -0.0f from +0.0f while remaining permutation-invariant.
        var floatA = new DiffBuffers().Out<float>("mf", 3, OutputPolicy.Multiset);
        floatA.Get<float>("mf")[0] = +0.0f;
        floatA.Get<float>("mf")[1] = -0.0f;
        floatA.Get<float>("mf")[2] = 1.5f;

        var floatPermuted = new DiffBuffers().Out<float>("mf", 3, OutputPolicy.Multiset);
        floatPermuted.Get<float>("mf")[0] = 1.5f;
        floatPermuted.Get<float>("mf")[1] = +0.0f;
        floatPermuted.Get<float>("mf")[2] = -0.0f;

        var floatDifferentSign = new DiffBuffers().Out<float>("mf", 3, OutputPolicy.Multiset);
        floatDifferentSign.Get<float>("mf")[0] = 1.5f;
        floatDifferentSign.Get<float>("mf")[1] = +0.0f;
        floatDifferentSign.Get<float>("mf")[2] = +0.0f;

        string? floatHash = DiffRecordWriter.ComputeOutputsHash(floatA);
        Assert.Equal(floatHash, DiffRecordWriter.ComputeOutputsHash(floatPermuted));
        Assert.NotEqual(floatHash, DiffRecordWriter.ComputeOutputsHash(floatDifferentSign));

        // Multi-column ColumnPolicies with Multiset sorts only the Multiset column while keeping Exact order.
        var multiColA = new DiffBuffers()
            .Out<int>("mc", 3, [("ms", OutputPolicy.Multiset), ("ex", OutputPolicy.Exact)]);
        multiColA.Get<int>("mc")[0] = 30;
        multiColA.Get<int>("mc")[1] = 1;
        multiColA.Get<int>("mc")[2] = 10;
        multiColA.Get<int>("mc")[3] = 2;
        multiColA.Get<int>("mc")[4] = 20;
        multiColA.Get<int>("mc")[5] = 3;

        var multiColPermuted = new DiffBuffers()
            .Out<int>("mc", 3, [("ms", OutputPolicy.Multiset), ("ex", OutputPolicy.Exact)]);
        multiColPermuted.Get<int>("mc")[0] = 10;
        multiColPermuted.Get<int>("mc")[1] = 1;
        multiColPermuted.Get<int>("mc")[2] = 20;
        multiColPermuted.Get<int>("mc")[3] = 2;
        multiColPermuted.Get<int>("mc")[4] = 30;
        multiColPermuted.Get<int>("mc")[5] = 3;

        var multiColExactReordered = new DiffBuffers()
            .Out<int>("mc", 3, [("ms", OutputPolicy.Multiset), ("ex", OutputPolicy.Exact)]);
        multiColExactReordered.Get<int>("mc")[0] = 10;
        multiColExactReordered.Get<int>("mc")[1] = 3;
        multiColExactReordered.Get<int>("mc")[2] = 20;
        multiColExactReordered.Get<int>("mc")[3] = 2;
        multiColExactReordered.Get<int>("mc")[4] = 30;
        multiColExactReordered.Get<int>("mc")[5] = 1;

        string? multiColHash = DiffRecordWriter.ComputeOutputsHash(multiColA);
        Assert.Equal(multiColHash, DiffRecordWriter.ComputeOutputsHash(multiColPermuted));
        Assert.NotEqual(multiColHash, DiffRecordWriter.ComputeOutputsHash(multiColExactReordered));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void ComputeOutputsHash_StructAndNullablePolicies_HashFormattedCellsIgnoringPadding()
    {
        PaddedPair left = default;
        PaddedPair right = default;
        MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref left, 1)).Fill(0xAA);
        MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref right, 1)).Fill(0x55);
        left.Tag = 7;
        left.Value = 42;
        right.Tag = 7;
        right.Value = 42;

        Assert.False(
            MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref left, 1))
                .SequenceEqual(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref right, 1)))
        );

        var structPolicy = OutputPolicy.ForStruct<PaddedPair>(static (ctx, e, a) =>
        {
            ctx.Field(nameof(PaddedPair.Tag), e.Tag, a.Tag, OutputPolicy.Exact);
            ctx.Field(nameof(PaddedPair.Value), e.Value, a.Value, OutputPolicy.Exact);
        });

        var structA = new DiffBuffers().OutScalar<PaddedPair>("p", structPolicy);
        var structB = structA.Clone();
        structA.SetScalar("p", left);
        structB.SetScalar("p", right);

        string? structHashA = DiffRecordWriter.ComputeOutputsHash(structA);
        Assert.Equal(structHashA, DiffRecordWriter.ComputeOutputsHash(structB));

        right.Value = 43;
        structB.SetScalar("p", right);
        Assert.NotEqual(structHashA, DiffRecordWriter.ComputeOutputsHash(structB));

        // Nullable<T> with OutputPolicy.ForStruct hashes via FormatCell rather than raw padding/payload bytes.
        int? probeDefault = default;
        int? probeZero = 0;
        int nullableSize = Unsafe.SizeOf<int?>();
        var defaultBytes = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<int?, byte>(ref probeDefault), nullableSize);
        var zeroBytes = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<int?, byte>(ref probeZero), nullableSize);

        int? dirtyNull = default;
        var dirtyBytes = MemoryMarshal.CreateSpan(ref Unsafe.As<int?, byte>(ref dirtyNull), nullableSize);
        for (int k = 0; k < dirtyBytes.Length; k++)
        {
            if (defaultBytes[k] == zeroBytes[k])
            {
                dirtyBytes[k] = 0x7F;
            }
        }

        Assert.False(dirtyNull.HasValue);
        Assert.False(defaultBytes.SequenceEqual(dirtyBytes));

        var nullablePolicy = OutputPolicy.ForStruct(new NullableIntComparer());
        var nullableA = new DiffBuffers().OutScalar<int?>("n", nullablePolicy);
        var nullableB = nullableA.Clone();
        nullableA.SetScalar<int?>("n", null);
        nullableB.SetScalar("n", dirtyNull);

        string? nullableHashA = DiffRecordWriter.ComputeOutputsHash(nullableA);
        Assert.Equal(nullableHashA, DiffRecordWriter.ComputeOutputsHash(nullableB));

        nullableB.SetScalar<int?>("n", 42);
        Assert.NotEqual(nullableHashA, DiffRecordWriter.ComputeOutputsHash(nullableB));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void ComputeOutputsHash_BufferCustomComparerWithExactPolicy_HashesFormattedCellsIgnoringPadding()
    {
        PaddedPair left = default;
        PaddedPair right = default;
        MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref left, 1)).Fill(0xAA);
        MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref right, 1)).Fill(0x55);
        left.Tag = 7;
        left.Value = 42;
        right.Tag = 7;
        right.Value = 42;

        Assert.False(
            MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref left, 1))
                .SequenceEqual(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref right, 1)))
        );

        var comparer = new PaddedPairComparer();
        var bufA = new DiffBuffers().OutScalar<PaddedPair>("p", OutputPolicy.Exact, comparer);
        var bufB = bufA.Clone();
        bufA.SetScalar("p", left);
        bufB.SetScalar("p", right);

        Assert.True(bufA.Find("p").CellsMatch(bufB.Find("p"), 0));
        string? hashA = DiffRecordWriter.ComputeOutputsHash(bufA);
        Assert.Equal(hashA, DiffRecordWriter.ComputeOutputsHash(bufB));

        right.Value = 43;
        bufB.SetScalar("p", right);
        Assert.False(bufA.Find("p").CellsMatch(bufB.Find("p"), 0));
        Assert.NotEqual(hashA, DiffRecordWriter.ComputeOutputsHash(bufB));

        // Mixed ColumnPolicies with a buffer-level comparer still format deterministic cells rather than pinning bytes.
        right.Value = 42;
        var colA = new DiffBuffers()
            .Out<PaddedPair>("cols", 1, [("det", OutputPolicy.Exact), ("nondet", OutputPolicy.Ignore)], comparer);
        var colB = colA.Clone();
        colA.Get<PaddedPair>("cols")[0] = left;
        colA.Get<PaddedPair>("cols")[1] = new PaddedPair { Tag = 1, Value = 100 };
        colB.Get<PaddedPair>("cols")[0] = right;
        colB.Get<PaddedPair>("cols")[1] = new PaddedPair { Tag = 2, Value = 200 };

        string? colHashA = DiffRecordWriter.ComputeOutputsHash(colA);
        Assert.Equal(colHashA, DiffRecordWriter.ComputeOutputsHash(colB));

        right.Value = 43;
        colB.Get<PaddedPair>("cols")[0] = right;
        Assert.NotEqual(colHashA, DiffRecordWriter.ComputeOutputsHash(colB));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void ComputeOutputsHash_UlpTolerancePolicy_HashesRawBytesAndDetectsOneUlpDifference()
    {
        float one = 1.0f;
        float onePlusOneUlp = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(one) + 1);
        var policy = OutputPolicy.Ulp(4, "ULP tolerance still requires identical machine-code bytes");

        var bufA = new DiffBuffers().Out<float>("f", 2, policy);
        bufA.Get<float>("f")[0] = one;
        bufA.Get<float>("f")[1] = 2.0f;

        var bufSame = new DiffBuffers().Out<float>("f", 2, policy);
        bufSame.Get<float>("f")[0] = one;
        bufSame.Get<float>("f")[1] = 2.0f;

        var bufOneUlp = new DiffBuffers().Out<float>("f", 2, policy);
        bufOneUlp.Get<float>("f")[0] = onePlusOneUlp;
        bufOneUlp.Get<float>("f")[1] = 2.0f;

        Assert.True(bufA.Find("f").CellsMatch(bufOneUlp.Find("f"), 0));
        string? hashA = DiffRecordWriter.ComputeOutputsHash(bufA);
        Assert.Equal(hashA, DiffRecordWriter.ComputeOutputsHash(bufSame));
        Assert.NotEqual(hashA, DiffRecordWriter.ComputeOutputsHash(bufOneUlp));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void ComputeOutputsHash_BitwisePolicy_CanonicalizesNaNsAndPreservesDistinctNonNaNBits()
    {
        const uint SignalingNanBits = 0x7F80_0001u;
        float quietNanA = BitConverter.Int32BitsToSingle(0x7FC0_0001);
        float negativeQuietNan = BitConverter.Int32BitsToSingle(unchecked((int)0xFFC0_0002u));
        float canonicalNan = BitConverter.Int32BitsToSingle(0x7FC0_0000);

        var bufA = new DiffBuffers().Out<float>("bw", 2, OutputPolicy.Bitwise);
        bufA.Get<float>("bw")[0] = +0.0f;
        bufA.Get<float>("bw")[1] = quietNanA;

        var bufSignalingNan = new DiffBuffers().Out<float>("bw", 2, OutputPolicy.Bitwise);
        bufSignalingNan.Get<float>("bw")[0] = +0.0f;
        MemoryMarshal.Cast<float, uint>(bufSignalingNan.Get<float>("bw").AsSpan())[1] = SignalingNanBits;

        var bufNegativeNan = new DiffBuffers().Out<float>("bw", 2, OutputPolicy.Bitwise);
        bufNegativeNan.Get<float>("bw")[0] = +0.0f;
        bufNegativeNan.Get<float>("bw")[1] = negativeQuietNan;

        var bufCanonicalNan = new DiffBuffers().Out<float>("bw", 2, OutputPolicy.Bitwise);
        bufCanonicalNan.Get<float>("bw")[0] = +0.0f;
        bufCanonicalNan.Get<float>("bw")[1] = canonicalNan;

        var bufNegZero = new DiffBuffers().Out<float>("bw", 2, OutputPolicy.Bitwise);
        bufNegZero.Get<float>("bw")[0] = -0.0f;
        bufNegZero.Get<float>("bw")[1] = quietNanA;

        var bufPosInf = new DiffBuffers().Out<float>("bw", 2, OutputPolicy.Bitwise);
        bufPosInf.Get<float>("bw")[0] = float.PositiveInfinity;
        bufPosInf.Get<float>("bw")[1] = 1.0f;

        var bufNegInf = new DiffBuffers().Out<float>("bw", 2, OutputPolicy.Bitwise);
        bufNegInf.Get<float>("bw")[0] = float.NegativeInfinity;
        bufNegInf.Get<float>("bw")[1] = 1.0f;

        var bufSubnormal = new DiffBuffers().Out<float>("bw", 2, OutputPolicy.Bitwise);
        bufSubnormal.Get<float>("bw")[0] = float.Epsilon;
        bufSubnormal.Get<float>("bw")[1] = 1.0f;

        var bufZero = new DiffBuffers().Out<float>("bw", 2, OutputPolicy.Bitwise);
        bufZero.Get<float>("bw")[0] = +0.0f;
        bufZero.Get<float>("bw")[1] = 1.0f;

        var bufOnePlusUlp = new DiffBuffers().Out<float>("bw", 2, OutputPolicy.Bitwise);
        bufOnePlusUlp.Get<float>("bw")[0] = +0.0f;
        bufOnePlusUlp.Get<float>("bw")[1] = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(1.0f) + 1);

        Assert.True(bufA.Find("bw").CellsMatch(bufSignalingNan.Find("bw"), 1));
        string? hashA = DiffRecordWriter.ComputeOutputsHash(bufA);
        Assert.Equal(hashA, DiffRecordWriter.ComputeOutputsHash(bufSignalingNan));
        Assert.Equal(hashA, DiffRecordWriter.ComputeOutputsHash(bufNegativeNan));
        Assert.Equal(hashA, DiffRecordWriter.ComputeOutputsHash(bufCanonicalNan));
        Assert.NotEqual(hashA, DiffRecordWriter.ComputeOutputsHash(bufNegZero));
        Assert.NotEqual(DiffRecordWriter.ComputeOutputsHash(bufPosInf), DiffRecordWriter.ComputeOutputsHash(bufNegInf));
        Assert.NotEqual(DiffRecordWriter.ComputeOutputsHash(bufZero), DiffRecordWriter.ComputeOutputsHash(bufSubnormal));
        Assert.NotEqual(DiffRecordWriter.ComputeOutputsHash(bufZero), DiffRecordWriter.ComputeOutputsHash(bufOnePlusUlp));

        // Exact policy continues to distinguish distinct NaN payloads (write raw sNaN bits so Release JIT cannot quiet).
        var exactNanA = new DiffBuffers().Out<float>("ex", 1, OutputPolicy.Exact);
        MemoryMarshal.Cast<float, uint>(exactNanA.Get<float>("ex").AsSpan())[0] = SignalingNanBits;
        var exactNanB = new DiffBuffers().Out<float>("ex", 1, OutputPolicy.Exact);
        exactNanB.Get<float>("ex")[0] = quietNanA;
        Assert.NotEqual(DiffRecordWriter.ComputeOutputsHash(exactNanA), DiffRecordWriter.ComputeOutputsHash(exactNanB));

        // Large buffer (> 128 floats, exceeding 512-byte scratch chunk) canonicalizes across chunk boundaries.
        var chunkA = new DiffBuffers().Out<float>("chunk", 200, OutputPolicy.Bitwise);
        var chunkB = new DiffBuffers().Out<float>("chunk", 200, OutputPolicy.Bitwise);
        for (int i = 0; i < 200; i++)
        {
            chunkA.Get<float>("chunk")[i] = i;
            chunkB.Get<float>("chunk")[i] = i;
        }
        MemoryMarshal.Cast<float, uint>(chunkA.Get<float>("chunk").AsSpan())[0] = SignalingNanBits;
        chunkB.Get<float>("chunk")[0] = negativeQuietNan;
        chunkA.Get<float>("chunk")[199] = quietNanA;
        chunkB.Get<float>("chunk")[199] = canonicalNan;
        Assert.Equal(DiffRecordWriter.ComputeOutputsHash(chunkA), DiffRecordWriter.ComputeOutputsHash(chunkB));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void ComputeOutputsHash_BitwisePolicyDoubleAndHalf_CanonicalizesNaNsAndPreservesDistinctNonNaNBits()
    {
        const ulong DoubleSnanBits = 0x7FF0_0000_0000_0001UL;
        double doubleQnan = BitConverter.Int64BitsToDouble(0x7FF8_0000_0000_0002L);
        double doubleNegNan = BitConverter.Int64BitsToDouble(unchecked((long)0xFFF8_0000_0000_0003UL));

        var dA = new DiffBuffers().Out<double>("bd", 2, OutputPolicy.Bitwise);
        dA.Get<double>("bd")[0] = +0.0;
        MemoryMarshal.Cast<double, ulong>(dA.Get<double>("bd").AsSpan())[1] = DoubleSnanBits;

        var dB = new DiffBuffers().Out<double>("bd", 2, OutputPolicy.Bitwise);
        dB.Get<double>("bd")[0] = +0.0;
        dB.Get<double>("bd")[1] = doubleQnan;

        var dNegNan = new DiffBuffers().Out<double>("bd", 2, OutputPolicy.Bitwise);
        dNegNan.Get<double>("bd")[0] = +0.0;
        dNegNan.Get<double>("bd")[1] = doubleNegNan;

        var dNegZero = new DiffBuffers().Out<double>("bd", 2, OutputPolicy.Bitwise);
        dNegZero.Get<double>("bd")[0] = -0.0;
        dNegZero.Get<double>("bd")[1] = doubleQnan;

        string? dHash = DiffRecordWriter.ComputeOutputsHash(dA);
        Assert.Equal(dHash, DiffRecordWriter.ComputeOutputsHash(dB));
        Assert.Equal(dHash, DiffRecordWriter.ComputeOutputsHash(dNegNan));
        Assert.NotEqual(dHash, DiffRecordWriter.ComputeOutputsHash(dNegZero));

        var dExactA = new DiffBuffers().Out<double>("exd", 1, OutputPolicy.Exact);
        MemoryMarshal.Cast<double, ulong>(dExactA.Get<double>("exd").AsSpan())[0] = DoubleSnanBits;
        var dExactB = new DiffBuffers().Out<double>("exd", 1, OutputPolicy.Exact);
        dExactB.Get<double>("exd")[0] = doubleQnan;
        Assert.NotEqual(DiffRecordWriter.ComputeOutputsHash(dExactA), DiffRecordWriter.ComputeOutputsHash(dExactB));

        const ushort HalfSnanBits = 0x7C01;
        Half halfQnan = BitConverter.UInt16BitsToHalf(0x7E01);
        Half halfNegNan = BitConverter.UInt16BitsToHalf(0xFE02);

        var hA = new DiffBuffers().Out<Half>("bh", 2, OutputPolicy.Bitwise);
        hA.Get<Half>("bh")[0] = (Half)(+0.0f);
        MemoryMarshal.Cast<Half, ushort>(hA.Get<Half>("bh").AsSpan())[1] = HalfSnanBits;

        var hB = new DiffBuffers().Out<Half>("bh", 2, OutputPolicy.Bitwise);
        hB.Get<Half>("bh")[0] = (Half)(+0.0f);
        hB.Get<Half>("bh")[1] = halfQnan;

        var hNegNan = new DiffBuffers().Out<Half>("bh", 2, OutputPolicy.Bitwise);
        hNegNan.Get<Half>("bh")[0] = (Half)(+0.0f);
        hNegNan.Get<Half>("bh")[1] = halfNegNan;

        var hNegZero = new DiffBuffers().Out<Half>("bh", 2, OutputPolicy.Bitwise);
        hNegZero.Get<Half>("bh")[0] = (Half)(-0.0f);
        hNegZero.Get<Half>("bh")[1] = halfQnan;

        string? hHash = DiffRecordWriter.ComputeOutputsHash(hA);
        Assert.Equal(hHash, DiffRecordWriter.ComputeOutputsHash(hB));
        Assert.Equal(hHash, DiffRecordWriter.ComputeOutputsHash(hNegNan));
        Assert.NotEqual(hHash, DiffRecordWriter.ComputeOutputsHash(hNegZero));

        var hExactA = new DiffBuffers().Out<Half>("exh", 1, OutputPolicy.Exact);
        MemoryMarshal.Cast<Half, ushort>(hExactA.Get<Half>("exh").AsSpan())[0] = HalfSnanBits;
        var hExactB = new DiffBuffers().Out<Half>("exh", 1, OutputPolicy.Exact);
        hExactB.Get<Half>("exh")[0] = halfQnan;
        Assert.NotEqual(DiffRecordWriter.ComputeOutputsHash(hExactA), DiffRecordWriter.ComputeOutputsHash(hExactB));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void ComputeOutputsHash_MixedColumnPoliciesWithBitwise_CanonicalizesOnlyBitwiseColumns()
    {
        const uint NanABits = 0x7F80_0001u;
        float nanB = BitConverter.Int32BitsToSingle(unchecked((int)0xFFC0_0002u));

        // Case 1: [Exact, Bitwise] where Policy == policies[0] == Exact (AppendOutputBuffer path).
        var exBwA = new DiffBuffers()
            .Out<float>("m1", 2, [("exact", OutputPolicy.Exact), ("bitwise", OutputPolicy.Bitwise)]);
        var exBwASpan = MemoryMarshal.Cast<float, uint>(exBwA.Get<float>("m1").AsSpan());
        exBwASpan[0] = NanABits;
        exBwASpan[1] = NanABits;
        exBwA.Get<float>("m1")[2] = 1.5f;
        exBwA.Get<float>("m1")[3] = +0.0f;

        var exBwSame = new DiffBuffers()
            .Out<float>("m1", 2, [("exact", OutputPolicy.Exact), ("bitwise", OutputPolicy.Bitwise)]);
        MemoryMarshal.Cast<float, uint>(exBwSame.Get<float>("m1").AsSpan())[0] = NanABits;
        exBwSame.Get<float>("m1")[1] = nanB;
        exBwSame.Get<float>("m1")[2] = 1.5f;
        exBwSame.Get<float>("m1")[3] = +0.0f;

        var exBwDiffExactNan = new DiffBuffers()
            .Out<float>("m1", 2, [("exact", OutputPolicy.Exact), ("bitwise", OutputPolicy.Bitwise)]);
        exBwDiffExactNan.Get<float>("m1")[0] = nanB;
        exBwDiffExactNan.Get<float>("m1")[1] = nanB;
        exBwDiffExactNan.Get<float>("m1")[2] = 1.5f;
        exBwDiffExactNan.Get<float>("m1")[3] = +0.0f;

        string? hashExBw = DiffRecordWriter.ComputeOutputsHash(exBwA);
        Assert.Equal(hashExBw, DiffRecordWriter.ComputeOutputsHash(exBwSame));
        Assert.NotEqual(hashExBw, DiffRecordWriter.ComputeOutputsHash(exBwDiffExactNan));

        // Case 2: [Bitwise, Exact] where Policy == policies[0] == Bitwise (AppendOutputBuffer path).
        var bwExA = new DiffBuffers()
            .Out<float>("m2", 1, [("bitwise", OutputPolicy.Bitwise), ("exact", OutputPolicy.Exact)]);
        var bwExASpan = MemoryMarshal.Cast<float, uint>(bwExA.Get<float>("m2").AsSpan());
        bwExASpan[0] = NanABits;
        bwExASpan[1] = NanABits;

        var bwExSame = new DiffBuffers()
            .Out<float>("m2", 1, [("bitwise", OutputPolicy.Bitwise), ("exact", OutputPolicy.Exact)]);
        bwExSame.Get<float>("m2")[0] = nanB;
        MemoryMarshal.Cast<float, uint>(bwExSame.Get<float>("m2").AsSpan())[1] = NanABits;

        var bwExDiffExactNan = new DiffBuffers()
            .Out<float>("m2", 1, [("bitwise", OutputPolicy.Bitwise), ("exact", OutputPolicy.Exact)]);
        bwExDiffExactNan.Get<float>("m2")[0] = nanB;
        bwExDiffExactNan.Get<float>("m2")[1] = nanB;

        string? hashBwEx = DiffRecordWriter.ComputeOutputsHash(bwExA);
        Assert.Equal(hashBwEx, DiffRecordWriter.ComputeOutputsHash(bwExSame));
        Assert.NotEqual(hashBwEx, DiffRecordWriter.ComputeOutputsHash(bwExDiffExactNan));

        // Case 3: [Ignore, Bitwise, Exact] routed through AppendMixedColumnBuffer.
        var mixedA = new DiffBuffers().Out<float>(
            "m3",
            1,
            [("ignored", OutputPolicy.Ignore), ("bitwise", OutputPolicy.Bitwise), ("exact", OutputPolicy.Exact)]
        );
        mixedA.Get<float>("m3")[0] = 10.0f;
        var mixedASpan = MemoryMarshal.Cast<float, uint>(mixedA.Get<float>("m3").AsSpan());
        mixedASpan[1] = NanABits;
        mixedASpan[2] = NanABits;

        var mixedSame = new DiffBuffers().Out<float>(
            "m3",
            1,
            [("ignored", OutputPolicy.Ignore), ("bitwise", OutputPolicy.Bitwise), ("exact", OutputPolicy.Exact)]
        );
        mixedSame.Get<float>("m3")[0] = 999.0f;
        mixedSame.Get<float>("m3")[1] = nanB;
        MemoryMarshal.Cast<float, uint>(mixedSame.Get<float>("m3").AsSpan())[2] = NanABits;

        var mixedDiffExactNan = new DiffBuffers().Out<float>(
            "m3",
            1,
            [("ignored", OutputPolicy.Ignore), ("bitwise", OutputPolicy.Bitwise), ("exact", OutputPolicy.Exact)]
        );
        mixedDiffExactNan.Get<float>("m3")[0] = 999.0f;
        mixedDiffExactNan.Get<float>("m3")[1] = nanB;
        mixedDiffExactNan.Get<float>("m3")[2] = nanB;

        string? hashMixed = DiffRecordWriter.ComputeOutputsHash(mixedA);
        Assert.Equal(hashMixed, DiffRecordWriter.ComputeOutputsHash(mixedSame));
        Assert.NotEqual(hashMixed, DiffRecordWriter.ComputeOutputsHash(mixedDiffExactNan));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void ComputeOutputsHash_MultisetPolicyWithNaNs_CanonicalizesNaNsAcrossOrderAndPreservesSignedZeros()
    {
        const uint SingleSnanBits = 0x7F80_0001u;
        float singleQnan = BitConverter.Int32BitsToSingle(0x7FC0_0001);
        float singleNegQnan = BitConverter.Int32BitsToSingle(unchecked((int)0xFFC0_0002u));
        float singleCanonicalNan = BitConverter.Int32BitsToSingle(0x7FC0_0000);

        var floatA = new DiffBuffers().Out<float>("mf", 3, OutputPolicy.Multiset);
        floatA.Get<float>("mf")[0] = 1.0f;
        MemoryMarshal.Cast<float, uint>(floatA.Get<float>("mf").AsSpan())[1] = SingleSnanBits;
        floatA.Get<float>("mf")[2] = singleNegQnan;

        var floatB = new DiffBuffers().Out<float>("mf", 3, OutputPolicy.Multiset);
        floatB.Get<float>("mf")[0] = singleQnan;
        floatB.Get<float>("mf")[1] = 1.0f;
        floatB.Get<float>("mf")[2] = singleCanonicalNan;

        var floatASorted = floatA.Find("mf").CreateSortedCopy();
        var floatBSorted = floatB.Find("mf").CreateSortedCopy();
        Assert.True(floatASorted.CellsMatch(floatBSorted, 0));
        Assert.True(floatASorted.CellsMatch(floatBSorted, 1));
        Assert.True(floatASorted.CellsMatch(floatBSorted, 2));

        string? floatHashA = DiffRecordWriter.ComputeOutputsHash(floatA);
        Assert.Equal(floatHashA, DiffRecordWriter.ComputeOutputsHash(floatB));

        // Signed zeros remain distinct while still permutation-invariant alongside canonicalized NaNs.
        var floatNegZero = new DiffBuffers().Out<float>("mf", 3, OutputPolicy.Multiset);
        floatNegZero.Get<float>("mf")[0] = -0.0f;
        floatNegZero.Get<float>("mf")[1] = 1.0f;
        floatNegZero.Get<float>("mf")[2] = singleQnan;

        var floatPosZero = new DiffBuffers().Out<float>("mf", 3, OutputPolicy.Multiset);
        floatPosZero.Get<float>("mf")[0] = +0.0f;
        floatPosZero.Get<float>("mf")[1] = 1.0f;
        floatPosZero.Get<float>("mf")[2] = singleCanonicalNan;

        Assert.NotEqual(DiffRecordWriter.ComputeOutputsHash(floatNegZero), DiffRecordWriter.ComputeOutputsHash(floatPosZero));

        var floatBothZerosA = new DiffBuffers().Out<float>("mf", 3, OutputPolicy.Multiset);
        floatBothZerosA.Get<float>("mf")[0] = -0.0f;
        floatBothZerosA.Get<float>("mf")[1] = +0.0f;
        MemoryMarshal.Cast<float, uint>(floatBothZerosA.Get<float>("mf").AsSpan())[2] = SingleSnanBits;

        var floatBothZerosB = new DiffBuffers().Out<float>("mf", 3, OutputPolicy.Multiset);
        floatBothZerosB.Get<float>("mf")[0] = singleNegQnan;
        floatBothZerosB.Get<float>("mf")[1] = +0.0f;
        floatBothZerosB.Get<float>("mf")[2] = -0.0f;

        Assert.Equal(DiffRecordWriter.ComputeOutputsHash(floatBothZerosA), DiffRecordWriter.ComputeOutputsHash(floatBothZerosB));

        // double
        const ulong DoubleSnanBits = 0x7FF0_0000_0000_0001UL;
        double doubleQnan = BitConverter.Int64BitsToDouble(0x7FF8_0000_0000_0002L);
        double doubleNegQnan = BitConverter.Int64BitsToDouble(unchecked((long)0xFFF8_0000_0000_0003UL));
        double doubleCanonicalNan = BitConverter.Int64BitsToDouble(0x7FF8_0000_0000_0000L);

        var doubleA = new DiffBuffers().Out<double>("md", 3, OutputPolicy.Multiset);
        doubleA.Get<double>("md")[0] = 1.0;
        MemoryMarshal.Cast<double, ulong>(doubleA.Get<double>("md").AsSpan())[1] = DoubleSnanBits;
        doubleA.Get<double>("md")[2] = doubleNegQnan;

        var doubleB = new DiffBuffers().Out<double>("md", 3, OutputPolicy.Multiset);
        doubleB.Get<double>("md")[0] = doubleQnan;
        doubleB.Get<double>("md")[1] = 1.0;
        doubleB.Get<double>("md")[2] = doubleCanonicalNan;

        Assert.Equal(DiffRecordWriter.ComputeOutputsHash(doubleA), DiffRecordWriter.ComputeOutputsHash(doubleB));

        var doubleNegZero = new DiffBuffers().Out<double>("md", 2, OutputPolicy.Multiset);
        doubleNegZero.Get<double>("md")[0] = -0.0;
        doubleNegZero.Get<double>("md")[1] = doubleQnan;

        var doublePosZero = new DiffBuffers().Out<double>("md", 2, OutputPolicy.Multiset);
        doublePosZero.Get<double>("md")[0] = +0.0;
        doublePosZero.Get<double>("md")[1] = doubleCanonicalNan;

        Assert.NotEqual(DiffRecordWriter.ComputeOutputsHash(doubleNegZero), DiffRecordWriter.ComputeOutputsHash(doublePosZero));

        // Half
        const ushort HalfSnanBits = 0x7C01;
        Half halfQnan = BitConverter.UInt16BitsToHalf(0x7E01);
        Half halfNegQnan = BitConverter.UInt16BitsToHalf(0xFE02);
        Half halfCanonicalNan = BitConverter.UInt16BitsToHalf(0x7E00);

        var halfA = new DiffBuffers().Out<Half>("mh", 3, OutputPolicy.Multiset);
        halfA.Get<Half>("mh")[0] = (Half)1.0f;
        MemoryMarshal.Cast<Half, ushort>(halfA.Get<Half>("mh").AsSpan())[1] = HalfSnanBits;
        halfA.Get<Half>("mh")[2] = halfNegQnan;

        var halfB = new DiffBuffers().Out<Half>("mh", 3, OutputPolicy.Multiset);
        halfB.Get<Half>("mh")[0] = halfQnan;
        halfB.Get<Half>("mh")[1] = (Half)1.0f;
        halfB.Get<Half>("mh")[2] = halfCanonicalNan;

        Assert.Equal(DiffRecordWriter.ComputeOutputsHash(halfA), DiffRecordWriter.ComputeOutputsHash(halfB));

        var halfNegZero = new DiffBuffers().Out<Half>("mh", 2, OutputPolicy.Multiset);
        halfNegZero.Get<Half>("mh")[0] = (Half)(-0.0f);
        halfNegZero.Get<Half>("mh")[1] = halfQnan;

        var halfPosZero = new DiffBuffers().Out<Half>("mh", 2, OutputPolicy.Multiset);
        halfPosZero.Get<Half>("mh")[0] = (Half)(+0.0f);
        halfPosZero.Get<Half>("mh")[1] = halfCanonicalNan;

        Assert.NotEqual(DiffRecordWriter.ComputeOutputsHash(halfNegZero), DiffRecordWriter.ComputeOutputsHash(halfPosZero));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void ComputeOutputsHash_MixedColumnPoliciesWithMultiset_CanonicalizesMultisetAndBitwiseColumnsOnly()
    {
        const uint SingleSnanBits = 0x7F80_0001u;
        float singleQnan = BitConverter.Int32BitsToSingle(0x7FC0_0001);
        float singleNegQnan = BitConverter.Int32BitsToSingle(unchecked((int)0xFFC0_0002u));
        float singleCanonicalNan = BitConverter.Int32BitsToSingle(0x7FC0_0000);

        // Case 1: [Multiset, Bitwise, Exact] routed through TryAppendCanonicalNaNFloatingBytes.
        var colA = new DiffBuffers().Out<float>(
            "mc1",
            3,
            [("multiset", OutputPolicy.Multiset), ("bitwise", OutputPolicy.Bitwise), ("exact", OutputPolicy.Exact)]
        );
        var colAData = colA.Get<float>("mc1");
        var colABits = MemoryMarshal.Cast<float, uint>(colAData.AsSpan());
        colAData[0] = 3.0f;
        colABits[1] = SingleSnanBits;
        colABits[2] = SingleSnanBits;
        colABits[3] = SingleSnanBits;
        colAData[4] = 1.5f;
        colAData[5] = +0.0f;
        colAData[6] = singleNegQnan;
        colAData[7] = singleNegQnan;
        colAData[8] = 2.5f;

        var colSame = new DiffBuffers().Out<float>(
            "mc1",
            3,
            [("multiset", OutputPolicy.Multiset), ("bitwise", OutputPolicy.Bitwise), ("exact", OutputPolicy.Exact)]
        );
        var colSameData = colSame.Get<float>("mc1");
        var colSameBits = MemoryMarshal.Cast<float, uint>(colSameData.AsSpan());
        colSameData[0] = singleQnan;
        colSameData[1] = singleCanonicalNan;
        colSameBits[2] = SingleSnanBits;
        colSameData[3] = singleCanonicalNan;
        colSameData[4] = 1.5f;
        colSameData[5] = +0.0f;
        colSameData[6] = 3.0f;
        colSameData[7] = singleQnan;
        colSameData[8] = 2.5f;

        var colDiffExactNan = new DiffBuffers().Out<float>(
            "mc1",
            3,
            [("multiset", OutputPolicy.Multiset), ("bitwise", OutputPolicy.Bitwise), ("exact", OutputPolicy.Exact)]
        );
        var colDiffExactData = colDiffExactNan.Get<float>("mc1");
        colSameData.CopyTo(colDiffExactData, 0);
        colDiffExactData[2] = singleQnan;

        string? colHash = DiffRecordWriter.ComputeOutputsHash(colA);
        Assert.Equal(colHash, DiffRecordWriter.ComputeOutputsHash(colSame));
        Assert.NotEqual(colHash, DiffRecordWriter.ComputeOutputsHash(colDiffExactNan));

        // Case 2: [Ignore, Multiset, Exact] routed through AppendMixedColumnBuffer (float, double, Half).
        var ignA = new DiffBuffers().Out<float>(
            "mc2",
            2,
            [("ignore", OutputPolicy.Ignore), ("multiset", OutputPolicy.Multiset), ("exact", OutputPolicy.Exact)]
        );
        var ignAData = ignA.Get<float>("mc2");
        ignAData[0] = 10.0f;
        MemoryMarshal.Cast<float, uint>(ignAData.AsSpan())[1] = SingleSnanBits;
        MemoryMarshal.Cast<float, uint>(ignAData.AsSpan())[2] = SingleSnanBits;
        ignAData[3] = 20.0f;
        ignAData[4] = 1.0f;
        ignAData[5] = +0.0f;

        var ignSame = new DiffBuffers().Out<float>(
            "mc2",
            2,
            [("ignore", OutputPolicy.Ignore), ("multiset", OutputPolicy.Multiset), ("exact", OutputPolicy.Exact)]
        );
        var ignSameData = ignSame.Get<float>("mc2");
        ignSameData[0] = 999.0f;
        ignSameData[1] = 1.0f;
        MemoryMarshal.Cast<float, uint>(ignSameData.AsSpan())[2] = SingleSnanBits;
        ignSameData[3] = 888.0f;
        ignSameData[4] = singleNegQnan;
        ignSameData[5] = +0.0f;

        var ignDiffExactNan = new DiffBuffers().Out<float>(
            "mc2",
            2,
            [("ignore", OutputPolicy.Ignore), ("multiset", OutputPolicy.Multiset), ("exact", OutputPolicy.Exact)]
        );
        var ignDiffExactData = ignDiffExactNan.Get<float>("mc2");
        ignSameData.CopyTo(ignDiffExactData, 0);
        ignDiffExactData[2] = singleNegQnan;

        string? ignHash = DiffRecordWriter.ComputeOutputsHash(ignA);
        Assert.Equal(ignHash, DiffRecordWriter.ComputeOutputsHash(ignSame));
        Assert.NotEqual(ignHash, DiffRecordWriter.ComputeOutputsHash(ignDiffExactNan));

        // double and Half through AppendMixedColumnBuffer with [Ignore, Multiset, Exact]
        const ulong DoubleSnanBits = 0x7FF0_0000_0000_0001UL;
        double doubleNegQnan = BitConverter.Int64BitsToDouble(unchecked((long)0xFFF8_0000_0000_0003UL));
        var ignDoubleA = new DiffBuffers().Out<double>(
            "mc2d",
            2,
            [("ignore", OutputPolicy.Ignore), ("multiset", OutputPolicy.Multiset), ("exact", OutputPolicy.Exact)]
        );
        ignDoubleA.Get<double>("mc2d")[0] = 1.0;
        MemoryMarshal.Cast<double, ulong>(ignDoubleA.Get<double>("mc2d").AsSpan())[1] = DoubleSnanBits;
        MemoryMarshal.Cast<double, ulong>(ignDoubleA.Get<double>("mc2d").AsSpan())[2] = DoubleSnanBits;
        ignDoubleA.Get<double>("mc2d")[3] = 2.0;
        ignDoubleA.Get<double>("mc2d")[4] = -0.0;
        ignDoubleA.Get<double>("mc2d")[5] = 3.0;

        var ignDoubleSame = new DiffBuffers().Out<double>(
            "mc2d",
            2,
            [("ignore", OutputPolicy.Ignore), ("multiset", OutputPolicy.Multiset), ("exact", OutputPolicy.Exact)]
        );
        ignDoubleSame.Get<double>("mc2d")[0] = 777.0;
        ignDoubleSame.Get<double>("mc2d")[1] = -0.0;
        MemoryMarshal.Cast<double, ulong>(ignDoubleSame.Get<double>("mc2d").AsSpan())[2] = DoubleSnanBits;
        ignDoubleSame.Get<double>("mc2d")[3] = 666.0;
        ignDoubleSame.Get<double>("mc2d")[4] = doubleNegQnan;
        ignDoubleSame.Get<double>("mc2d")[5] = 3.0;

        Assert.Equal(DiffRecordWriter.ComputeOutputsHash(ignDoubleA), DiffRecordWriter.ComputeOutputsHash(ignDoubleSame));

        const ushort HalfSnanBits = 0x7C01;
        Half halfNegQnan = BitConverter.UInt16BitsToHalf(0xFE02);
        var ignHalfA = new DiffBuffers().Out<Half>(
            "mc2h",
            2,
            [("ignore", OutputPolicy.Ignore), ("multiset", OutputPolicy.Multiset), ("exact", OutputPolicy.Exact)]
        );
        ignHalfA.Get<Half>("mc2h")[0] = (Half)1.0f;
        MemoryMarshal.Cast<Half, ushort>(ignHalfA.Get<Half>("mc2h").AsSpan())[1] = HalfSnanBits;
        MemoryMarshal.Cast<Half, ushort>(ignHalfA.Get<Half>("mc2h").AsSpan())[2] = HalfSnanBits;
        ignHalfA.Get<Half>("mc2h")[3] = (Half)2.0f;
        ignHalfA.Get<Half>("mc2h")[4] = (Half)(-0.0f);
        ignHalfA.Get<Half>("mc2h")[5] = (Half)3.0f;

        var ignHalfSame = new DiffBuffers().Out<Half>(
            "mc2h",
            2,
            [("ignore", OutputPolicy.Ignore), ("multiset", OutputPolicy.Multiset), ("exact", OutputPolicy.Exact)]
        );
        ignHalfSame.Get<Half>("mc2h")[0] = (Half)777.0f;
        ignHalfSame.Get<Half>("mc2h")[1] = (Half)(-0.0f);
        MemoryMarshal.Cast<Half, ushort>(ignHalfSame.Get<Half>("mc2h").AsSpan())[2] = HalfSnanBits;
        ignHalfSame.Get<Half>("mc2h")[3] = (Half)666.0f;
        ignHalfSame.Get<Half>("mc2h")[4] = halfNegQnan;
        ignHalfSame.Get<Half>("mc2h")[5] = (Half)3.0f;

        Assert.Equal(DiffRecordWriter.ComputeOutputsHash(ignHalfA), DiffRecordWriter.ComputeOutputsHash(ignHalfSame));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void ComputeOutputsHash_AbsoluteTolerancePolicy_HashesRawBytesAndDetectsWithinToleranceDifference()
    {
        var policy = OutputPolicy.Absolute(0.01, "Absolute tolerance still requires identical machine-code bytes");

        var bufA = new DiffBuffers().Out<float>("abs", 2, policy);
        bufA.Get<float>("abs")[0] = 1.0f;
        bufA.Get<float>("abs")[1] = 2.0f;

        var bufSame = new DiffBuffers().Out<float>("abs", 2, policy);
        bufSame.Get<float>("abs")[0] = 1.0f;
        bufSame.Get<float>("abs")[1] = 2.0f;

        var bufWithinTol = new DiffBuffers().Out<float>("abs", 2, policy);
        bufWithinTol.Get<float>("abs")[0] = 1.001f;
        bufWithinTol.Get<float>("abs")[1] = 2.0f;

        Assert.True(bufA.Find("abs").CellsMatch(bufWithinTol.Find("abs"), 0));
        string? hashA = DiffRecordWriter.ComputeOutputsHash(bufA);
        Assert.Equal(hashA, DiffRecordWriter.ComputeOutputsHash(bufSame));
        Assert.NotEqual(hashA, DiffRecordWriter.ComputeOutputsHash(bufWithinTol));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void ComputeOutputsHash_PerColumnForStructWithoutBufferComparer_HashesFormattedCellsPerColumn()
    {
        PaddedPair left = default;
        PaddedPair right = default;
        MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref left, 1)).Fill(0xAA);
        MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref right, 1)).Fill(0x55);
        left.Tag = 7;
        left.Value = 42;
        right.Tag = 7;
        right.Value = 42;

        var structPolicy = OutputPolicy.ForStruct<PaddedPair>(static (ctx, e, a) =>
        {
            ctx.Field(nameof(PaddedPair.Tag), e.Tag, a.Tag, OutputPolicy.Exact);
            ctx.Field(nameof(PaddedPair.Value), e.Value, a.Value, OutputPolicy.Exact);
        });

        // Case 1: Column 0 is Exact, Column 1 is ForStruct (no buffer-level comparer).
        var colA = new DiffBuffers()
            .Out<PaddedPair>("cols", 1, [("exact", OutputPolicy.Exact), ("padded", structPolicy)]);
        var colB = colA.Clone();
        colA.Get<PaddedPair>("cols")[0] = new PaddedPair { Tag = 1, Value = 10 };
        colA.Get<PaddedPair>("cols")[1] = left;
        colB.Get<PaddedPair>("cols")[0] = new PaddedPair { Tag = 1, Value = 10 };
        colB.Get<PaddedPair>("cols")[1] = right;

        Assert.True(colA.Find("cols").CellsMatch(colB.Find("cols"), 1));
        string? hashA = DiffRecordWriter.ComputeOutputsHash(colA);
        Assert.Equal(hashA, DiffRecordWriter.ComputeOutputsHash(colB));

        right.Value = 43;
        colB.Get<PaddedPair>("cols")[1] = right;
        Assert.False(colA.Find("cols").CellsMatch(colB.Find("cols"), 1));
        Assert.NotEqual(hashA, DiffRecordWriter.ComputeOutputsHash(colB));

        // Case 2: Column 0 is ForStruct, Column 1 is Exact (ensures Policy = policies[0] does not leak into column 1).
        right.Value = 42;
        var flippedA = new DiffBuffers()
            .Out<PaddedPair>("flipped", 1, [("padded", structPolicy), ("exact", OutputPolicy.Exact)]);
        var flippedB = flippedA.Clone();
        flippedA.Get<PaddedPair>("flipped")[0] = left;
        flippedA.Get<PaddedPair>("flipped")[1] = new PaddedPair { Tag = 2, Value = 20 };
        flippedB.Get<PaddedPair>("flipped")[0] = right;
        flippedB.Get<PaddedPair>("flipped")[1] = new PaddedPair { Tag = 2, Value = 20 };

        Assert.True(flippedA.Find("flipped").CellsMatch(flippedB.Find("flipped"), 0));
        Assert.True(flippedA.Find("flipped").CellsMatch(flippedB.Find("flipped"), 1));
        string? flippedHashA = DiffRecordWriter.ComputeOutputsHash(flippedA);
        Assert.Equal(flippedHashA, DiffRecordWriter.ComputeOutputsHash(flippedB));

        flippedB.Get<PaddedPair>("flipped")[1] = left;
        Assert.False(flippedA.Find("flipped").CellsMatch(flippedB.Find("flipped"), 1));
        Assert.NotEqual(flippedHashA, DiffRecordWriter.ComputeOutputsHash(flippedB));
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(
        DiffPath.Native, 2u, 1u, 1u,
        "Index was outside the bounds of the array. in MyKernel at File.cs(10,5) in thread (1,0,0) of block (1,0,0)",
        "Index was outside the bounds of the array. in MyKernel at File.cs(10,5)"
    )]
    [InlineData(
        DiffPath.Native, 1u, 2u, 1u,
        "Count is out. in MyKernel at File.cs(7,3) in thread (0,0,0) of block (0,1,0) (Parameter 'count')",
        "Count is out. in MyKernel at File.cs(7,3) (Parameter 'count')"
    )]
    [InlineData(
        DiffPath.Native, 1u, 1u, 1u,
        "Index was outside the bounds of the array. in MyKernel at File.cs(10,5) in thread (1,0,0) of block (0,0,0)",
        "Index was outside the bounds of the array. in MyKernel at File.cs(10,5) in thread (1,0,0) of block (0,0,0)"
    )]
    [InlineData(
        DiffPath.Native, 1u, 1u, 1u,
        "Count is out. in MyKernel at File.cs(7,3) in thread (0,0,0) of block (0,0,0) (Parameter 'count')",
        "Count is out. in MyKernel at File.cs(7,3) in thread (0,0,0) of block (0,0,0) (Parameter 'count')"
    )]
    [InlineData(
        DiffPath.Managed, 2u, 1u, 1u,
        "Index was outside the bounds of the array. in MyKernel at File.cs(10,5) in thread (1,0,0) of block (1,0,0)",
        "Index was outside the bounds of the array. in MyKernel at File.cs(10,5) in thread (1,0,0) of block (1,0,0)"
    )]
    [InlineData(
        DiffPath.Cuda, 2u, 1u, 1u,
        "Index was outside the bounds of the array. in MyKernel at File.cs(10,5) in thread (1,0,0) of block (1,0,0)",
        "Index was outside the bounds of the array. in MyKernel at File.cs(10,5) in thread (1,0,0) of block (1,0,0)"
    )]
    [InlineData(
        DiffPath.Native, 2u, 1u, 1u,
        "Message with in thread (1,0,0) of block (0,0,0) followed by extra text",
        "Message with in thread (1,0,0) of block (0,0,0) followed by extra text"
    )]
    public void NormalizeExceptionMessage_MultiBlockNativeOnly_StripsThreadAndBlockSuffix(
        DiffPath path,
        uint gridX,
        uint gridY,
        uint gridZ,
        string rawMessage,
        string expectedMessage
    )
    {
        var shape = new LaunchShape(gridX, gridY, gridZ, 64, 1, 1, 1);
        string actual = DiffRecordWriter.NormalizeExceptionMessage(rawMessage, path, shape);
        Assert.Equal(expectedMessage, actual);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void TryCreate_NullEmptyOrWhitespace_ReturnsNull()
    {
        Assert.Null(DiffRecordWriter.TryCreate(null));
        Assert.Null(DiffRecordWriter.TryCreate(string.Empty));
        Assert.Null(DiffRecordWriter.TryCreate("   "));
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DiffRecordWriter.EnvironmentVariableName)))
        {
            Assert.Null(DiffRecordWriter.Active);
        }
        else
        {
            Assert.NotNull(DiffRecordWriter.Active);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void UnwrapException_WrappedInTargetInvocationAndAggregate_ReturnsInnerException()
    {
        var root = new DivideByZeroException("Attempted to divide by zero.");
        var agg = new AggregateException(new TargetInvocationException(root));
        var unwrapped = DiffRecordWriter.UnwrapException(agg);
        Assert.Same(root, unwrapped);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Write_ParallelCalls_AssignsUniqueOrdinalsAndValidJsonlSchema()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"gsharp-diff-writer-{Guid.NewGuid():N}");
        try
        {
            using (DiffRecordWriter.UseScopeForTests(tempDir))
            {
                var writer = DiffRecordWriter.Active;
                Assert.NotNull(writer);
                var shape = new LaunchShape(2, 1, 1, 32, 1, 1, 64);
                var buffers = new DiffBuffers().Out<int>("r", 4, OutputPolicy.Exact);

                const int TotalWrites = 16;
                Parallel.For(0, TotalWrites, i =>
                {
                    writer.Write(
                        "ParallelTests.Kernel",
                        seed: 42,
                        shapeIndex: 1,
                        DiffPath.Native,
                        shape,
                        buffers,
                        ptrBuffers: null,
                        exception: i == 0 ? new ArgumentOutOfRangeException("count", "Bad in thread (0,0,0) of block (1,0,0)") : null,
                        stackLaunches: 2
                    );
                });

                Assert.True(File.Exists(writer.FilePath));
                string[] lines = File.ReadAllLines(writer.FilePath);
                Assert.Equal(TotalWrites, lines.Length);

                var ordinals = new SortedSet<int>();
                bool sawException = false;
                foreach (string line in lines)
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    Assert.Equal("ParallelTests.Kernel", root.GetProperty("case").GetString());
                    Assert.Equal(42, root.GetProperty("seed").GetInt32());
                    Assert.Equal(1, root.GetProperty("shape").GetInt32());
                    Assert.Equal("Native", root.GetProperty("path").GetString());
                    Assert.Equal([2, 1, 1], root.GetProperty("grid").EnumerateArray().Select(static e => e.GetInt32()));
                    Assert.Equal([32, 1, 1], root.GetProperty("block").EnumerateArray().Select(static e => e.GetInt32()));
                    Assert.True(ordinals.Add(root.GetProperty("ordinal").GetInt32()));
                    Assert.StartsWith("sha256:", root.GetProperty("outputs").GetString(), StringComparison.Ordinal);
                    Assert.Equal(JsonValueKind.Null, root.GetProperty("ptrOutputs").ValueKind);
                    Assert.Equal(2L, root.GetProperty("stackLaunches").GetInt64());

                    var exProp = root.GetProperty("exception");
                    if (exProp.ValueKind != JsonValueKind.Null)
                    {
                        sawException = true;
                        Assert.Equal(typeof(ArgumentOutOfRangeException).FullName, exProp.GetProperty("type").GetString());
                        Assert.Equal("Bad (Parameter 'count')", exProp.GetProperty("message").GetString());
                    }
                }

                Assert.True(sawException);
                Assert.Equal(Enumerable.Range(0, TotalWrites), ordinals);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Evaluate_RecordingFlagControlsOutput_RecordsNormalAndExceptionPaths()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"gsharp-diff-runner-{Guid.NewGuid():N}");
        try
        {
            using (DiffRecordWriter.UseScopeForTests(tempDir))
            {
                var fake = FakeCase(DiffPath.Native);
                // Calling Evaluate(...) directly does not record even when a scope is active.
                var evalReport = fake.Evaluate([0]);
                Assert.False(evalReport.HasFailures);
                Assert.Empty(Directory.GetFiles(tempDir, "*.jsonl"));

                // Recording mode (used by DifferentialRunner.Run) records normal runs.
                var runReport = DifferentialRunner.EvaluateRecordingForTests(
                    fake.Case,
                    [0],
                    fake.Paths,
                    NoEntries
                );
                Assert.False(runReport.HasFailures);

                // Recording mode also records exception-expectation paths (with multi-block Native suffix stripped).
                var exceptionCase = new DifferentialCase("HarnessTests.FaultKernel")
                {
                    Shape = new LaunchShape(2, 1, 1, 4, 1, 1, 8),
                    Inputs = static (_, rows) => new DiffBuffers().Out<int>("r", rows, OutputPolicy.Exact),
                    Launch = static (b, _) =>
                    {
                        b.Get<int>("r")[0] = 5;
                        throw new TargetInvocationException(
                            new IndexOutOfRangeException("OOB in MyKernel at File.cs(1,1) in thread (0,0,0) of block (1,0,0)")
                        );
                    },
                    ExpectedException = static (_, _) => ExceptionPolicy.For<IndexOutOfRangeException>(DiffPath.Native),
                };
                var threadRunner = new DiffPathRunner(DiffPath.Native, static launch =>
                {
                    Exception? workerError = null;
                    var worker = new Thread(() =>
                    {
                        try
                        {
                            launch();
                        }
                        catch (Exception ex) when (ex is not OutOfMemoryException)
                        {
                            workerError = ex;
                        }
                    });
                    worker.Start();
                    worker.Join();
                    if (workerError is not null)
                    {
                        ExceptionDispatchInfo.Capture(workerError).Throw();
                    }
                });

                var exReport = DifferentialRunner.EvaluateRecordingForTests(
                    exceptionCase,
                    [7],
                    [threadRunner],
                    NoEntries
                );
                Assert.False(exReport.HasFailures, exReport.ToString());

                string[] files = Directory.GetFiles(tempDir, "*.jsonl");
                var file = Assert.Single(files);
                string[] lines = File.ReadAllLines(file);
                Assert.Equal(2, lines.Length);

                using var normalDoc = JsonDocument.Parse(lines[0]);
                Assert.Equal(FakeCaseName, normalDoc.RootElement.GetProperty("case").GetString());
                Assert.Equal(JsonValueKind.Null, normalDoc.RootElement.GetProperty("exception").ValueKind);

                using var exDoc = JsonDocument.Parse(lines[1]);
                Assert.Equal("HarnessTests.FaultKernel", exDoc.RootElement.GetProperty("case").GetString());
                Assert.Equal(
                    "OOB in MyKernel at File.cs(1,1)",
                    exDoc.RootElement.GetProperty("exception").GetProperty("message").GetString()
                );
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
