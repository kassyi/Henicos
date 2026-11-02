#nullable enable
using System;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

// --- D3-M7 / D3-A2 / D3-A3 / D3-A4 / D3-D1 / D3-N2 / D3-N3 / D3-N4: Deep Structs ---

public struct DiffDeepBag
{
    public int[] Data;
    public int Tag;
}

public struct DiffInnerBag
{
    public float[] Weights;
    public int Bias;
}

public struct DiffNestedDeep
{
    public int[] Indices;
    public DiffInnerBag Inner;
    public int Scale;
}

public struct DiffPrimaryCtorBag(int scale, Vector2 bias, int[] data)
{
    public int[] Data = data;

    public float Eval0 => (Data[0] * scale) + bias.X + bias.Y;

    public float Eval1 => (Data[1] * scale) + bias.X + bias.Y;

    public int Scale
    {
        get => scale;
        set => scale = value;
    }

    public Vector2 Bias
    {
        get => bias;
        set => bias = value;
    }
}

public struct DiffArrayPair
{
    public int[] First;
    public int[] Second;
}

public struct DiffNullableDeepBag
{
    public int[]? Data;
    public int Fallback;
}

public static partial class MarshalingAliasingDifferentialKernels
{
    // --- D3-M7a: Deep Struct (with nested Deep Struct and multiple array fields) across value, in, ref ---
    [GpuKernel]
    public static void DeepStructAllArgModesKernel(
        DiffNestedDeep byVal,
        in DiffNestedDeep byIn,
        ref DiffNestedDeep byRef,
        [Out] float[] outSums)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid == 0)
        {
            int idx0 = byVal.Indices[0];
            int idx1 = byIn.Indices[1];
            float w0 = byVal.Inner.Weights[idx0] * byVal.Scale;
            float w1 = byIn.Inner.Weights[idx1] * byIn.Scale;
            outSums[0] = w0 + byVal.Inner.Bias;
            outSums[1] = w1 + byIn.Inner.Bias;

            byRef.Indices[0] = byRef.Indices[0] + 10;
            byRef.Inner.Weights[0] = byRef.Inner.Weights[0] + 2.5f;
            byRef.Inner.Bias += 3;
            byRef.Scale *= 2;
            outSums[2] = byRef.Inner.Weights[0] + byRef.Indices[0] + byRef.Inner.Bias + byRef.Scale;
        }
    }

    // --- D3-M7b: Deep Struct across all 6 array-like container shapes ---
    [GpuKernel]
    public static void DeepStructSixContainersKernel(
        DiffDeepBag[] arrBags,
        Span<DiffDeepBag> spanBags,
        ReadOnlySpan<DiffDeepBag> roSpanBags,
        ArraySegment<DiffDeepBag> segBags,
        Memory<DiffDeepBag> memBags,
        ImmutableArray<DiffDeepBag> immBags,
        [Out] int[] outTotals)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid == 0)
        {
            arrBags[0].Data[0] += 100;
            arrBags[0].Tag += 1;

            spanBags[0].Data[0] += 200;
            spanBags[0].Tag += 2;

            int roVal = roSpanBags[0].Data[0] + roSpanBags[0].Tag;

            DiffDeepBag segElem = segBags[0];
            segElem.Data[0] += 300;
            segElem.Tag += 3;
            segBags[0] = segElem;

            memBags.Span[0].Data[0] += 400;
            memBags.Span[0].Tag += 4;

            int immVal = immBags[0].Data[0] + immBags[0].Tag;

            outTotals[0] = arrBags[0].Data[0] + arrBags[0].Tag;
            outTotals[1] = spanBags[0].Data[0] + spanBags[0].Tag;
            outTotals[2] = roVal;
            outTotals[3] = segBags[0].Data[0] + segBags[0].Tag + segBags.Count;
            outTotals[4] = memBags.Span[0].Data[0] + memBags.Span[0].Tag;
            outTotals[5] = immVal + immBags.AsSpan().Length;
        }
    }

    // --- D3-M7c: C# 12 Primary Constructor Deep Struct (#513) ---
    [GpuKernel]
    public static void PrimaryCtorDeepStructKernel(
        DiffPrimaryCtorBag byVal,
        ref DiffPrimaryCtorBag byRef,
        DiffPrimaryCtorBag[] arr,
        [Out] float[] outEval)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid == 0)
        {
            outEval[0] = byVal.Eval0;
            byRef.Data[0] += 5;
            byRef.Scale = byRef.Scale + 1;
            outEval[1] = byRef.Eval0;
            arr[0].Data[1] += 7;
            outEval[2] = arr[0].Eval1;
        }
    }

    // --- D3-A1: Top-level array aliasing across 4 attribute combinations ---
    [GpuKernel]
    public static void AliasTwoRwArraysKernel(int[] a, int[] b, [Out] int[] observed)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid < a.Length && tid < b.Length && tid < observed.Length)
        {
            int before = b[tid];
            a[tid] = before + 10;
            int afterA = b[tid];
            b[tid] = afterA + 100;
            observed[tid] = (before * 1000) + afterA;
        }
    }

    [GpuKernel]
    public static void AliasInAndRwArrayKernel([In] int[] aIn, int[] bRw, [Out] int[] observed)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid < aIn.Length && tid < bRw.Length && tid < observed.Length)
        {
            int before = aIn[tid];
            bRw[tid] = before + 50;
            int afterWrite = aIn[tid];
            observed[tid] = before + afterWrite;
        }
    }

    [GpuKernel]
    public static void AliasOutAndInArrayKernel([Out] int[] aOut, [In] int[] bIn, [Out] int[] observed)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid < aOut.Length && tid < bIn.Length && tid < observed.Length)
        {
            int inBefore = bIn[tid];
            aOut[tid] = inBefore + 77;
            int inAfter = bIn[tid];
            observed[tid] = inBefore + inAfter;
        }
    }

    [GpuKernel]
    public static void AliasTwoInArraysKernel([In] int[] aIn, [In] int[] bIn, [Out] int[] observed)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid < aIn.Length && tid < bIn.Length && tid < observed.Length)
        {
            aIn[tid] = aIn[tid] + 999;
            observed[tid] = aIn[tid] + bIn[tid];
        }
    }

    // --- D3-A1 (Span overlap #857 & ref scalar alias): Overlapping Span slices and aliased ref scalars ---
    [GpuKernel]
    public static void OverlappingSpanSliceKernel(
        Span<int> sliceA,
        Span<int> sliceB,
        [Out] int[] observed)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid == 0)
        {
            for (int i = 0; i < sliceA.Length; i++)
            {
                sliceA[i] = sliceA[i] + 10;
            }

            for (int i = 0; i < sliceB.Length; i++)
            {
                observed[i] = sliceB[i];
                sliceB[i] = sliceB[i] + 100;
            }
        }
    }

    [GpuKernel]
    public static void AliasedRefScalarsKernel(ref int a, ref int b, [Out] int[] observed)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid == 0)
        {
            a += 10;
            b += 20;
            observed[0] = a;
            observed[1] = b;
        }
    }

    // --- D3-A2: Deep Struct internal array aliasing (#707) ---
    [GpuKernel]
    public static void DeepStructInternalAliasKernel(
        ref DiffArrayPair pair,
        DiffDeepBag bagA,
        DiffDeepBag bagB,
        DiffDeepBag[] bagArray,
        [Out] int[] observed)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid == 0)
        {
            pair.First[0] += 10;
            int seenViaSecond = pair.Second[0];
            pair.Second[0] += 20;

            bagA.Data[0] += 100;
            int seenViaBagB = bagB.Data[0];

            bagArray[0].Data[0] += 1000;
            int seenViaElem1 = bagArray[1].Data[0];

            observed[0] = seenViaSecond;
            observed[1] = pair.First[0];
            observed[2] = seenViaBagB;
            observed[3] = seenViaElem1;
        }
    }

    // --- D3-A3: Cross-level aliasing between top-level array and Deep Struct field ---
    [GpuKernel]
    public static void CrossLevelTopAndDeepStructKernel(
        [In] int[] topReadOnly,
        int[] topWritable,
        DiffDeepBag bagByVal,
        ref DiffDeepBag bagByRef,
        [Out] int[] observed)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid == 0)
        {
            // bagByVal.Data aliases topReadOnly; writing through bagByVal.Data mutates the underlying array!
            int roBefore = topReadOnly[0];
            bagByVal.Data[0] = roBefore + 40;
            int roAfter = topReadOnly[0];

            // bagByRef.Data aliases topWritable; writing through both updates the shared array.
            topWritable[0] += 5;
            int seenInBagRef = bagByRef.Data[0];
            bagByRef.Data[0] += 15;

            observed[0] = roBefore;
            observed[1] = roAfter;
            observed[2] = seenInBagRef;
            observed[3] = topWritable[0];
        }
    }

    // --- D3-A4: Cross-aliasing between [In] DeepStruct[] element field and writable int[] (#985 / S18) ---
    [GpuKernel]
    public static void CrossAliasInDeepArrayAndRwArrayKernel(
        [In] DiffDeepBag[] inBags,
        int[] rwArr,
        [Out] int[] observed)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid == 0)
        {
            int before = inBags[0].Data[0];
            rwArr[0] = before + 500;
            int after = inBags[0].Data[0];
            observed[0] = before;
            observed[1] = after;
        }
    }

    // --- D3-D1: [In] array mutation isolation ---
    [GpuKernel]
    public static void InArrayMutationIsolationKernel(
        [In] int[] inPrimitives,
        [In] DiffVec2Padded[] inConvertStructs,
        [In] DiffNestedDeep[] inDeepStructs,
        [Out] float[] outObserved)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid == 0)
        {
            inPrimitives[0] += 999;
            inConvertStructs[0].A += 888.0f;
            inConvertStructs[0].V = new Vector2(77.0f, 88.0f);
            inDeepStructs[0].Scale += 555;
            inDeepStructs[0].Inner.Bias += 444;
            inDeepStructs[0].Indices[0] += 333;
            inDeepStructs[0].Inner.Weights[0] += 222.0f;

            outObserved[0] = inPrimitives[0];
            outObserved[1] = inConvertStructs[0].A + inConvertStructs[0].V.X;
            outObserved[2] = inDeepStructs[0].Scale + inDeepStructs[0].Inner.Bias + inDeepStructs[0].Indices[0] + inDeepStructs[0].Inner.Weights[0];
        }
    }

    // --- D3-D2: [Out] array full write vs partial write (Q10 = a) ---
    [GpuKernel]
    public static void OutArrayStridedWriteKernel(
        [In] int[] src,
        [Out] int[] dstPrim,
        [Out] DiffVec2Padded[] dstConvert,
        int writeStride)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid < src.Length && (tid % writeStride) == 0)
        {
            dstPrim[tid] = (src[tid] * 10) + 1;
            dstConvert[tid] = new DiffVec2Padded
            {
                A = src[tid] + 0.25f,
                V = new Vector2(src[tid] * 1.5f, src[tid] * -0.5f),
            };
        }
    }

    // --- D3-P1 / D3-P2 / D3-P3: Launch{Kernel}Ptr with Length guard (#720), GpuBuffer overload, & Convert/Deep Struct ---
    [GpuKernel]
    public static void PtrLengthGuardedScaleKernel(int[] data, int factor, [Out] int[] outLen)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid == 0)
        {
            outLen[0] = data.Length;
        }

        if (tid < data.Length)
        {
            data[tid] = (data[tid] * factor) + 1;
        }
    }

    [GpuKernel]
    public static void OverloadedPtrKernel(int[] src, [Out] int[] dst, int count)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid < count)
        {
            dst[tid] = (src[tid] * 3) + count;
        }
    }

    [GpuKernel]
    public static void OverloadedPtrKernel(float[] src, [Out] float[] dst, int count)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid < count)
        {
            dst[tid] = (src[tid] * 1.5f) + count;
        }
    }

    [GpuKernel]
    public static void PtrConvertStructAndRefKernel(
        int[] items,
        ref DiffVec2Padded accum,
        int count)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid < count)
        {
            items[tid] = (items[tid] * 2) + count;
        }

        if (tid == 0)
        {
            accum.A += count;
            accum.V = new Vector2(accum.V.X + 2.0f, accum.V.Y - 1.0f);
        }
    }

    [GpuKernel]
    public static void PtrDeepStructNotSupportedKernel(DiffDeepBag[] bags, [Out] int[] output)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid == 0)
        {
            output[0] = bags[0].Tag;
        }
    }

    // --- D3-N1 / D3-N2: Non-nullable vs Nullable top-level array null / empty (#806, #838) ---
    [GpuKernel]
    public static void NonNullableArrayKernel(int[] input, [Out] int[] output)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid < input.Length && tid < output.Length)
        {
            output[tid] = input[tid] + 1;
        }
    }

    [GpuKernel]
    public static void NullableTopLevelArrayKernel(
        int[]? optPrim,
        [In] DiffDeepBag[]? optDeep,
        [Out] int[] output)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid == 0)
        {
            output[0] = optPrim is null ? -1 : 1;
            output[1] = optPrim == null ? -1 : 1;
#pragma warning disable CS8602
            output[2] = optPrim.Length;
#pragma warning restore CS8602
            output[3] = optDeep is null ? -1 : 1;
#pragma warning disable CS8602
            output[4] = optDeep.Length;
#pragma warning restore CS8602
            if (optDeep is not null && optDeep.Length > 0 && optDeep[0].Data is not null && optDeep[0].Data.Length > 0)
            {
                output[5] = optDeep[0].Data[0] + optDeep[0].Tag;
            }
            else
            {
                output[5] = -99;
            }
        }
    }

    // --- D3-N3 / D3-N4: Struct field null array vs empty array (#706) & null dereference ---
    [GpuKernel]
    public static void StructFieldNullOrEmptySafeKernel(
        DiffNullableDeepBag bagByVal,
        ref DiffNullableDeepBag bagByRef,
        [Out] int[] output)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid == 0)
        {
            output[0] = bagByVal.Data is null ? 1 : 0;
            output[1] = bagByVal.Data is not null ? bagByVal.Data.Length : -1;
            output[2] = bagByRef.Data is null ? 1 : 0;
            output[3] = bagByRef.Data is not null ? bagByRef.Data.Length : -1;
            if (bagByRef.Data is not null && bagByRef.Data.Length > 0)
            {
                bagByRef.Data[0] += bagByRef.Fallback;
                output[4] = bagByRef.Data[0];
            }
            else
            {
                output[4] = bagByVal.Fallback + bagByRef.Fallback;
            }
        }
    }

    [GpuKernel]
    public static void StructFieldNullDereferenceKernel(
        DiffNullableDeepBag bag,
        [Out] int[] output)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid == 0)
        {
#pragma warning disable CS8602
            output[0] = bag.Data.Length + bag.Fallback;
#pragma warning restore CS8602
        }
    }
}
