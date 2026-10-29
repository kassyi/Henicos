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
}
