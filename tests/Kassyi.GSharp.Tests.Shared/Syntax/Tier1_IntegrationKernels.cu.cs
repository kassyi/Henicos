// ReSharper disable All

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Stdlib.GpuMath;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

[InlineArray(4)]
public struct Tier1FixedArray
{
    private float _element0;
}

public struct Tier1CustomStruct
{
    public int IntVal;
    public float FloatVal;
    public bool BoolVal;
}

public static partial class Tier1IntegrationKernels
{
    [GpuDeviceFunction]
    public static float HelperMath(float x, float y)
    {
        return (float)Math.Max(x * x, y * y);
    }

    [GpuDeviceFunction]
    public static bool TryProcess(int input, out int output)
    {
        output = input * 2;
        return input > 0;
    }

    public unsafe struct Tier1FixedBufferStruct
    {
        public fixed int Data[4];
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float HelperMathAggressive(float x)
    {
        return (float)Math.Abs(x);
    }

    [GpuKernel]
    public static void AllInOneKernel(
        int[] intInput,
        float[] floatInput,
        float[] results,
        Tier1CustomStruct config,
        ulong[] globalCounter,
        int count,
        int dynamicSharedSize)
    {
        // SYN-94: Macro DSL
        Gpu.GridStrideLoop(count, tid =>
        {
            // Local function with AggressiveInlining
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            int LocalInlineFunc(int x) => x + 5 - 5; // effectively x

            // Tuple, tuple swap, & Inline out var (SYN-03)
            (int a, int b) = (LocalInlineFunc(tid), tid * 2);
            (a, b) = (b, a); // Swap
            (a, b) = (b, a); // Swap back

            bool ok = TryProcess(intInput[tid], out var processed); // var support

            // Bitwise and Shifts
            int bitwise = (a << 1) | (b >> 1) ^ 0xFF;
            bitwise &= ~1;

            // Math & Hardware Math (SYN-68)
            float mag = HelperMath(floatInput[tid], (float)processed);
            mag = HelperMathAggressive(mag); // SYN-28
            mag = (float)Math.Max(mag, config.FloatVal);

            // SYN-01: Switch expression
            mag += (tid % 3) switch
            {
                0 => 1.0f,
                1 => 2.0f,
                _ => 3.0f
            };

            // Inline Array (GEN-30)
            Tier1FixedArray arr = new Tier1FixedArray();
            arr[0] = mag;
            arr[1] = mag * 2.0f;
            arr[2] = mag * 3.0f;
            arr[3] = mag * 4.0f;

            // SYN-94: ForUnroll
            float sum = 0;
            Gpu.ForUnroll(0, 4, i =>
            {
                if (i != 1)
                {
                    sum += arr[i];
                }
            });

            // SYN-70: foreach loop on InlineArray
            float foreachSum = 0;
            foreach (var val in arr)
            {
                foreachSum += val;
            }

            sum += foreachSum;

            // SYN-12: C# 12 Collection Expressions on ReadOnlySpan & Length property
            {
                ReadOnlySpan<float> coll = [1.0f, 2.0f, 3.0f, 4.0f];
                float collSum = 0;
                foreach (var val in coll)
                {
                    collSum += val;
                }

                sum += collSum + coll.Length;
            }

            // SYN-13: StackAlloc on Span & Length property
            {
                Span<float> stackBuf = stackalloc float[] { 5.0f, 10.0f, 15.0f, 20.0f };
                float stackSum = 0;
                foreach (var val in stackBuf)
                {
                    stackSum += val;
                }

                sum += stackSum + stackBuf.Length;
            }

            // SYN-08: Lambda expressions (Expanded to C++ auto lambdas)
            var multiplier = (float x, float m) => x * m;
            sum = multiplier(sum, 2.0f);

            // Atomic Operations (SYN-49)
            if (ok && config.BoolVal)
            {
                Gpu.AtomicAdd(ref globalCounter[0], (ulong)bitwise);
            }

            // SYN-47: Dynamic Shared Memory
            SharedMemory<float> dynamicSharedMem = new SharedMemory<float>(dynamicSharedSize);
            dynamicSharedMem[ThreadIndex.X] = sum;
            Gpu.SyncThreads(); // GBI-31

            // Complex ternary (GSH-01)
            float finalVal = (bitwise > 0)
                ? (dynamicSharedMem[ThreadIndex.X] > 10.0f ? 10.0f : dynamicSharedMem[ThreadIndex.X])
                : 0.0f;

            // SYN-14: fixed buffers inside structs
            unsafe
            {
                Tier1FixedBufferStruct fbs = new Tier1FixedBufferStruct();
                fbs.Data[0] = bitwise;
                finalVal += fbs.Data[0];
            }

            // SYN-04 Null coalescing
            int? maybeVal = default;
            int actualVal = maybeVal ?? 5; // Should be 5
            finalVal += actualVal;

            // BCL-01 & BCL-02: System.Numerics.BitOperations
            int popCount = System.Numerics.BitOperations.PopCount((uint)bitwise);
            int leadingZeroes = System.Numerics.BitOperations.LeadingZeroCount((uint)bitwise);
            finalVal += popCount + leadingZeroes;

            // SYN-90: Complex Control Flow
            int loopCounter = 0;
            do
            {
                if (loopCounter % 2 == 0)
                {
                    if (finalVal < 1000.0f)
                    {
                        finalVal += 1.0f;
                    }
                }
                else
                {
                    finalVal -= 0.5f;
                }

                loopCounter++;
            } while (loopCounter < 3);

            // SYN-77: Inline PTX
            Gpu.InlinePtx("// test inline ptx");

            results[tid] = finalVal;
        });
    }
}
