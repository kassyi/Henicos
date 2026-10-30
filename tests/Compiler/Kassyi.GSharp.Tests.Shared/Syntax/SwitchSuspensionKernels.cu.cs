// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Kernels with a barrier or a warp shuffle inside a switch section (#708). On the CPU paths each of them must split the
/// state machine as it does outside a switch, so every thread sees what the others wrote or published before it.
/// </summary>
public static partial class SwitchSuspensionKernels
{
    private const uint FullMask = 0xFFFFFFFFu;
    private const int LastCase = 2;

    // The form of #708: the read after the switch sees the writes of every thread made before the barrier of its section
    [GpuKernel]
    public static void BarrierInSectionKernel(int[] data, int[] output, int k, int count)
    {
        int tid = ThreadIndex.X;
        switch (k)
        {
            case 0:
                data[tid] = tid + 1;
                Gpu.SyncThreads();
                break;
            default:
                data[tid] = 100 + tid;
                Gpu.SyncThreads();
                break;
        }

        output[tid] = data[(tid + 1) % count];
    }

    // A shuffle in several sections, one of them with two case labels, and two shuffles in one statement
    [GpuKernel]
    public static void ShuffleInSeveralSectionsKernel(int[] output, int flag)
    {
        int tid = ThreadIndex.X;
        int value = tid * 10;
        switch (flag)
        {
            case 0:
                value = Warp.ShuffleXorSync(FullMask, value, 1);
                break;
            case 1:
            case 3:
                int shifted = Warp.ShuffleDownSync(FullMask, value, (uint)flag);
                value = shifted + flag;
                break;
            default:
                value = Warp.ShuffleXorSync(FullMask, value, 2) + Warp.ShuffleXorSync(FullMask, value, 4);
                break;
        }

        output[tid] = value;
    }

    // A shuffle in a switch nested in a section; the outer section goes on after the inner break. The other section
    // holds a nested switch without a split point, which keeps its own break.
    [GpuKernel]
    public static void NestedSwitchKernel(int[] output, int outer, int inner)
    {
        int tid = ThreadIndex.X;
        int value = tid;
        switch (outer)
        {
            case 0:
                switch (inner)
                {
                    case 0:
                        value = Warp.ShuffleXorSync(FullMask, value, 1);
                        break;
                    default:
                        value += 100;
                        break;
                }

                value += 1000;
                break;
            default:
                switch (inner)
                {
                    case 0:
                        value += 5;
                        break;
                    default:
                        value += 7;
                        break;
                }

                value = Warp.ShuffleXorSync(FullMask, value, 3);
                break;
        }

        output[tid] = value;
    }

    // goto case and goto default between sections that shuffle; goto case 2 reaches the section labeled by the constant
    // LastCase, so the jump is matched by value, not by its spelling
    [GpuKernel]
    public static void GotoCaseKernel(int[] output, int flag)
    {
        int tid = ThreadIndex.X;
        int value = tid;
        switch (flag)
        {
            case 0:
                value = Warp.ShuffleXorSync(FullMask, value, 1);
                goto case 2;
            case 1:
                value += 50;
                goto default;
            case LastCase:
                int doubled = Warp.ShuffleXorSync(FullMask, value, 2) * 2;
                value = doubled;
                break;
            default:
                value = Warp.ShuffleXorSync(FullMask, value, 4) + 1;
                break;
        }

        output[tid] = value;
    }

    // A switch with a shuffle in a loop: break leaves the switch, continue goes to the next iteration
    [GpuKernel]
    public static void SwitchInLoopKernel(int[] output, int rounds)
    {
        int tid = ThreadIndex.X;
        int value = tid;
        for (int i = 0; i < rounds; i++)
        {
            switch (i % 3)
            {
                case 0:
                    value = Warp.ShuffleXorSync(FullMask, value, 1);
                    break;
                case 1:
                    value += 1;
                    continue;
                default:
                    value = Warp.ShuffleXorSync(FullMask, value, 2) * 2;
                    break;
            }

            value += 10;
        }

        output[tid] = value;
    }

    // The governing expression is evaluated once, also when the sections are compared one by one
    [GpuKernel]
    public static void GoverningEvaluatedOnceKernel(int[] output, int flag)
    {
        int tid = ThreadIndex.X;
        int k = flag;
        int value = tid;
        switch (k++)
        {
            case 0:
                value = Warp.ShuffleXorSync(FullMask, value, 1);
                break;
            case 1:
                value = Warp.ShuffleXorSync(FullMask, value, 2);
                break;
        }

        output[tid] = value * 10 + k;
    }

    // A const of a section that another section reads, declared before the barrier of its own section: the default
    // section reads 255, not the value of a context field that only the first section assigns. The byte type is kept, so
    // the first section wraps to 0
    [GpuKernel]
    public static void SectionConstantAcrossBarrierKernel(int[] output, int flag)
    {
        int tid = ThreadIndex.X;
        switch (flag)
        {
            case 0:
                const byte Max = 255;
                Gpu.SyncThreads();
                var x = Max;
                x++;
                output[tid] = x;
                break;
            default:
                var y = Max;
                y++;
                output[tid] = y + Max;
                break;
        }
    }

    // A const of a section used as the case label of another section, in a switch flattened for its shuffles: the
    // dispatch compares with the const's value before any section runs
    [GpuKernel]
    public static void SectionConstantCaseLabelKernel(int[] output, int flag)
    {
        int tid = ThreadIndex.X;
        int value = tid;
        switch (flag)
        {
            case 0:
                const int Next = 1;
                value = Warp.ShuffleXorSync(FullMask, value, 1);
                break;
            case Next:
                value = Warp.ShuffleXorSync(FullMask, value, 2) + 100;
                break;
            default:
                value += 1000;
                break;
        }

        output[tid] = value;
    }

    // A const of a section that reads a const of an earlier section, each before a barrier: the consts move ahead of the
    // dispatch in source order, so B reads the value of A that is already assigned
    [GpuKernel]
    public static void SectionConstantChainKernel(int[] output, int flag)
    {
        int tid = ThreadIndex.X;
        switch (flag)
        {
            case 0:
                const int A = 3;
                Gpu.SyncThreads();
                output[tid] = A;
                break;
            case 1:
                const int B = A * 2;
                Gpu.SyncThreads();
                output[tid] = B;
                break;
            default:
                output[tid] = A + B;
                break;
        }
    }
}
