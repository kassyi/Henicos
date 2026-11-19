// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>Enum used to verify <c>const enum</c> locals read across a barrier.</summary>
public enum LocalConstMode : byte
{
    Alpha = 1,
    Beta = 2,
    Wide = 200
}

/// <summary>
/// Kernels that declare local <c>const</c> values in a body that a barrier or a warp shuffle splits and read them
/// afterwards, where C# still treats them as constants (constant conversions, overload resolution, other const
/// initializers, default parameter values).
/// </summary>
public static partial class LocalConstAcrossBarrierKernels
{
    [GpuKernel]
    public static void ConstAcrossBarrierKernel(uint[] result)
    {
        const int D = 2;
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        uint x = D;
        result[tid] = x;
    }

    [GpuKernel]
    public static void LocalConstInLoopKernel(int[] result)
    {
        int v = ThreadIndex.X;
        for (int k = 0; k < 2; k++)
        {
            const int D = 2;
            v = Warp.ShuffleDownSync(0xFFFFFFFFu, v, D / 2);
        }

        result[ThreadIndex.X] = v;
    }

    // A constant int converts to uint, so `x - A` is a uint subtraction (4294967295); as a variable, A would make it
    // a long subtraction (-1)
    [GpuKernel]
    public static void ConstConversionChangesOperatorKernel(long[] result)
    {
        const int A = 1;
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        uint x = 0u;
        result[tid] = x - A;
    }

    [GpuKernel]
    public static void ShortConstWrapKernel(int[] result)
    {
        const short Max = short.MaxValue;
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        var wrapped = Max;
        wrapped++;
        result[tid] = wrapped;
    }

    [GpuKernel]
    public static void ShortConstSumKernel(int[] result)
    {
        const short Pos = 7;
        const short Neg = -3;
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        result[tid] = Neg + Pos;
    }

    // A constant int converts to ushort only as a constant
    [GpuKernel]
    public static void UshortConstConversionKernel(uint[] result)
    {
        const int PosInt = 7;
        const ulong Small = 42UL;
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        ushort u = PosInt;
        result[tid] = (uint)Small + u;
    }

    [GpuKernel]
    public static void CharConstKernel(int[] result)
    {
        const char Ch = 'Z';
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        int charValue = Ch;
        result[tid] = charValue;
    }

    [GpuKernel]
    public static void BoolConstKernel(int[] result)
    {
        const bool Flag = true;
        const bool Off = false;
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        result[tid] = (Flag && !Off) ? 1 : 0;
    }

    // The byte constant and the int pick Math.Max(int, int)
    [GpuKernel]
    public static void ByteConstMaxKernel(int[] result)
    {
        const byte ByteMax = 255;
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        result[tid] = Math.Max(ByteMax, tid);
    }

    [GpuKernel]
    public static void NanConstKernel(int[] result)
    {
        const float NaN = float.NaN;
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        result[tid] = (NaN != NaN) ? 1 : 0;
    }

    [GpuKernel]
    public static void UlongConstMaskKernel(ulong[] result)
    {
        const ulong BigMask = 1UL << 40;
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        result[tid] = BigMask | (ulong)(uint)tid;
    }

    // A static local function reads the const, which needs no capture
    [GpuKernel]
    public static void StaticLocalFunctionConstKernel(int[] result)
    {
        const int D = 6;
        int tid = ThreadIndex.X;

        static int F() => D;

        Gpu.SyncThreads();
        result[tid] = F();
    }

    [GpuKernel]
    public static void ZeroFloatConstDivisorsAcrossBarrierKernel(double[] doubles, float[] floats)
    {
        const float NegZero = -0f;
        const double PosZero = 0.0;
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        doubles[tid * 3 + 0] = 1.0 / NegZero;
        doubles[tid * 3 + 1] = 1.0 / PosZero;
        doubles[tid * 3 + 2] = 0.0 / PosZero;
        floats[tid * 2 + 0] = 1f / NegZero;
        floats[tid * 2 + 1] = NegZero;
    }

    [GpuKernel]
    public static void SpecialFloatConstsAcrossBarrierKernel(double[] result)
    {
        const float Nan = float.NaN;
        const double Inf = double.PositiveInfinity;
        const float Tiny = 1e-45f;
        const double NegZero = -0.0;
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        result[tid * 4 + 0] = Nan;
        result[tid * 4 + 1] = Inf;
        result[tid * 4 + 2] = Tiny;
        result[tid * 4 + 3] = NegZero;
    }

