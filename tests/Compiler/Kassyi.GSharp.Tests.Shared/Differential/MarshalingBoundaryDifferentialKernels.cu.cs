#nullable enable
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.DeviceLibrary;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public struct DiffBoolPack
{
    public bool Flag0;
    public int Mid;
    public bool Flag1;
}

public struct DiffCharPack
{
    public char C0;
    public int Mid;
    public char C1;
}

public struct DiffNullableBlitHolder
{
    public int? IntOpt;
    public byte? ByteOpt;
    public char? CharOpt;
    public bool? BoolOpt;
}

public struct DiffNullableVec2Holder
{
    public Vector2? V;
    public Vector2? Rv;
    public int Tag;
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct DiffExplicitGapped
{
    [FieldOffset(0)]
    public byte Head;

    [FieldOffset(8)]
    public long Mid;

    [FieldOffset(16)]
    public int Tail;
}

[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct DiffFloatUintUnion
{
    [FieldOffset(0)]
    public float F;

    [FieldOffset(0)]
    public uint U;

    [FieldOffset(8)]
    public int Tag;
}

public struct DiffVec2Padded
{
    public float A;
    public Vector2 V;
}

[StructLayout(LayoutKind.Explicit)]
public struct DiffConvertExplicitUnion
{
    [FieldOffset(0)]
    public DiffVec2Padded VecArm;

    [FieldOffset(0)]
    public long RawArm;
}

[StructLayout(LayoutKind.Auto)]
public struct DiffAutoMixed
{
    public byte B1;
    public long L;
    public bool Flag;
    public short S;
    public char C;
}

public struct DiffAutoNested
{
    public int Header;
    public DiffAutoMixed Inner;
}

[InlineArray(3)]
public struct DiffInlineBool3
{
    private bool _e0;
}

[InlineArray(3)]
public struct DiffInlineChar3
{
    private char _e0;
}

[InlineArray(3)]
public struct DiffInlineVec2Padded3
{
    private DiffVec2Padded _e0;
}

public struct DiffInlineOuterPacket
{
    public int Tag;
    public DiffInlineBool3 Bools;
    public DiffInlineChar3 Chars;
    public DiffInlineVec2Padded3 Vecs;
}

public unsafe struct D3FixedBufferPacket
{
    public int Header;
    public fixed int Items[4];
    public fixed byte Bytes[4];
}

[InlineArray(2)]
public struct DiffInlineNullableVec2
{
    private Vector2? _e0;
}

public struct DiffInlineNullableVec2Holder
{
    public int Tag;
    public DiffInlineNullableVec2 Items;
}

public struct D3InnerPair
{
    public int Left;
    public float Right;
}

public struct D3OuterPacket
{
    public byte Tag;
    public D3InnerPair First;
    public Vector2 Vec2;
    public D3InnerPair Second;
}

public struct DiffVecAlignTorture
{
    public byte B0;
    public Vector3 V3;
    public byte B1;
    public Vector2 V2;
    public byte B2;
    public Vector4 V4;
}

public struct DiffExternalNestedHolder
{
    public byte Prefix;
    public PackedPair Packed;
    public Sized SizedItem;
    public SwapPair Swapped;
    public IntFloatUnion UnionItem;
}

/// <summary>
/// Differential kernels for Lane D3 (Stage 1 and Stage 2): unmanaged boundary marshaling across
/// value / <c>in</c> / <c>ref</c> / <c>out</c> parameters, non-<c>void</c> kernel return values,
/// arrays, nested structs, <c>LayoutKind.Explicit</c>, <c>LayoutKind.Auto</c>, <c>[InlineArray]</c>,
/// <c>fixed</c> buffers, <c>Nullable&lt;T&gt;</c>, raw pointers, and external assembly structs.
/// </summary>
public static partial class MarshalingBoundaryDifferentialKernels
{
    public static readonly string[] BoolProbeColumns =
    [
        "v_bit",
        "iv_bit",
        "arr_bit",
        "pack_sum",
    ];

    public static readonly string[] CharProbeColumns =
    [
        "char_val",
        "char_in",
        "char_arr",
        "char_pack_c1",
    ];

    public static readonly string[] NullableBlitProbeColumns =
    [
        "arr_has",
        "arr_val",
        "holder_mask",
        "holder_sum",
    ];

    public static readonly string[] NullableBlitScalarProbeColumns =
    [
        "v_has",
        "v_val",
        "rb_has",
        "rb_val",
    ];

    public static readonly string[] NullableConvertProbeColumns =
    [
        "v_has",
        "v_x",
        "v_y",
        "rv_x",
        "rv_y",
    ];

    public static readonly string[] ExplicitUnionProbeColumns =
    [
        "g_val_sum",
        "g_arr_sum",
        "u_flipped_bits",
        "u_tag",
    ];

    public static readonly string[] ConvertUnionProbeColumns =
    [
        "device_a",
        "device_vx",
        "device_vy",
    ];

    public static readonly string[] AutoLayoutProbeColumns =
    [
        "val_weighted",
        "in_weighted",
        "arr_weighted",
        "nested_weighted",
    ];

    public static readonly string[] InlineAndFixedProbeColumns =
    [
        "inline_vec_sum",
        "outer_char_sum",
        "fixed_pkt_sum",
        "fixed_arr_sum",
    ];

    public static readonly string[] InlineNullableVec2ProbeColumns =
    [
        "item0_has",
        "item0_x",
        "item0_y",
        "item1_has",
        "item1_x",
        "item1_y",
    ];

    public static readonly string[] VecAlignAndExternalProbeColumns =
    [
        "torture_val_sum",
        "torture_arr_sum",
        "ext_packed_sum",
        "ext_swap_diff",
        "ext_holder_sum",
    ];

    public static readonly string[] MultiRefAndPointerProbeColumns =
    [
        "ptr_read",
        "null_ptr_flag",
        "ro_span_empty_len",
        "constexpr_scaled",
    ];

    // D3-M1: bool across value, in, ref, out, bool[], return bool, and struct fields
    [GpuKernel]
    public static bool BoolAllPositionsKernel(
        bool v,
        in bool iv,
        ref bool rv,
        out bool ov,
        bool[] arr,
        ref DiffBoolPack pack,
        int[] probe)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        ov = iv ^ v;
        if (tid >= arr.Length)
        {
            return false;
        }

        bool elem = arr[tid];

        int o = tid * 4;
        probe[o + 0] = v ? 1 : 0;
        probe[o + 1] = iv ? 1 : 0;
        probe[o + 2] = elem ? 1 : 0;
        probe[o + 3] = 0;

        arr[tid] = elem ^ v;
        if (tid == 0)
        {
            bool f0 = pack.Flag0;
            bool f1 = pack.Flag1;
            int mid = pack.Mid;
            probe[o + 3] = (f0 ? 100 : 0) + (f1 ? 10 : 0) + mid;

            rv = !rv;
            pack.Flag0 = !f0;
            pack.Flag1 = f1 ^ iv;
            pack.Mid = mid + (v ? 7 : 3);
        }

        return (elem ^ iv) != v;
    }

    // D3-M2: char across value, in, ref, out, char[], return char, and struct fields
    [GpuKernel]
    public static char CharAllPositionsKernel(
        char v,
        in char iv,
        ref char rv,
        out char ov,
        char[] arr,
        ref DiffCharPack pack,
        int[] probe)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        ov = (char)(iv ^ 0x0055);
        if (tid >= arr.Length)
        {
            return '\0';
        }

        char elem = arr[tid];

        int o = tid * 4;
        probe[o + 0] = v;
        probe[o + 1] = iv;
        probe[o + 2] = elem;
        probe[o + 3] = 0;

        arr[tid] = (char)(elem ^ 0x00A5);
        if (tid == 0)
        {
            char c0 = pack.C0;
            char c1 = pack.C1;
            probe[o + 3] = c1;

            rv = (char)(rv ^ 0x0F0F);
            pack.C0 = (char)(c0 + 1);
            pack.C1 = (char)(c1 ^ 0x1234);
            pack.Mid += elem;
        }

        return (char)(elem ^ v);
    }

