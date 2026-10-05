// ReSharper disable All

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
[Kassyi.GSharp.Core.SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static partial class GpuBigIntGcdKernels
{
    public static partial class AotContexts
    {
        public unsafe struct ExtendedBinaryGcdContext
        {
            public uint* M;
            public uint* U;
            public uint* V;
            public uint* X1;
            public uint* X2;
            public int* Status;
            public int NumWords;
            public int MaxIterations;
        }

        public unsafe struct LehmerGcdContext
        {
            public uint* A;
            public uint* B;
            public uint* NewA;
            public uint* NewB;
            public int* Status;
            public int NumWords;
            public int MaxIterations;
        }

        public unsafe struct Matrix2x2ApplyContext
        {
            [GpuReadOnly]
            public uint* A;

            [GpuReadOnly]
            public uint* B;

            public uint* DstA;
            public uint* DstB;
            public long U0;
            public long V0;
            public long U1;
            public long V1;
            public int NumWords;
        }
    }

    [GpuKernel]
    public static void ApplyMatrix2x2Kernel(AotContexts.Matrix2x2ApplyContext ctx)
    {
        Gpu.SingleThread(() =>
        {
            unsafe
            {
                long carryA = 0;
                long carryB = 0;
                for (int i = 0; i < ctx.NumWords; i++)
                {
                    long valA = ctx.A[i];
                    long valB = ctx.B[i];

                    var nextA = valA * ctx.U0 + valB * ctx.V0 + carryA;
                    var nextB = valA * ctx.U1 + valB * ctx.V1 + carryB;

                    ctx.DstA[i] = (uint)(nextA & 0xFFFFFFFF);
                    carryA = nextA >> 32;

                    ctx.DstB[i] = (uint)(nextB & 0xFFFFFFFF);
                    carryB = nextB >> 32;
                }
            }
        });
    }

    [GpuDeviceFunction]
    public static bool ComputeLehmerMatrix(ulong x, ulong y, out long u0, out long v0, out long u1, out long v1)
    {
        u0 = 1;
        v0 = 0;
        u1 = 0;
        v1 = 1;
        var progress = false;

        while (y != 0)
        {
            if (y + 1 == 0 || x == ~0ul)
            {
                break; // Overflow protection
            }

            var qMin = x / (y + 1);
            var qMax = (x + 1) / y;

            if (qMin != qMax)
            {
                break;
            }

            var q = (long)qMin;
            if (q == 0)
            {
                break;
            }

            var r = x - (ulong)q * y;

            var nextU = u0 - q * u1;
            var nextV = v0 - q * v1;

            if (nextU > 0x7FFFFFFF || nextU < -0x7FFFFFFF ||
                nextV > 0x7FFFFFFF || nextV < -0x7FFFFFFF)
            {
                break;
            }

            x = y;
            y = r;
            u0 = u1;
            v0 = v1;
            u1 = nextU;
            v1 = nextV;
            progress = true;
        }

        return progress;
    }

    [GpuKernel]
    public static void ExecuteLehmerGcdMacroKernel(AotContexts.LehmerGcdContext ctx)
    {
        Gpu.SingleThread(() =>
        {
            for (int iter = 0; iter < ctx.MaxIterations; iter++)
            {
                unsafe
                {
                    int bitsA = GpuBigIntPrimitiveOps.DeviceGetBitLength(ctx.A, ctx.NumWords);
                    int bitsB = GpuBigIntPrimitiveOps.DeviceGetBitLength(ctx.B, ctx.NumWords);

                    if (bitsB == 0)
                    {
                        ctx.Status[0] = 2; // 2: Finished
                        break;
                    }

                    if (bitsA - bitsB > 64 || bitsA <= 64)
                    {
                        ctx.Status[0] = 1; // 1: NeedMod
                        break;
                    }

                    int shift = bitsA - 64;
                    if (shift < 0)
                    {
                        shift = 0;
                    }

                    ulong x = GpuBigIntPrimitiveOps.DeviceExtract64(ctx.A, shift, ctx.NumWords);
                    ulong y = GpuBigIntPrimitiveOps.DeviceExtract64(ctx.B, shift, ctx.NumWords);

                    long u0, v0, u1, v1;
                    bool progress = ComputeLehmerMatrix(x, y, out u0, out v0, out u1, out v1);

                    if (!progress)
                    {
                        ctx.Status[0] = 1; // 1: NeedMod
                        break;
                    }

                    long carryA = 0;
                    long carryB = 0;
                    for (int i = 0; i < ctx.NumWords; i++)
                    {
                        long valA = ctx.A[i];
                        long valB = ctx.B[i];

                        long nextA = valA * u0 + valB * v0 + carryA;
                        long nextB = valA * u1 + valB * v1 + carryB;

                        ctx.NewA[i] = (uint)(nextA & 0xFFFFFFFF);
                        carryA = nextA >> 32;

                        ctx.NewB[i] = (uint)(nextB & 0xFFFFFFFF);
                        carryB = nextB >> 32;
                    }

                    if (GpuBigIntPrimitiveOps.DeviceIsNegative(ctx.NewA, ctx.NumWords) ||
                        GpuBigIntPrimitiveOps.DeviceIsNegative(ctx.NewB, ctx.NumWords) ||
                        GpuBigIntPrimitiveOps.DeviceCompareTo(ctx.NewB, ctx.NewA, ctx.NumWords) > 0)
                    {
                        ctx.Status[0] = 1; // 1: NeedMod
                        break;
                    }

                    for (int i = 0; i < ctx.NumWords; i++)
                    {
                        ctx.A[i] = ctx.NewA[i];
                        ctx.B[i] = ctx.NewB[i];
                    }
                }
            }
        });
    }

    [GpuDeviceFunction]
    private static unsafe void DeviceUpdateXShift(uint* x, uint* m, int numWords)
    {
        if ((x[0] & 1) == 0)
        {
            GpuBigIntPrimitiveOps.DeviceShiftRight1(x, numWords);
        }
        else
        {
            uint carry = GpuBigIntPrimitiveOps.DeviceAdd(x, m, x, numWords);
            GpuBigIntPrimitiveOps.DeviceShiftRight1(x, numWords);
            if (carry != 0)
            {
                x[numWords - 1] |= 0x80000000u;
            }
        }
    }

    [GpuDeviceFunction]
    private static unsafe void DeviceUpdateXSub(uint* x1, uint* x2, uint* m, int numWords)
    {
        if (GpuBigIntPrimitiveOps.DeviceCompareTo(x1, x2, numWords) >= 0)
        {
            GpuBigIntPrimitiveOps.DeviceSub(x1, x2, x1, numWords);
        }
        else
        {
            GpuBigIntPrimitiveOps.DeviceAdd(x1, m, x1, numWords);
            GpuBigIntPrimitiveOps.DeviceSub(x1, x2, x1, numWords);
        }
    }

    [GpuKernel]
    public static void ExecuteExtendedBinaryGcdMacroKernel(AotContexts.ExtendedBinaryGcdContext ctx)
    {
        Gpu.SingleThread(() =>
        {
            for (int iter = 0; iter < ctx.MaxIterations; iter++)
            {
                unsafe
                {
                    if (GpuBigIntPrimitiveOps.DeviceIsZero(ctx.U, ctx.NumWords) || GpuBigIntPrimitiveOps.DeviceIsZero(ctx.V, ctx.NumWords))
                    {
                        bool uIsOne = GpuBigIntPrimitiveOps.DeviceIsOne(ctx.U, ctx.NumWords);
                        bool vIsOne = GpuBigIntPrimitiveOps.DeviceIsOne(ctx.V, ctx.NumWords);
                        if (uIsOne || vIsOne)
                        {
                            if (vIsOne)
                            {
                                for (int i = 0; i < ctx.NumWords; i++) ctx.X1[i] = ctx.X2[i];
                            }

                            ctx.Status[0] = 1; // Success
                        }
                        else
                        {
                            ctx.Status[0] = 2; // Failed (not coprime)
                        }

                        return;
                    }

                    if ((ctx.U[0] & 1) == 0) // u is even
                    {
                        GpuBigIntPrimitiveOps.DeviceShiftRight1(ctx.U, ctx.NumWords);
                        DeviceUpdateXShift(ctx.X1, ctx.M, ctx.NumWords);
                        continue;
                    }

                    if ((ctx.V[0] & 1) == 0) // v is even
                    {
                        GpuBigIntPrimitiveOps.DeviceShiftRight1(ctx.V, ctx.NumWords);
                        DeviceUpdateXShift(ctx.X2, ctx.M, ctx.NumWords);
                        continue;
                    }

                    // Both odd
                    if (GpuBigIntPrimitiveOps.DeviceCompareTo(ctx.U, ctx.V, ctx.NumWords) >= 0)
                    {
                        GpuBigIntPrimitiveOps.DeviceSub(ctx.U, ctx.V, ctx.U, ctx.NumWords); // u = u - v
                        DeviceUpdateXSub(ctx.X1, ctx.X2, ctx.M, ctx.NumWords);
                    }
                    else
                    {
                        GpuBigIntPrimitiveOps.DeviceSub(ctx.V, ctx.U, ctx.V, ctx.NumWords); // v = v - u
                        DeviceUpdateXSub(ctx.X2, ctx.X1, ctx.M, ctx.NumWords);
                    }
                }
            }
        });
    }
}

