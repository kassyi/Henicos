using Kassyi.GSharp.Core;

namespace RecursionCases;

// A -> B -> A: A returns the thread index on its base case, B passes on what A returns. Whichever kernel is analyzed
// first, both calls return a thread-variant value (#772 review item 4)
public static class Recursion
{
    [GpuDeviceFunction]
    public static int A(int n) => n <= 0 ? ThreadIndex.X : B(n - 1);

    [GpuDeviceFunction]
    public static int B(int n) => n <= 0 ? 0 : A(n - 1);

    [GpuKernel]
    public static void KernelA(int[] output)
    {
        output[0] = A(3);
    }

    [GpuKernel]
    public static void KernelB(int[] output)
    {
        output[0] = B(3);
    }

    // MaskA -> MaskB -> MaskA, where the recursive call passes the thread index: MaskA returns p, so the outer call
    // returns the thread index although its own arguments are uniform (#772 second review)
    [GpuDeviceFunction]
    public static int MaskA(int p, bool q)
    {
        if (q)
        {
            return p;
        }

        return MaskB(p);
    }

    [GpuDeviceFunction]
    public static int MaskB(int p) => MaskA(ThreadIndex.X, true);

    [GpuKernel]
    public static void MaskKernel(int[] output)
    {
        output[0] = MaskA(0, false);
    }
}