    // D3-M3a (Positions 2, 3, 4): Nullable<T> (H == D) in array, return value, and struct fields
    [GpuKernel]
    public static int? NullableBlitPositionsKernel(
        int?[] arr,
        ref DiffNullableBlitHolder h,
        int[] probe)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= arr.Length)
        {
            return default;
        }

        int? elem = arr[tid];

        int o = tid * 4;
        probe[o + 0] = elem.HasValue ? 1 : 0;
        probe[o + 1] = elem ?? -999;
        probe[o + 2] = 0;
        probe[o + 3] = 0;

        if (elem.HasValue)
        {
            arr[tid] = elem.Value + tid + 1;
        }
        else if ((tid & 1) == 0)
        {
            arr[tid] = tid * 10;
        }
        else
        {
            arr[tid] = default;
        }

        if (tid == 0)
        {
            int hInt = h.IntOpt ?? -10;
            int hByte = h.ByteOpt.HasValue ? h.ByteOpt.Value : -20;
            int hChar = h.CharOpt.HasValue ? h.CharOpt.Value : -30;
            int hBool = h.BoolOpt.HasValue ? (h.BoolOpt.Value ? 1 : 0) : -40;

            int mask = (h.IntOpt.HasValue ? 1 : 0)
                       | (h.ByteOpt.HasValue ? 2 : 0)
                       | (h.CharOpt.HasValue ? 4 : 0)
                       | (h.BoolOpt.HasValue ? 8 : 0);

            probe[o + 2] = mask;
            probe[o + 3] = hInt + hByte + hChar + hBool;

            h.IntOpt = h.IntOpt.HasValue ? h.IntOpt.Value + 5 : 42;
            if (h.ByteOpt.HasValue)
            {
                h.ByteOpt = (byte)(h.ByteOpt.Value ^ 0x0F);
            }
            else
            {
                h.ByteOpt = default;
            }

            h.CharOpt = h.CharOpt.HasValue ? (char)(h.CharOpt.Value + 1) : '\u1234';
            h.BoolOpt = h.BoolOpt.HasValue ? !h.BoolOpt.Value : true;
        }

        int? ret = default;
        if (!elem.HasValue)
        {
            ret = tid + 100;
        }
        else if ((elem.Value & 1) == 0)
        {
            ret = elem.Value * 2;
        }

        return ret;
    }

    // D3-M3a (Position 1): Nullable<T> direct scalar/byref parameters (#972 S19-3a on Native CPU)
    [GpuKernel]
    public static void NullableBlitScalarArgKernel(
        int? v,
        ref byte? rb,
        out char? oc,
        int[] probe)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (v.HasValue)
        {
            oc = (char)(0x1200 + (v.Value & 0xFF));
        }
        else
        {
            oc = default;
        }
        if (tid >= probe.Length / 4)
        {
            return;
        }

        int o = tid * 4;
        probe[o + 0] = v.HasValue ? 1 : 0;
        probe[o + 1] = v ?? -777;
        probe[o + 2] = 0;
        probe[o + 3] = 0;

        if (tid == 0)
        {
            byte? curRb = rb;
            probe[o + 2] = curRb.HasValue ? 1 : 0;
            probe[o + 3] = curRb.HasValue ? curRb.Value : -888;
            rb = curRb.HasValue ? (byte)(curRb.Value + 3) : (byte)17;
        }
    }

    // D3-M3b: Nullable<Vector2> (H != D, #961 S19-3b on CUDA)
    [GpuKernel]
    public static void NullableConvertPositionsKernel(
        ref DiffNullableVec2Holder h,
        float[] probe)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= probe.Length / 5)
        {
            return;
        }

        int o = tid * 5;
        probe[o + 0] = 0.0f;
        probe[o + 1] = 0.0f;
        probe[o + 2] = 0.0f;
        probe[o + 3] = 0.0f;
        probe[o + 4] = 0.0f;

        if (tid == 0)
        {
            Vector2? v = h.V;
            Vector2? rv = h.Rv;
            probe[o + 0] = v.HasValue ? 1.0f : 0.0f;
            probe[o + 1] = v.HasValue ? v.Value.X : -100.0f;
            probe[o + 2] = v.HasValue ? v.Value.Y : -200.0f;
            probe[o + 3] = rv.HasValue ? rv.Value.X : -300.0f;
            probe[o + 4] = rv.HasValue ? rv.Value.Y : -400.0f;

            if (v.HasValue)
            {
                h.Rv = new Vector2(v.Value.X + 10.0f, v.Value.Y - 10.0f);
            }
            else
            {
                h.Rv = new Vector2(7.0f, 8.0f);
            }

            h.Tag += 1;
        }
    }

    // D3-M4a: LayoutKind.Explicit (non-overlapping with gaps & Size=24, plus identical bit union)
    [GpuKernel]
    public static DiffFloatUintUnion ExplicitLayoutAndUnionPositionsKernel(
        DiffExplicitGapped gVal,
        ref DiffFloatUintUnion uRef,
        DiffExplicitGapped[] gArr,
        DiffFloatUintUnion[] uArr,
        int[] probe)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= gArr.Length)
        {
            return default;
        }

        DiffExplicitGapped gElem = gArr[tid];
        DiffFloatUintUnion uElem = uArr[tid];
        uint flippedBits = uElem.U ^ 0x8000_0000u;

        int o = tid * 4;
        probe[o + 0] = gVal.Head + (int)gVal.Mid + gVal.Tail;
        probe[o + 1] = gElem.Head + (int)gElem.Mid + gElem.Tail;
        probe[o + 2] = (int)flippedBits;
        probe[o + 3] = uElem.Tag;

        gArr[tid] = new DiffExplicitGapped
        {
            Head = (byte)(gElem.Head + 1),
            Mid = gElem.Mid + gVal.Mid,
            Tail = gElem.Tail ^ gVal.Tail,
        };

        DiffFloatUintUnion updatedUnion = new DiffFloatUintUnion
        {
            U = flippedBits,
            Tag = uElem.Tag + 10,
        };
        uArr[tid] = updatedUnion;

        if (tid == 0)
        {
            probe[o + 3] = uElem.Tag + uRef.Tag;
            uRef.U ^= 0x8000_0000u;
            uRef.Tag += 100;
        }

        return updatedUnion;
    }

    // D3-M4b: LayoutKind.Explicit union containing a Convert arm (#1006 on CUDA)
    [GpuKernel]
    public static void ExplicitUnionWithConvertArmKernel(
        ref DiffConvertExplicitUnion u,
        float[] probe)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= probe.Length / 3)
        {
            return;
        }

        int o = tid * 3;
        probe[o + 0] = 0.0f;
        probe[o + 1] = 0.0f;
        probe[o + 2] = 0.0f;

        if (tid == 0)
        {
            float a = u.VecArm.A;
            Vector2 v = u.VecArm.V;
            probe[o + 0] = a;
            probe[o + 1] = v.X;
            probe[o + 2] = v.Y;

            u.VecArm.A = a + 1.0f;
            u.VecArm.V = new Vector2(v.X + 2.5f, v.Y + 5.0f);
        }
    }

    // D3-M5: LayoutKind.Auto across value, in, ref, out, array, return value, and nested struct
    [GpuDeviceFunction]
    public static long WeightAutoMixed(DiffAutoMixed m) =>
        (m.B1 * 3L) + (m.L * 5L) + (m.Flag ? 11L : 0L) + (m.S * 17L) + ((long)m.C * 23L);

    [GpuKernel]
    public static DiffAutoMixed AutoLayoutAllPositionsKernel(
        DiffAutoMixed val,
        in DiffAutoMixed inVal,
        ref DiffAutoMixed refVal,
        out DiffAutoMixed outVal,
        DiffAutoMixed[] arr,
        ref DiffAutoNested nested,
        long[] probe)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        outVal = new DiffAutoMixed
        {
            B1 = (byte)(inVal.B1 + 1),
            L = inVal.L + val.L,
            Flag = !inVal.Flag,
            S = (short)(inVal.S + 2),
            C = (char)(inVal.C + 3),
        };

        if (tid >= arr.Length)
        {
            return default;
        }

        DiffAutoMixed elem = arr[tid];
        int o = tid * 4;
        probe[o + 0] = WeightAutoMixed(val);
        probe[o + 1] = WeightAutoMixed(inVal);
        probe[o + 2] = WeightAutoMixed(elem);
        probe[o + 3] = 0L;

        DiffAutoMixed updated = new DiffAutoMixed
        {
            B1 = (byte)(elem.B1 ^ val.B1),
            L = elem.L + inVal.L + tid,
            Flag = elem.Flag ^ val.Flag,
            S = (short)(elem.S + val.S),
            C = (char)(elem.C ^ 0x0020),
        };
        arr[tid] = updated;

        if (tid == 0)
        {
            probe[o + 3] = nested.Header + WeightAutoMixed(nested.Inner);

            refVal.B1 = (byte)(refVal.B1 + 7);
            refVal.L += 1000L;
            refVal.Flag = !refVal.Flag;
            refVal.S = (short)(refVal.S - 3);
            refVal.C = (char)(refVal.C + 5);

            nested.Header += 99;
            nested.Inner.L += 500L;
            nested.Inner.Flag = !nested.Inner.Flag;
        }

        return updated;
    }

    // D3-M6a: [InlineArray(3)] (bool, char, DiffVec2Padded) + fixed buffer D3FixedBufferPacket across 4 positions
    [GpuKernel]
    public static unsafe DiffInlineVec2Padded3 InlineArrayAndFixedBufferPositionsKernel(
        DiffInlineVec2Padded3 valVecs,
        ref DiffInlineOuterPacket outer,
        DiffInlineVec2Padded3[] vecArr,
        ref D3FixedBufferPacket fixedPkt,
        D3FixedBufferPacket[] fixedArr,
        int[] probe)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= vecArr.Length)
        {
            return default;
        }

        DiffInlineVec2Padded3 elemVecs = vecArr[tid];
        float vSum = elemVecs[0].A + elemVecs[0].V.X + elemVecs[1].A + elemVecs[1].V.Y + elemVecs[2].A + elemVecs[2].V.X
                     + valVecs[0].A + valVecs[2].V.Y;

        D3FixedBufferPacket fElem = fixedArr[tid];
        int fixedArrSum = fElem.Header
                          + fElem.Items[0] + fElem.Items[1] + fElem.Items[2] + fElem.Items[3]
                          + fElem.Bytes[0] + fElem.Bytes[3];

        int o = tid * 4;
        probe[o + 0] = (int)vSum;
        probe[o + 1] = 0;
        probe[o + 2] = 0;
        probe[o + 3] = fixedArrSum;

        // Swap [1] and [2] in elemVecs
        DiffVec2Padded tmp = elemVecs[1];
        elemVecs[1] = elemVecs[2];
        elemVecs[2] = tmp;
        elemVecs[0].A += tid;
        vecArr[tid] = elemVecs;

        fElem.Header += 10;
        int tmpFixed = fElem.Items[0];
        fElem.Items[0] = fElem.Items[3];
        fElem.Items[3] = tmpFixed + tid;
        fElem.Bytes[1] = (byte)(fElem.Bytes[1] ^ 0x5A);
        fixedArr[tid] = fElem;

        if (tid == 0)
        {
            int charSum = outer.Chars[0] + outer.Chars[1] + outer.Chars[2]
                          + (outer.Bools[0] ? 1 : 0) + (outer.Bools[1] ? 2 : 0) + (outer.Bools[2] ? 4 : 0);
            int fixedPktSum = fixedPkt.Header
                              + fixedPkt.Items[0] + fixedPkt.Items[1] + fixedPkt.Items[2] + fixedPkt.Items[3]
                              + fixedPkt.Bytes[0] + fixedPkt.Bytes[1] + fixedPkt.Bytes[2] + fixedPkt.Bytes[3];
            probe[o + 1] = charSum;
            probe[o + 2] = fixedPktSum;

            outer.Tag += 1;
            outer.Bools[0] = !outer.Bools[0];
            outer.Chars[1] = (char)(outer.Chars[1] + 2);
            DiffVec2Padded outerTmp = outer.Vecs[0];
            outer.Vecs[0] = outer.Vecs[2];
            outer.Vecs[2] = outerTmp;

            fixedPkt.Header += 77;
            fixedPkt.Items[2] += 100;
            fixedPkt.Bytes[0] = (byte)(fixedPkt.Bytes[0] + 1);
        }

        return elemVecs;
    }

    // D3-M6b: [InlineArray(2)] of Vector2? (#961 S19-3c block-copy corruption on CUDA)
    [GpuKernel]
    public static void InlineArrayNullableVec2Kernel(
        ref DiffInlineNullableVec2Holder h,
        float[] probe)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= probe.Length / 6)
        {
            return;
        }

        int o = tid * 6;
        probe[o + 0] = 0.0f;
        probe[o + 1] = 0.0f;
        probe[o + 2] = 0.0f;
        probe[o + 3] = 0.0f;
        probe[o + 4] = 0.0f;
        probe[o + 5] = 0.0f;

        if (tid == 0)
        {
            Vector2? e0 = h.Items[0];
            Vector2? e1 = h.Items[1];
            probe[o + 0] = e0.HasValue ? 1.0f : 0.0f;
            probe[o + 1] = e0.HasValue ? e0.Value.X : -1.0f;
            probe[o + 2] = e0.HasValue ? e0.Value.Y : -2.0f;
            probe[o + 3] = e1.HasValue ? 1.0f : 0.0f;
            probe[o + 4] = e1.HasValue ? e1.Value.X : -3.0f;
            probe[o + 5] = e1.HasValue ? e1.Value.Y : -4.0f;

            h.Tag += 1;
            h.Items[0] = e1;
            h.Items[1] = e0;
        }
    }

    // D3-M8_9: Vector2/3/4 alignment torture + D3InnerPair/D3OuterPacket + External assembly structs (DeviceLibrary)
    [GpuDeviceFunction]
    public static float SumTorture(DiffVecAlignTorture t) =>
        t.B0 + t.V3.X + t.V3.Y + t.V3.Z + t.B1 + t.V2.X + t.V2.Y + t.B2 + t.V4.X + t.V4.Y + t.V4.Z + t.V4.W;

    [GpuKernel]
    public static D3OuterPacket VectorAlignmentAndExternalStructsKernel(
        DiffVecAlignTorture tortVal,
        ref D3OuterPacket outerRef,
        DiffVecAlignTorture[] tortArr,
        PackedPair[] extPackedArr,
        SwapPair[] extSwapArr,
        ref DiffExternalNestedHolder extHolder,
        float[] probe)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= tortArr.Length)
        {
            return default;
        }

        DiffVecAlignTorture tElem = tortArr[tid];
        PackedPair pElem = extPackedArr[tid];
        SwapPair sElem = extSwapArr[tid];

        int o = tid * 5;
        probe[o + 0] = SumTorture(tortVal);
        probe[o + 1] = SumTorture(tElem);
        probe[o + 2] = pElem.Tag + pElem.Value;
        probe[o + 3] = sElem.A - sElem.B;
        probe[o + 4] = 0.0f;

        tElem.V3 = new Vector3(tElem.V3.X + 1.0f, tElem.V3.Y + 2.0f, tElem.V3.Z + 3.0f);
        tElem.V2 = new Vector2(tElem.V2.Y, tElem.V2.X);
        tElem.V4 = new Vector4(tElem.V4.W, tElem.V4.Z, tElem.V4.Y, tElem.V4.X);
        tortArr[tid] = tElem;

        extPackedArr[tid] = new PackedPair
        {
            Tag = (byte)(pElem.Tag + 1),
            Value = pElem.Value + tid,
        };
        extSwapArr[tid] = new SwapPair
        {
            A = sElem.B + 10,
            B = sElem.A - 10,
        };

        if (tid == 0)
        {
            probe[o + 4] = extHolder.Prefix + extHolder.Packed.Tag + extHolder.Packed.Value
                           + extHolder.SizedItem.A + (extHolder.Swapped.A - extHolder.Swapped.B)
                           + extHolder.UnionItem.Tag;

            outerRef.Tag = (byte)(outerRef.Tag + 1);
            outerRef.First = new D3InnerPair
            {
                Left = outerRef.First.Left + 5,
                Right = outerRef.First.Right + 1.5f,
            };
            outerRef.Vec2 = new Vector2(outerRef.Vec2.X + 2.0f, outerRef.Vec2.Y - 2.0f);

            extHolder.Prefix = (byte)(extHolder.Prefix + 3);
            extHolder.Packed.Value += 50;
            extHolder.SizedItem.A += 100;
            extHolder.Swapped = new SwapPair
            {
                A = extHolder.Swapped.B,
                B = extHolder.Swapped.A,
            };
            extHolder.UnionItem.Tag += 7;
        }

        return new D3OuterPacket
        {
            Tag = (byte)(pElem.Tag ^ tElem.B0),
            First = new D3InnerPair { Left = pElem.Value, Right = tElem.V2.X },
            Vec2 = tElem.V2,
            Second = new D3InnerPair { Left = sElem.A, Right = tElem.V3.Z },
        };
    }

    // Stage 1: Multiple ref/in/out scalars + raw pointer (valid & null) + empty Span slice + [GpuConstexpr] + [GpuReadOnly]
    [GpuKernel]
    public static unsafe void MultiRefScalarAndRawPointerKernel(
        in int inScalar,
        ref int refA,
        ref int refB,
        ref float refF,
        out int outSum,
        int* pValid,
        int* pNull,
        [GpuConstexpr(4)] int constFactor,
        [GpuReadOnly] int[] roTable,
        int[] probe)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        outSum = (inScalar * constFactor) + 11;
        if (tid >= roTable.Length)
        {
            return;
        }

        ReadOnlySpan<int> emptySlice = ((ReadOnlySpan<int>)probe).Slice(0, 0);
        int ptrVal = pValid != null ? pValid[tid] : -1;
        int nullFlag = pNull == null ? 1 : 0;

        int o = tid * 4;
        probe[o + 0] = ptrVal;
        probe[o + 1] = nullFlag;
        probe[o + 2] = emptySlice.Length;
        probe[o + 3] = (roTable[tid] + inScalar) * constFactor;

        if (pValid != null)
        {
            pValid[tid] = ptrVal + (inScalar * constFactor);
        }

        if (tid == 0)
        {
            int oldA = refA;
            refA = refB + inScalar;
            refB = oldA * constFactor;
            refF += 3.5f;
        }
    }
}
