// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Local const values where C++ needs a constant expression or a value: a case label, the count of shared memory, the
/// argument of an <c>in</c> parameter, and a label before a const at the end of a block.
/// </summary>
public static partial class LocalConstCaseLabelKernels
{
    [GpuKernel]
    public static void CaseLabelKernel(int[] result)
    {
        const int D = 2;
        int tid = ThreadIndex.X;
        switch (tid)
        {
            case D:
                result[tid] = 1;
                break;
            default:
                result[tid] = 0;
                break;
        }
    }

    [GpuKernel]
    public static void ByteCaseLabelKernel(int[] result)
    {
        const byte B = 200;
        int tid = ThreadIndex.X;
        byte x = (byte)(tid + 198);
        switch (x)
        {
            case B:
                result[tid] = 1;
                break;
            default:
                result[tid] = 0;
                break;
        }
    }

    [GpuKernel]
    public static void GotoCaseLocalConstKernel(int[] result)
    {
        const int K = 1;
        int tid = ThreadIndex.X;
        switch (tid & 1)
        {
            case 0:
                goto case K;
            case K:
                result[tid] += 2;
                break;
        }
    }

    // The const of one section is read by another section
    [GpuKernel]
    public static void SharedSectionConstKernel(int[] result, int flag)
    {
        int tid = ThreadIndex.X;
        switch (flag)
        {
            case 0:
                const int D = 10;
                result[tid] = D;
                break;
            case 1:
                result[tid] = D + 1;
                break;
        }
    }

    [GpuDeviceFunction]
    private static int AddIn(in int a, int b) => a + b;

    [GpuKernel]
    public static void InArgumentLocalConstKernel(int[] result)
    {
        const int K = 7;
        int tid = ThreadIndex.X;
        result[tid] = AddIn(K, tid);
    }

    // The declaration writes nothing, so the label needs a statement of its own at the end of the block
    [GpuKernel]
    public static void LabeledConstAtBlockEndKernel(int[] result)
    {
        int tid = ThreadIndex.X;
        result[tid] = 0;
        if (tid == 0)
        {
            goto L;
        }

        result[tid] = 1;
    L:
        const int K = 2;
    }

    // CUDA declares static shared memory as an array, whose size has to be a constant expression
    [GpuKernel]
    public static void LocalConstSharedMemoryCountKernel(int[] result)
    {
        const int N = 4;
        int tid = ThreadIndex.X;
        SharedMemory<int> s = new SharedMemory<int>(N);
        s[tid] = tid;
        result[tid] = s[tid];
    }
}
