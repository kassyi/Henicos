using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches and checks the kernels of <see cref="PrimaryConstructorKernels"/> (#495). The native and managed test
/// classes share these scenarios and differ only in how a launch runs, so the three ways give the same values.
/// </summary>
internal static class PrimaryConstructorScenarios
{
    public const int Count = 4;

    public static void InitializersRun(Action<Action> run)
    {
        var output = new int[2 * Count];

        run(() => PrimaryConstructorKernels.LaunchInitializersKernel(output, blockDimX: Count));

        // X = a + 1 and Y = a * 2 with a = tid + 5: the initializers ran, a was not stored in X as it is.
        // mutant: StructTranspiler.WriteStructMemberInitializers `if (!TryGetMemberInitializer(...))` ->
        // `if (!TryGetMemberInitializer(...) || primaryConstructor != null)` -> X and Y stay 0 (6 native tests fail)
        for (int i = 0; i < Count; i++)
        {
            Assert.Equal(i + 6, output[2 * i]);
            Assert.Equal(2 * (i + 5), output[2 * i + 1]);
        }
    }

    public static void InitializersRunBeforeObjectInitializer(Action<Action> run)
    {
        var output = new int[2 * Count];

        run(() => PrimaryConstructorKernels.LaunchInitializersWithObjectInitializerKernel(output, blockDimX: Count));

        // The constructor runs first (X = a + 1), then the object initializer overwrites Y
        for (int i = 0; i < Count; i++)
        {
            Assert.Equal(i + 1, output[2 * i]);
            Assert.Equal(9, output[2 * i + 1]);
        }
    }

    public static void CapturedParametersAreStored(Action<Action> run)
    {
        var output = new int[3 * Count];

        run(() => PrimaryConstructorKernels.LaunchCapturedKernel(output, blockDimX: Count));

        // Sum = a + b + X with a = tid, b = 10, X = a; the setter writes the captured b, which Sum then reads.
        // mutant: StructTranspiler.TryGetPrimaryParameter `member.Kind == StructAbiMemberKind.CapturedParameter ||` ->
        // `false ||` -> the factory does not store a and b, which stay 0
        for (int i = 0; i < Count; i++)
        {
            Assert.Equal(i + 10 + i, output[3 * i]);
            Assert.Equal(7, output[3 * i + 1]);
            Assert.Equal(i + 7 + i, output[3 * i + 2]);
        }
    }

    public static void CapturedParametersCrossToTheDevice(Action<Action> run)
    {
        var data = new PcCaptured(3, 4);
        var output = new int[1];

        run(() => PrimaryConstructorKernels.LaunchCapturedThroughRefKernel(ref data, output, blockDimX: 1));

        // The host's hidden fields reach the device at the offsets the device reads, and come back
        Assert.Equal(3 + 4 + 3, output[0]);
        Assert.Equal(5, data.B);
        Assert.Equal(103, data.X);
        Assert.Equal(3 + 5 + 103, data.Sum);
    }

    public static void CapturedParametersMatchTheHostLayout(Action<Action> run, bool checkShadow)
    {
        var data = new PcCapturedMixed(2, 40);
        var layout = new long[3];

        run(() => PrimaryConstructorKernels.LaunchMeasureCapturedMixedKernel(ref data, layout, blockDimX: 1));

        // The device puts X where the CLR does (after <tag>P and <big>P) and has the CLR size
        // mutant: StructAbiMembers.MemberEnumerator.TryCreate `if (TryGetCapturedParameterName(field, ...))` ->
        // `if (false && ...)` -> the hidden fields leave the device struct, and its getters do not compile (C2065)
        Assert.Equal(HostOffsetOfX(ref data), layout[0]);
        Assert.Equal(Unsafe.SizeOf<PcCapturedMixed>(), layout[1]);
        Assert.Equal(16L, layout[0]);
        Assert.Equal(24L, layout[1]);
        Assert.Equal(2 + 40 + 3, layout[2]);

        if (checkShadow)
        {
            var shadow = typeof(PrimaryConstructorKernels).GetNestedType("AotContexts")?.GetNestedType(nameof(PcCapturedMixed));
            Assert.NotNull(shadow);
            Assert.Equal(24, Marshal.SizeOf(shadow));
            Assert.Equal(16, Marshal.OffsetOf(shadow, nameof(PcCapturedMixed.X)).ToInt32());
        }
    }

    public static void RecordStructInitializesItsProperties(Action<Action> run)
    {
        var output = new int[Count];

        run(() => PrimaryConstructorKernels.LaunchRecordKernel(output, blockDimX: Count));

        // X = tid, Y = 2, Z = X + Y
        for (int i = 0; i < Count; i++)
        {
            Assert.Equal(i * 100 + 20 + (i + 2), output[i]);
        }
    }

    public static void ChainedConstructorRunsThePrimaryOneFirst(Action<Action> run)
    {
        var output = new int[Count];

        run(() => PrimaryConstructorKernels.LaunchChainedKernel(output, blockDimX: Count));

        // this(a + b) sets X = (tid + 1) * 10, then the body adds 1
        for (int i = 0; i < Count; i++)
        {
            Assert.Equal((i + 1) * 10 + 1, output[i]);
        }
    }

    public static void ParameterlessPrimaryConstructorRunsInitializers(Action<Action> run)
    {
        var output = new int[Count];

        run(() => PrimaryConstructorKernels.LaunchNoParametersKernel(output, blockDimX: Count));

        // new PcNoParameters() runs X = 5; default leaves X = 0
        for (int i = 0; i < Count; i++)
        {
            Assert.Equal(500, output[i]);
        }
    }

    public static void CapturedParametersAndAutoPropertiesRoundtrip(Action<Action> run, bool checkShadow)
    {
        var data = new PcCapturedAutoProperty(3, 1.5);
        var output = new double[2];

        run(() => PrimaryConstructorKernels.LaunchRoundtripCapturedAutoPropertyKernel(ref data, output, blockDimX: 1));

        // The device reads tag, scale, First and Last where the host stored them: Scaled = 3 * 1.5 + 1 + 2
        Assert.Equal(7.5, output[0]);
        Assert.Equal(Unsafe.SizeOf<PcCapturedAutoProperty>(), (int)output[1]);
        Assert.Equal(24, (int)output[1]);

        // and writes them back to the same places
        Assert.Equal(2, data.First);
        Assert.Equal(12, data.Last);
        Assert.Equal(3.0, data.Scale);
        Assert.Equal(3 * 3.0 + 2 + 12, data.Scaled);

        if (checkShadow)
        {
            // The shadow struct puts each member at the offset of its CLR field, hidden fields included
            var shadow = typeof(PrimaryConstructorKernels).GetNestedType("AotContexts")?.GetNestedType(nameof(PcCapturedAutoProperty));
            Assert.NotNull(shadow);
            Assert.Equal(Unsafe.SizeOf<PcCapturedAutoProperty>(), Marshal.SizeOf(shadow));
            (string Shadow, string Clr, int Offset)[] fields =
            [
                ("tag", "<tag>P", 0),
                ("scale", "<scale>P", 8),
                ("First", "<First>k__BackingField", 16),
                ("Last", "<Last>k__BackingField", 20)
            ];
            foreach (var (shadowField, clrField, offset) in fields)
            {
                Assert.Equal(offset, Marshal.OffsetOf<PcCapturedAutoProperty>(clrField).ToInt32());
                Assert.Equal(offset, Marshal.OffsetOf(shadow, shadowField).ToInt32());
            }
        }
    }

    public static void ConvertedCapturedParameterRoundtrips(Action<Action> run)
    {
        var data = new PcConverted(3) { V = new System.Numerics.Vector2(1f, 2f), Flag = true };
        var output = new float[1];

        run(() => PrimaryConstructorKernels.LaunchRoundtripConvertedKernel(ref data, output, blockDimX: 1));

        // The device reads the hidden field a and the members after it where the host stored them: 3 + 10 + 200 + 1000.
        // On CUDA the launcher converts the struct through the host mirror (#495).
        // mutant: NativeStructEmitter.WriteHostMirror writes the captured a after the other members -> LayoutGuard throws
        // (16 / 12 bytes; the 4 converted tests fail on CUDA); with the size check also gone -> wrong values (they fail too).
        // With the size check disabled, the field offset check alone throws (ThrowFieldOffsetMismatch, 5 tests fail).
        // The size check alone removed changes the PrimaryConstructorKernels snapshot.
        Assert.Equal(1213f, output[0]);

        // and the converted values come back to the host fields
        Assert.Equal(4, data.A);
        Assert.Equal(new System.Numerics.Vector2(2f, 1f), data.V);
        Assert.False(data.Flag);
    }

    public static void SizedConvertedCapturedParameterRoundtrips(Action<Action> run)
    {
        var data = new PcSized(3) { V = new System.Numerics.Vector2(1f, 2f) };
        var output = new float[1];

        run(() => PrimaryConstructorKernels.LaunchRoundtripSizedKernel(ref data, output, blockDimX: 1));

        // The host mirror copies StructLayout.Size, so the size check passes and the values cross both ways
        // mutant: NativeStructEmitter.WriteHostMirror leaves out Size -> LayoutGuard throws (12 / 32 bytes) on CUDA
        Assert.Equal(32, Unsafe.SizeOf<PcSized>());
        Assert.Equal(3f + 10f + 200f, output[0]);
        Assert.Equal(4, data.A);
        Assert.Equal(new System.Numerics.Vector2(2f, 1f), data.V);
    }

    public static void NestedConvertedCapturedParameterRoundtrips(Action<Action> run)
    {
        var data = new PcConvertedOuter
        {
            Before = 5,
            Inner = new PcConverted(7) { V = new System.Numerics.Vector2(3f, 4f) }
        };
        var output = new float[1];

        run(() => PrimaryConstructorKernels.LaunchRoundtripConvertedOuterKernel(ref data, output, blockDimX: 1));

        Assert.Equal(7f + 30f + 400f + 5f, output[0]);
        Assert.Equal(6, data.Before);
        Assert.Equal(9, data.Inner.A);
        Assert.True(data.Inner.Flag);
        Assert.Equal(new System.Numerics.Vector2(3f, 4f), data.Inner.V);
    }

    public static void ConvertedCapturedParameterArrayRoundtrips(Action<Action> run)
    {
        PcConverted[] items =
        [
            new PcConverted(1) { V = new System.Numerics.Vector2(2f, 3f) },
            new PcConverted(4) { V = new System.Numerics.Vector2(5f, 6f), Flag = true }
        ];
        var output = new float[2];

        run(() => PrimaryConstructorKernels.LaunchRoundtripConvertedArrayKernel(items, output, blockDimX: 2));

        Assert.Equal([1f + 20f + 300f, 4f + 50f + 600f + 1000f], output);
        Assert.Equal(11, items[0].A);
        Assert.Equal(14, items[1].A);
        Assert.Equal(new System.Numerics.Vector2(3f, 3f), items[0].V);
        Assert.Equal(new System.Numerics.Vector2(6f, 6f), items[1].V);
        Assert.True(items[1].Flag);
    }

    public static void ConvertedCapturedParameterInInlineArrayRoundtrips(Action<Action> run)
    {
        var data = new PcConvertedPairHolder { Tag = 20 };
        data.Items[0] = new PcConverted(1) { V = new System.Numerics.Vector2(2f, 3f) };
        data.Items[1] = new PcConverted(4) { Flag = true };
        var output = new float[2];

        run(() => PrimaryConstructorKernels.LaunchRoundtripConvertedPairKernel(ref data, output, blockDimX: 1));

        Assert.Equal([1f + 20f + 300f, 4f + 1000f], output);
        Assert.Equal(1, data.Items[0].A);
        Assert.Equal(24, data.Items[1].A);
        Assert.True(data.Items[1].Flag);
    }

    public static void PartialStructRunsItsPrimaryConstructor(Action<Action> run)
    {
        var output = new long[3];

        run(() => PrimaryConstructorPartialKernels.LaunchCreatePartialKernel(output, blockDimX: 1));

        // X = a + 1 (first file), Y = 2 (second file), Read = a * 100 + X * 10 + Y reads the captured a
        Assert.Equal([4L, 2L, 342L], output);
    }

    public static void PartialStructMatchesTheHostLayout(Action<Action> run)
    {
        var data = new PcPartial(5);
        var layout = new long[4];

        run(() => PrimaryConstructorPartialKernels.LaunchRoundtripPartialKernel(ref data, layout, blockDimX: 1));

        // The members of both files sit where the CLR put them, after the hidden field of a
        Assert.Equal(HostOffset(ref data, ref data.X), layout[0]);
        Assert.Equal(HostOffset(ref data, ref data.Y), layout[1]);
        Assert.Equal(Unsafe.SizeOf<PcPartial>(), layout[2]);
        Assert.Equal(5 * 100 + 6 * 10 + 2, layout[3]);
        Assert.Equal(1006L, data.X);
        Assert.Equal(7, data.Y);
        Assert.Equal(5 * 100 + 1006 * 10 + 7, data.Read);
    }

    private static long HostOffset<TField>(ref PcPartial s, ref TField field) =>
        (long)Unsafe.ByteOffset(ref Unsafe.As<PcPartial, byte>(ref s), ref Unsafe.As<TField, byte>(ref field));

    private static long HostOffsetOfX(ref PcCapturedMixed s) =>
        (long)Unsafe.ByteOffset(ref Unsafe.As<PcCapturedMixed, byte>(ref s), ref Unsafe.As<int, byte>(ref s.X));
}