    // Two consts of one name and different types in sibling scopes, and a variable of that name
    [GpuKernel]
    public static void SiblingScopeConstsKernel(double[] result)
    {
        int tid = ThreadIndex.X;
        {
            const int K = 3;
            Gpu.SyncThreads();
            result[tid] = K;
        }
        {
            const double K = 0.5;
            Gpu.SyncThreads();
            result[tid] += K;
        }
        {
            int K = tid;
            result[tid] += K;
        }
    }

    [GpuKernel]
    public static void SwitchScopedBlockConstsAcrossSectionsKernel(int[] result, int flag)
    {
        int tid = ThreadIndex.X;
        switch (flag)
        {
            case 0:
                {
                    const int D = 10;
                    Gpu.SyncThreads();
                    result[tid] = D + tid;
                    break;
                }
            case 1:
                {
                    const int D = 20;
                    Gpu.SyncThreads();
                    result[tid] = D + tid;
                    break;
                }
        }
    }

    [GpuKernel]
    public static void SwitchSameSectionTwoBlocksConstsKernel(int[] result, int flag)
    {
        int tid = ThreadIndex.X;
        switch (flag)
        {
            case 0:
                {
                    const int D = 10;
                    Gpu.SyncThreads();
                    result[tid] = D + tid;
                }
                {
                    const int D = 20;
                    Gpu.SyncThreads();
                    result[tid] += D * 10;
                }
                break;
            default:
                result[tid] = -1;
                break;
        }
    }

    [GpuKernel]
    public static void ConstInitializerAfterBarrierKernel(uint[] result)
    {
        const int A = 2;
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        const int B = A * 2;
        uint x = B;
        result[tid] = x;
    }

    // The local functions of a split body are written in front of its state switch, after the consts
    [GpuKernel]
    public static void MovedLocalFunctionConstKernel(uint[] result)
    {
        const int D = 2;
        int tid = ThreadIndex.X;

        uint F()
        {
            uint x = D;
            return x;
        }

        int G(int v = D) => v;

        Gpu.SyncThreads();
        result[tid] = F() + (uint)G();
    }

    // Two consts of one name in sibling scopes: nameof keeps the source name when the managed output renames them
    [GpuKernel]
    public static void NameofConstAcrossBarrierKernel(int[] result)
    {
        int tid = ThreadIndex.X;
        {
            const int Dd = 2;
            Gpu.SyncThreads();
            result[tid] = nameof(Dd).Length * 10 + Dd;
        }
        {
            const int Dd = 3;
            Gpu.SyncThreads();
            result[tid] += Dd;
        }
    }

    [GpuKernel]
    public static void EnumConstAcrossBarrierKernel(int[] result)
    {
        const LocalConstMode V = LocalConstMode.Wide;
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        LocalConstMode e = V;
        result[tid] = (int)V + (int)e;
    }

    [GpuKernel]
    public static void MultiDeclaratorConstKernel(uint[] result)
    {
        const int A = 1, B = A + 1;
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        uint x = B;
        result[tid] = x + A;
    }

    [GpuKernel]
    public static void LabeledConstKernel(uint[] result)
    {
        int tid = ThreadIndex.X;
        if (tid < 0)
        {
            goto Start;
        }

    Start:
        const int D = 2;
        Gpu.SyncThreads();
        uint x = D;
        result[tid] = x;
    }

    // A const of a local function or a lambda stays in the function
    [GpuKernel]
    public static void ConstInsideLocalFunctionKernel(int[] result)
    {
        int tid = ThreadIndex.X;

        int F(int v)
        {
            const int K = 7;
            return v + K;
        }

        Func<int, int> g = v =>
        {
            const int L = 1;
            return v + L;
        };

        Gpu.SyncThreads();
        result[tid] = F(tid) + g(0);
    }

    [GpuDeviceFunction]
    public static uint ReadConstAfterBarrierDevice(int tid)
    {
        const int Offset = 5;
        Gpu.SyncThreads();
        uint x = Offset;
        return x + (uint)tid;
    }

    [GpuKernel]
    public static void DeviceFunctionLocalConstKernel(uint[] result)
    {
        int tid = ThreadIndex.X;
        result[tid] = ReadConstAfterBarrierDevice(tid);
    }
}
