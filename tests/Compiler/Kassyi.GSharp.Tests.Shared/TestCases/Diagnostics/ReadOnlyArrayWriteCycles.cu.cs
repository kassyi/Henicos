using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public struct ReadOnlyCycleHolder
{
    [GpuReadOnly] public uint[] Fixed;
}

public struct ReadOnlyCycleInner
{
    public uint[] Data;
}

[SkipSnapshotConsistency("Diagnostics case")]
public static partial class ReadOnlyArrayWriteCycleKernels
{
    [GpuKernel]
    public static void TemporarySwapReadOnlyParamsKernel([GpuReadOnly] uint[] a, [GpuReadOnly] uint[] b)
    {
        var t = a;
        a = b;
        b = t;
        a[0] = 1u;
    }

    [GpuKernel]
    public static void TupleSwapReadOnlyParamsKernel([GpuReadOnly] uint[] a, [GpuReadOnly] uint[] b)
    {
        (a, b) = (b, a);
        a[0] = 1u;
    }

    [GpuKernel]
    public static void CopyBackReadOnlyParamKernel([GpuReadOnly] uint[] a)
    {
        var t = a;
        a = t;
        a[0] = 1u;
    }

    [GpuKernel]
    public static void StructParamSwapKernel(ReadOnlyCycleHolder h1, ReadOnlyCycleHolder h2)
    {
        var t = h1;
        h1 = h2;
        h2 = t;
        h1.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void SelfAssignedReadOnlyParamKernel([GpuReadOnly] uint[] r)
    {
        r = r;
        r[0] = 1u;
    }

    [GpuKernel]
    public static void LongArrayCopyChainKernel([GpuReadOnly] uint[] r)
    {
        var a0 = r;
        var a1 = a0;
        var a2 = a1;
        var a3 = a2;
        var a4 = a3;
        var a5 = a4;
        var a6 = a5;
        var a7 = a6;
        var a8 = a7;
        var a9 = a8;
        var a10 = a9;
        var a11 = a10;
        var a12 = a11;
        var a13 = a12;
        var a14 = a13;
        var a15 = a14;
        var a16 = a15;
        var a17 = a16;
        var a18 = a17;
        var a19 = a18;
        var a20 = a19;
        var a21 = a20;
        var a22 = a21;
        var a23 = a22;
        var a24 = a23;
        var a25 = a24;
        var a26 = a25;
        var a27 = a26;
        var a28 = a27;
        var a29 = a28;
        var a30 = a29;
        var a31 = a30;
        var a32 = a31;
        var a33 = a32;
        var a34 = a33;
        var a35 = a34;
        var a36 = a35;
        var a37 = a36;
        var a38 = a37;
        var a39 = a38;
        var a40 = a39;
        var a41 = a40;
        var a42 = a41;
        var a43 = a42;
        var a44 = a43;
        var a45 = a44;
        var a46 = a45;
        var a47 = a46;
        var a48 = a47;
        var a49 = a48;
        var a50 = a49;
        var a51 = a50;
        var a52 = a51;
        var a53 = a52;
        var a54 = a53;
        var a55 = a54;
        var a56 = a55;
        var a57 = a56;
        var a58 = a57;
        var a59 = a58;
        var a60 = a59;
        var a61 = a60;
        var a62 = a61;
        var a63 = a62;
        var a64 = a63;
        var a65 = a64;
        var a66 = a65;
        var a67 = a66;
        var a68 = a67;
        var a69 = a68;
        var a70 = a69;
        a70[0] = 1u;
    }

    [GpuKernel]
    public static void LongStructCopyChainKernel([GpuReadOnly] uint[] r)
    {
        var s0 = new ReadOnlyCycleInner { Data = r };
        var s1 = s0;
        var s2 = s1;
        var s3 = s2;
        var s4 = s3;
        var s5 = s4;
        var s6 = s5;
        var s7 = s6;
        var s8 = s7;
        var s9 = s8;
        var s10 = s9;
        var s11 = s10;
        var s12 = s11;
        var s13 = s12;
        var s14 = s13;
        var s15 = s14;
        var s16 = s15;
        var s17 = s16;
        var s18 = s17;
        var s19 = s18;
        var s20 = s19;
        var s21 = s20;
        var s22 = s21;
        var s23 = s22;
        var s24 = s23;
        var s25 = s24;
        var s26 = s25;
        var s27 = s26;
        var s28 = s27;
        var s29 = s28;
        var s30 = s29;
        var s31 = s30;
        var s32 = s31;
        var s33 = s32;
        var s34 = s33;
        var s35 = s34;
        var s36 = s35;
        var s37 = s36;
        var s38 = s37;
        var s39 = s38;
        var s40 = s39;
        var s41 = s40;
        var s42 = s41;
        var s43 = s42;
        var s44 = s43;
        var s45 = s44;
        var s46 = s45;
        var s47 = s46;
        var s48 = s47;
        var s49 = s48;
        var s50 = s49;
        var s51 = s50;
        var s52 = s51;
        var s53 = s52;
        var s54 = s53;
        var s55 = s54;
        var s56 = s55;
        var s57 = s56;
        var s58 = s57;
        var s59 = s58;
        var s60 = s59;
        var s61 = s60;
        var s62 = s61;
        var s63 = s62;
        var s64 = s63;
        var s65 = s64;
        var s66 = s65;
        var s67 = s66;
        var s68 = s67;
        var s69 = s68;
        var s70 = s69;
        s70.Data[0] = 1u;
    }
}
