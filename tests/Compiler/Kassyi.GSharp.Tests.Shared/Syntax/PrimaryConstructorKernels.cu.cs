using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

// The initializers use the parameter, so new PcScaled(a) must run them instead of storing a in X (#495)
public struct PcScaled(int a)
{
    public int X = a + 1;
    public int Y = a * 2;
}

// a and b are read by members, so Roslyn stores them in hidden fields (<a>P, <b>P), declared before X. X also
// copies a, which C# warns about (CS9124); the device must store both.
public struct PcCaptured(int a, int b)
{
#pragma warning disable CS9124 // a is captured and also initializes X, on purpose
    public int X = a;
#pragma warning restore CS9124

    public int Sum => a + b + X;

    public int B
    {
        get => b;
        set => b = value;
    }
}

// The hidden fields of the captured parameters take part in the layout: <tag>P at 0, <big>P at 8, X at 16, size 24
public struct PcCapturedMixed(byte tag, long big)
{
    public int X = 3;

    public long Tagged => tag + big + X;
}

// Captured parameters and auto-properties together: <tag>P at 0, <scale>P at 8, the backing fields of First at 16 and
// Last at 20, size 24
public struct PcCapturedAutoProperty(short tag, double scale)
{
    public byte First { get; set; } = 1;
    public int Last { get; set; } = 2;

    public double Scaled => tag * scale + First + Last;

    public double Scale
    {
        get => scale;
        set => scale = value;
    }
}

// On CUDA, Vector2 is 8-byte aligned on the device and 4-byte aligned on the host, so the launcher converts the struct
// member by member, the hidden field of the captured parameter a included: host <a>P at 0, V at 4, Flag at 12 (size 16);
// device a at 0, V at 8, Flag at 16 (size 24). On the CPU dialect both are the host layout.
public struct PcConverted(byte a)
{
    public System.Numerics.Vector2 V;
    public bool Flag;

    public float Read => a + V.X * 10f + V.Y * 100f + (Flag ? 1000f : 0f);

    public byte A
    {
        get => a;
        set => a = value;
    }
}

// StructLayout.Size makes the CLR struct 32 bytes; the CUDA launcher converts it (Vector2 is 8-byte aligned on the
// device), so the host mirror must be 32 bytes too
[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct PcSized(byte a)
{
    public System.Numerics.Vector2 V;

    public float Read => a + V.X * 10f + V.Y * 100f;

    public byte A
    {
        get => a;
        set => a = value;
    }
}

// The converted struct inside another one, which the launcher converts member by member too
public struct PcConvertedOuter
{
    public short Before;
    public PcConverted Inner;
}

// The converted struct as the element of an inline array, which the launcher converts element by element
[InlineArray(2)]
public struct PcConvertedPair
{
    private PcConverted _element0;
}

public struct PcConvertedPairHolder
{
    public int Tag;
    public PcConvertedPair Items;
}

// A positional record struct initializes its properties from the parameters, then the other members in order
public record struct PcRecord(int X, int Y)
{
    public int Z = X + Y;
}

// Another constructor chains to the primary one, which runs the initializers before the body
public struct PcChained(int a)
{
    public int X = a * 10;

    public PcChained(int a, int b) : this(a + b)
    {
        X += 1;
    }
}

// new PcNoParameters() calls the primary constructor and runs the initializers; default does not
public struct PcNoParameters()
{
    public int X = 5;
    public int Y;
}

public static partial class PrimaryConstructorKernels
{
    [GpuKernel]
    public static void InitializersKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var s = new PcScaled(tid + 5);
        output[2 * tid] = s.X;
        output[2 * tid + 1] = s.Y;
    }

    [GpuKernel]
    public static void InitializersWithObjectInitializerKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var s = new PcScaled(tid) { Y = 9 };
        output[2 * tid] = s.X;
        output[2 * tid + 1] = s.Y;
    }

    [GpuKernel]
    public static void CapturedKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var s = new PcCaptured(tid, 10);
        output[3 * tid] = s.Sum;
        s.B = 7;
        output[3 * tid + 1] = s.B;
        output[3 * tid + 2] = s.Sum;
    }

    [GpuKernel]
    public static void CapturedThroughRefKernel(ref PcCaptured data, int[] output)
    {
        output[0] = data.Sum;
        data.B = data.B + 1;
        data.X = data.X + 100;
    }

    [GpuKernel]
    public static void MeasureCapturedMixedKernel(ref PcCapturedMixed data, long[] layout)
    {
        unsafe
        {
            PcCapturedMixed s = default;
            PcCapturedMixed* p = &s;
            layout[0] = (long)((byte*)&s.X - (byte*)p);
            layout[1] = (long)((byte*)(p + 1) - (byte*)p);
        }

        layout[2] = data.Tagged;
    }

    [GpuKernel]
    public static void RoundtripCapturedAutoPropertyKernel(ref PcCapturedAutoProperty data, double[] output)
    {
        unsafe
        {
            PcCapturedAutoProperty s = default;
            PcCapturedAutoProperty* p = &s;
            output[1] = (double)((byte*)(p + 1) - (byte*)p);
        }

        output[0] = data.Scaled;
        data.First = (byte)(data.First + 1);
        data.Last = data.Last + 10;
        data.Scale = data.Scale * 2.0;
    }

    [GpuKernel]
    public static void RoundtripConvertedKernel(ref PcConverted data, float[] output)
    {
        output[0] = data.Read;
        data.A = (byte)(data.A + 1);
        data.V = new System.Numerics.Vector2(data.V.Y, data.V.X);
        data.Flag = !data.Flag;
    }

    [GpuKernel]
    public static void RoundtripConvertedOuterKernel(ref PcConvertedOuter data, float[] output)
    {
        output[0] = data.Inner.Read + data.Before;
        data.Before = (short)(data.Before + 1);
        data.Inner.A = (byte)(data.Inner.A + 2);
        data.Inner.Flag = !data.Inner.Flag;
    }

    [GpuKernel]
    public static void RoundtripConvertedArrayKernel(PcConverted[] items, float[] output)
    {
        int i = ThreadIndex.X;
        if (i < items.Length)
        {
            output[i] = items[i].Read;
            items[i].A = (byte)(items[i].A + 10);
            items[i].V = new System.Numerics.Vector2(items[i].V.X + 1f, items[i].V.Y);
        }
    }

    [GpuKernel]
    public static void RoundtripConvertedPairKernel(ref PcConvertedPairHolder data, float[] output)
    {
        output[0] = data.Items[0].Read;
        output[1] = data.Items[1].Read;
        data.Items[1].A = (byte)(data.Items[1].A + data.Tag);
    }

    [GpuKernel]
    public static void RoundtripSizedKernel(ref PcSized data, float[] output)
    {
        output[0] = data.Read;
        data.A = (byte)(data.A + 1);
        data.V = new System.Numerics.Vector2(data.V.Y, data.V.X);
    }

    [GpuKernel]
    public static void RecordKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var r = new PcRecord(tid, 2);
        output[tid] = r.X * 100 + r.Y * 10 + r.Z;
    }

    [GpuKernel]
    public static void ChainedKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var s = new PcChained(tid, 1);
        output[tid] = s.X;
    }

    [GpuKernel]
    public static void NoParametersKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var created = new PcNoParameters();
        PcNoParameters defaulted = default;
        output[tid] = created.X * 100 + created.Y * 10 + defaulted.X;
    }
}
