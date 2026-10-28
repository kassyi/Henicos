using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Kernels with a jump out of a switch of the user inside a loop with a barrier (#670). The CPU dialects flatten such a
/// loop into the states of a state machine, which is itself a switch, so a jump to a state must not bind to the user's
/// switch. The barrier comes first in each iteration, so every thread reaches it the same number of times.
/// </summary>
public static partial class SwitchInBarrierLoopKernels
{
    // K1: a continue inside a switch that has a case with the number of the state the continue reaches
    [GpuKernel]
    public static void ContinueInSwitchKernel(int[] a, int[] mode)
    {
        int tid = ThreadIndex.X;
        for (int i = 0; i < 3; i++)
        {
            Gpu.SyncThreads();
            switch (mode[tid])
            {
                case 0:
                    continue;
                case 1:
                    a[tid] += 10;
                    break;
                case 2:
                    a[tid] += 20;
                    break;
                case 3:
                    a[tid] += 100;
                    break;
                case 4:
                    a[tid] += 1000;
                    break;
                default:
                    break;
            }
            a[tid] += 1;
        }
    }

    // K2: a continue inside a switch without a case of that number
    [GpuKernel]
    public static void ContinueInSwitchWithoutCaseKernel(int[] a, int[] mode)
    {
        int tid = ThreadIndex.X;
        for (int i = 0; i < 3; i++)
        {
            Gpu.SyncThreads();
            switch (mode[tid])
            {
                case 0:
                    continue;
                default:
                    break;
            }
            a[tid] += 1;
        }
    }

    // K3: a goto of the user from a switch to a label after the loop, in another state than the goto, in the last iteration
    [GpuKernel]
    public static void GotoOutOfSwitchKernel(int[] a, int[] mode)
    {
        int tid = ThreadIndex.X;
        for (int i = 0; i < 3; i++)
        {
            Gpu.SyncThreads();
            switch (mode[tid])
            {
                case 0:
                    if (i == 2) goto done;
                    break;
                case 1:
                    a[tid] += 10;
                    break;
                case 2:
                    a[tid] += 20;
                    break;
                case 3:
                    a[tid] += 100;
                    break;
                case 4:
                    a[tid] += 1000;
                    break;
                default:
                    break;
            }
            a[tid] += 1;
        }
        a[tid] += 5;
    done:
        a[tid] += 7;
    }

    // K4: a continue of a do loop two switches deep
    [GpuKernel]
    public static void ContinueInNestedSwitchKernel(int[] a, int[] mode)
    {
        int tid = ThreadIndex.X;
        int i = 0;
        do
        {
            i++;
            Gpu.SyncThreads();
            switch (mode[tid] & 1)
            {
                case 0:
                    switch (mode[tid] >> 1)
                    {
                        case 0:
                            continue;
                        case 1:
                            a[tid] += 100;
                            break;
                        default:
                            break;
                    }
                    break;
                case 1:
                    a[tid] += 10;
                    break;
                default:
                    break;
            }
            a[tid] += 1;
        } while (i < 3);
    }
}
