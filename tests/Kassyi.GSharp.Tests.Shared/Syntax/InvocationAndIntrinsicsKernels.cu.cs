// ReSharper disable All

using System;
using System.Threading;
using Kassyi.GSharp.Core;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Tests.Syntax;

[StructLayout(LayoutKind.Sequential)]
public struct AtomicTestStruct
{
    public int ValueI;
    public uint ValueU;
    public long ValueL;
}

public static partial class InvocationAndIntrinsicsKernels
{
    // SYN-49: Interlocked アトミック演算マッピング
    [GpuKernel]
    public static void InterlockedKernel(int[] intBuf, uint[] uintBuf, long[] longBuf, ulong[] ulongBuf)
    {
        int tid = ThreadIndex.X;

        // Interlocked.Add
        Interlocked.Add(ref intBuf[0], 1);
        Interlocked.Add(ref longBuf[0], 2L);

        // Interlocked.Exchange
        if (tid == 0)
        {
            Interlocked.Exchange(ref intBuf[1], 100);
            Interlocked.Exchange(ref uintBuf[1], 200u);
            Interlocked.Exchange(ref longBuf[1], 300L);
            Interlocked.Exchange(ref ulongBuf[1], 400ul);
        }

        // Interlocked.CompareExchange
        if (tid == 0)
        {
            Interlocked.CompareExchange(ref intBuf[2], 500, 0);
            Interlocked.CompareExchange(ref uintBuf[2], 600u, 0u);
            Interlocked.CompareExchange(ref longBuf[2], 700L, 0L);
            Interlocked.CompareExchange(ref ulongBuf[2], 800ul, 0ul);
        }
    }

    // SYN-49 Edge Cases: ref 指定の第1引数に対して配列要素や構造体フィールドを渡す
    [GpuKernel]
    public static void InterlockedEdgeCasesKernel(int[] array, AtomicTestStruct[] structArray)
    {
        int tid = ThreadIndex.X;

        // 配列要素: ref array[tid]
        Interlocked.Add(ref array[tid], 10);

        // 構造体フィールド: ref structArray[tid].ValueI, ValueU, ValueL
        Interlocked.Add(ref structArray[tid].ValueI, 1);
        Interlocked.Exchange(ref structArray[tid].ValueU, 42u);
        Interlocked.CompareExchange(ref structArray[tid].ValueL, 99L, 0L);
    }

    // SYN-68: Math / MathF 関数群のマッピング (float vs double 呼び分け含む)
    [GpuKernel]
    public static void MathKernel(
        double[] doubleInputs, double[] doubleOutputs,
        float[] floatInputs, float[] floatOutputs)
    {
        int tid = ThreadIndex.X;

        // Double (System.Math) -> sin, cos, clamp, max, min, abs, exp, sqrt, log
        double dVal = doubleInputs[tid];
        doubleOutputs[0] = Math.Sin(dVal);
        doubleOutputs[1] = Math.Cos(dVal);
        doubleOutputs[2] = Math.Clamp(dVal, 0.0, 10.0);
        doubleOutputs[3] = Math.Max(dVal, 5.0);
        doubleOutputs[4] = Math.Min(dVal, 5.0);
        doubleOutputs[5] = Math.Abs(dVal);
        doubleOutputs[6] = Math.Exp(dVal);
        doubleOutputs[7] = Math.Sqrt(Math.Abs(dVal)); // Ensure no domain error for general test
        doubleOutputs[8] = Math.Log(Math.Abs(dVal) + 1.0); // Ensure no domain error

        // Float (System.MathF) -> sinf, cosf, maxf, minf, absf, expf, sqrtf, logf; Math.Clamp for float
        float fVal = floatInputs[tid];
        floatOutputs[0] = MathF.Sin(fVal);
        floatOutputs[1] = MathF.Cos(fVal);
        floatOutputs[2] = Math.Clamp(fVal, 0.0f, 10.0f);
        floatOutputs[3] = MathF.Max(fVal, 5.0f);
        floatOutputs[4] = MathF.Min(fVal, 5.0f);
        floatOutputs[5] = MathF.Abs(fVal);
        floatOutputs[6] = MathF.Exp(fVal);
        floatOutputs[7] = MathF.Sqrt(MathF.Abs(fVal));
        floatOutputs[8] = MathF.Log(MathF.Abs(fVal) + 1.0f);
    }

    // SYN-68 Edge Cases: NaN / Infinity / 境界値
    [GpuKernel]
    public static void MathEdgeCasesKernel(
        float[] floatSpecialInputs, float[] floatSpecialResults,
        double[] doubleSpecialInputs, double[] doubleSpecialResults)
    {
        int tid = ThreadIndex.X;

        // Float NaN / Infinity / Extremes
        float f = floatSpecialInputs[tid];
        floatSpecialResults[0] = MathF.Abs(f);
        floatSpecialResults[1] = MathF.Max(f, 0.0f);
        floatSpecialResults[2] = MathF.Min(f, 0.0f);
        floatSpecialResults[3] = MathF.Exp(f);
        floatSpecialResults[4] = MathF.Sqrt(f);
        floatSpecialResults[5] = MathF.Log(f);

        // Double NaN / Infinity / Extremes
        double d = doubleSpecialInputs[tid];
        doubleSpecialResults[0] = Math.Abs(d);
        doubleSpecialResults[1] = Math.Max(d, 0.0);
        doubleSpecialResults[2] = Math.Min(d, 0.0);
        doubleSpecialResults[3] = Math.Exp(d);
        doubleSpecialResults[4] = Math.Sqrt(d);
        doubleSpecialResults[5] = Math.Log(d);
    }
}
