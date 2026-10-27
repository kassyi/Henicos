// K20 has a label of the user that is the shape under test, not a jump target
#pragma warning disable CS0164
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Kernels whose <c>Gpu.SyncGrid(body)</c> bodies must run on every dialect, with a grid sync between their top-level
/// statements (#626). Each statement after a gap reads what other blocks wrote before it.
/// </summary>
public static partial class SyncGridBodyKernels
{
    // K1: the body runs
    [GpuKernel(Cooperative = true)]
    public static void BodyRunsKernel(int[] a)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        Gpu.SyncGrid(() =>
        {
            a[tid] = tid;
            a[tid] += 1;
        });
    }

    // K2: the grid is synchronized between the statements (thread 31 reads what block 1 wrote)
    [GpuKernel(Cooperative = true)]
    public static void NeighborKernel(int[] a, int[] b)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int n = GridDimension.X * BlockDimension.X;
        Gpu.SyncGrid(() =>
        {
            a[tid] = tid + 1;
            b[tid] = a[(tid + 1) % n];
        });
    }

    // K3: a nested block (a loop macro body, an if) is one statement; nothing inside it is synchronized. A grid sync
    // there would wait for threads that never reach it
    [GpuKernel(Cooperative = true)]
    public static void NestedKernel(int[] a, int n)
    {
        Gpu.SyncGrid(() =>
        {
            Gpu.GridStrideLoop(n, i =>
            {
                a[i] += 1;
                a[i] *= 2;
            });
            if (ThreadIndex.X == 0)
            {
                a[BlockIndex.X * 2] += 1;
                a[BlockIndex.X * 2 + 1] += 1;
            }
        });
    }

    // K4: empty statements, an explicit sync, an expression body and an empty nested body add no extra sync
    [GpuKernel(Cooperative = true)]
    public static void MixedKernel(int[] a, int[] b)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int n = GridDimension.X * BlockDimension.X;
        Gpu.SyncGrid(() =>
        {
            a[tid] = tid + 1;
            ;
            Gpu.SyncGrid();
            b[tid] = a[(tid + 1) % n];
            Gpu.SyncGrid(() => b[tid] += 100);
            Gpu.SyncGrid(() =>
            {
            });
        });
    }

    // K5: a local of the body lives across the gaps
    [GpuKernel(Cooperative = true)]
    public static void HoistedLocalKernel(int[] a, int[] b)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int n = GridDimension.X * BlockDimension.X;
        Gpu.SyncGrid(() =>
        {
            int mine = tid + 1;
            a[tid] = mine;
            b[tid] = a[(tid + 1) % n] + mine;
        });
    }

    // K6: a body inside a uniform loop syncs between its own statements in every iteration; the explicit sync at the end
    // keeps the next iteration's write after this iteration's read
    [GpuKernel(Cooperative = true)]
    public static void LoopNestedKernel(int[] a, int[] b)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int n = GridDimension.X * BlockDimension.X;
        for (int k = 0; k < 2; k++)
        {
            Gpu.SyncGrid(() =>
            {
                a[tid] = tid + 1 + k;
                b[tid] += a[(tid + 1) % n];
                Gpu.SyncGrid();
            });
        }
    }

    // K7: sibling bodies declare a local of the same name
    [GpuKernel(Cooperative = true)]
    public static void SiblingLocalsKernel(int[] a, int[] b)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        Gpu.SyncGrid(() =>
        {
            int v = tid;
            a[tid] = v;
            a[tid] += v;
        });
        Gpu.SyncGrid(() =>
        {
            int v = tid * 2;
            b[tid] = v;
            b[tid] += v;
        });
    }
    // K8: a return that every thread takes the same way ends the rest of the body, not the kernel
    [GpuKernel(Cooperative = true)]
    public static void UniformReturnKernel(int[] a, int[] b, int n)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int size = GridDimension.X * BlockDimension.X;
        Gpu.SyncGrid(() =>
        {
            a[tid] = tid + 1;
            if (n == 0) return;
            b[tid] = a[(tid + 1) % size];
            b[tid] += 100;
        });
        a[tid] += 1000;
    }

    // K9: threads that return still take part in the grid syncs of the remaining statements
    [GpuKernel(Cooperative = true)]
    public static void DivergentReturnKernel(int[] a, int[] b)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int size = GridDimension.X * BlockDimension.X;
        Gpu.SyncGrid(() =>
        {
            a[tid] = tid + 1;
            if (tid % 2 == 0) return;
            b[tid] = a[(tid + 1) % size];
        });
    }

    // K10: a return in a nested body skips the rest of that body only
    [GpuKernel(Cooperative = true)]
    public static void NestedReturnKernel(int[] a, int[] b)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int size = GridDimension.X * BlockDimension.X;
        Gpu.SyncGrid(() =>
        {
            a[tid] = 1;
            Gpu.SyncGrid(() =>
            {
                if (tid < 32) return;
                a[tid] = 2;
            });
            b[tid] = a[(tid + 1) % size];
        });
    }

    // K11: a return inside a loop of the body leaves the loop and the body; a return of a local function is its own
    [GpuKernel(Cooperative = true)]
    public static void ReturnFromLoopKernel(int[] a, int[] b)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        Gpu.SyncGrid(() =>
        {
            for (int i = 0; i < 4; i++)
            {
                a[tid] += 1;
                if (i == 1) return;
            }
            b[tid] = 7;
        });
        Gpu.SyncGrid(() =>
        {
            void Add()
            {
                if (tid > 1000) return;
                a[tid] += 10;
            }

            Add();
            b[tid] += 1;
        });
    }

    // K12: locals declared after a return stay visible to the statements after them; their initializers do not run on
    // the threads that returned
    [GpuKernel(Cooperative = true)]
    public static void DeclarationAfterReturnKernel(int[] a, int[] b, int[] c, int[] counter, int n)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        Gpu.SyncGrid(() =>
        {
            if (tid >= n) return;
            int v = a[tid];
            var w = v * 2;
            int x = 1, y = x + w;
            const int k = 5;
            int z = ++counter[tid];
            b[tid] = v + y + k;
            c[tid] = z;
        });
    }
    // K13: a body in a device function (the NTT shape): one statement is a call that suspends at a block barrier
    [GpuKernel(Cooperative = true)]
    public static void DeviceFunctionKernel(int[] a, int[] b, int[] c)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int size = GridDimension.X * BlockDimension.X;
        Step(a, b, c, tid, size);
    }

    [GpuDeviceFunction]
    public static void Step(int[] a, int[] b, int[] c, int tid, int n)
    {
        Gpu.SyncGrid(() =>
        {
            a[tid] = tid + 1;
            ShiftInBlock(c, tid);
            b[tid] = a[(tid + 1) % n];
        });
    }

    // c[t] = t, then each thread reads its neighbour in the block after a block barrier
    [GpuDeviceFunction]
    public static void ShiftInBlock(int[] c, int tid)
    {
        c[tid] = tid;
        Gpu.SyncThreads();
        int lane = tid % BlockDimension.X;
        int next = tid - lane + (lane + 1) % BlockDimension.X;
        int value = c[next];
        Gpu.SyncThreads();
        c[tid] = value;
    }

    // K14: a body in a loop of another body is expanded too, and syncs between its own statements in every iteration
    [GpuKernel(Cooperative = true)]
    public static void BodyInLoopOfBodyKernel(int[] a, int[] b)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int size = GridDimension.X * BlockDimension.X;
        Gpu.SyncGrid(() =>
        {
            b[tid] = 0;
            for (int k = 0; k < 2; k++)
            {
                Gpu.SyncGrid(() =>
                {
                    a[tid] = tid + 1 + k;
                    b[tid] += a[(tid + 1) % size];
                    Gpu.SyncGrid();
                });
            }
            b[tid] += 1000;
        });
    }

    // K15: an expression body that is itself a Gpu.SyncGrid(body)
    [GpuKernel(Cooperative = true)]
    public static void ExpressionBodyIsBodyKernel(int[] a, int[] b)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int size = GridDimension.X * BlockDimension.X;
        Gpu.SyncGrid(() => Gpu.SyncGrid(() =>
        {
            a[tid] = tid + 1;
            b[tid] = a[(tid + 1) % size];
        }));
    }

    // K16: out variables declared after a return (an expression statement, an if condition, a local initializer) stay
    // visible to the later statements
    [GpuKernel(Cooperative = true)]
    public static void OutVariableAfterReturnKernel(int[] a, int[] b, int n)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        Gpu.SyncGrid(() =>
        {
            if (tid >= n) return;
            ReadPlusOne(a, tid, out var x);
            if (!TryDouble(a, tid, out int y)) y = -1;
            int s = ReadPlusOne(a, tid, out var z) + 1;
            b[tid] = x + y + s + z;
        });
    }

    [GpuDeviceFunction]
    public static int ReadPlusOne(int[] a, int i, out int value)
    {
        value = a[i] + 1;
        return value;
    }

    [GpuDeviceFunction]
    public static bool TryDouble(int[] a, int i, out int value)
    {
        value = a[i] * 2;
        return value >= 0;
    }

    // K17: a return inside a switch of the user, which has a case with the number of the next state (2 on the CPU)
    [GpuKernel(Cooperative = true)]
    public static void ReturnInSwitchKernel(int[] a, int[] b, int[] mode)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int size = GridDimension.X * BlockDimension.X;
        Gpu.SyncGrid(() =>
        {
            a[tid] = 1;
            switch (mode[tid])
            {
                case 0:
                    return;
                case 2:
                    a[tid] += 100;
                    break;
                default:
                    break;
            }
            b[tid] = a[(tid + 1) % size];
        });
    }

    // K18: a return inside a switch of the user without a case of that number
    [GpuKernel(Cooperative = true)]
    public static void ReturnInSwitchWithoutCaseKernel(int[] a, int[] b, int[] mode)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int size = GridDimension.X * BlockDimension.X;
        Gpu.SyncGrid(() =>
        {
            a[tid] = 1;
            switch (mode[tid])
            {
                case 0:
                    return;
                default:
                    break;
            }
            b[tid] = a[(tid + 1) % size];
        });
    }

    // K19: a return inside a foreach statement of the body
    [GpuKernel(Cooperative = true)]
    public static void ReturnInForEachKernel(int[] a, int[] b, int[] values)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int size = GridDimension.X * BlockDimension.X;
        Gpu.SyncGrid(() =>
        {
            a[tid] = 0;
            foreach (var value in values)
            {
                if (value == tid) return;
                a[tid] += 1;
            }
            b[tid] = a[(tid + 1) % size];
        });
    }

    // K20: a return in a statement with a label of the user
    [GpuKernel(Cooperative = true)]
    public static void ReturnInLabeledStatementKernel(int[] a, int[] b)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int size = GridDimension.X * BlockDimension.X;
        Gpu.SyncGrid(() =>
        {
            a[tid] = 1;
            Check: if (tid % 2 == 0) return;
            b[tid] = a[(tid + 1) % size];
        });
    }

    // K21: an outer return, then a nested body with a return of its own: the last statement of the nested body is guarded
    // by both flags
    [GpuKernel(Cooperative = true)]
    public static void NestedReturnAfterReturnKernel(int[] a, int[] b, int[] c)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int size = GridDimension.X * BlockDimension.X;
        Gpu.SyncGrid(() =>
        {
            a[tid] = 1;
            if (tid % 2 == 0) return;
            Gpu.SyncGrid(() =>
            {
                if (tid % 3 == 0) return;
                b[tid] = a[(tid + 1) % size];
            });
            c[tid] = 5;
        });
    }

    // K22: two returns in one body, and a goto that stays inside a guarded statement
    [GpuKernel(Cooperative = true)]
    public static void TwoReturnsKernel(int[] a, int[] b)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        Gpu.SyncGrid(() =>
        {
            if (tid % 2 == 0) return;
            a[tid] = 1;
            for (int i = 0; i < 3; i++)
            {
                if (i == 1) goto next;
                a[tid] += 10;
                next: ;
            }
            if (tid % 3 == 0) return;
            b[tid] = 2;
        });
    }

    // K23: a body with a return in a ForUnroll body: on CUDA each unrolled copy has its own flag and labels
    [GpuKernel(Cooperative = true)]
    public static void ForUnrollReturnKernel(int[] a)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        Gpu.ForUnroll(0, 2, k =>
        {
            Gpu.SyncGrid(() =>
            {
                if (tid < k) return;
                a[tid] += 1;
            });
        });
    }
}
