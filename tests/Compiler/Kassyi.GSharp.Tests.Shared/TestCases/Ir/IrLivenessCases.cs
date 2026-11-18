using Kassyi.GSharp.Core;

namespace IrLiveness;

public static class IrLivenessCases
{
    public static void AssignedInOneArm(int[] a, bool p)
    {
        int x = 1;
        Gpu.SyncThreads();
        if (p) { x = 2; }
        a[0] = x;
    }

    public static void WrittenBeforeRead(int[] a)
    {
        int x = 1;
        a[1] = x;
        Gpu.SyncThreads();
        x = 2;
        a[0] = x;
    }

    public static void LoopCarried(int[] a, int n)
    {
        int s = 0;
        int i = 0;
        while (i < n) { s += a[i]; Gpu.SyncThreads(); i++; }
        a[0] = s;
    }

    public static void ShuffleValue(int[] a)
    {
        int v = a[0];
        int r = Warp.ShuffleSync(0xFFFFFFFFu, v, 0);
        a[1] = r;
    }

    public static void CompoundAfterSplit(int[] a)
    {
        int x = a[0];
        Gpu.SyncThreads();
        x += 1;
        a[1] = x;
    }

    public static void WrittenInLoopOnly(int[] a, int n)
    {
        int x = 0;
        int i = 0;
        Gpu.SyncThreads();
        while (i < n) { x = i; i++; }
        a[0] = x;
    }

    public static void WrittenInBothArms(int[] a, bool c)
    {
        int x = 0;
        Gpu.SyncThreads();
        int y = c ? (x = 1) : (x = 2);
        a[0] = x + y;
    }

    public static void OutVarBeforeSplit(int[] a)
    {
        Read(a, out var y);
        Gpu.SyncThreads();
        a[0] = y;
    }

    public static void UnusedAfterSplit(int[] a)
    {
        int x = a[0];
        a[1] = x;
        Gpu.SyncThreads();
        a[2] = 0;
    }

    public static void StackArrayPinnedInLoop(int[] a, int n)
    {
        int i = 0;
        while (i < n)
        {
            Gpu.SyncThreads();
            int[] s = new int[4];
            s[0] = i;
            a[i] = Sum(s);
            i++;
        }
    }

    public static void StackArrayElementsOnly(int[] a)
    {
        int[] s = new int[4];
        s[0] = a[0];
        a[1] = s.Length;
        Gpu.SyncThreads();
        a[2] = 0;
    }

    public static void ReadBeforeSplitInLoop(int[] a, int n)
    {
        int k = a[0];
        int i = 0;
        while (i < n) { a[i] = k; Gpu.SyncThreads(); i++; }
    }

    public static void NoSplit(int[] a, bool c)
    {
        int x = a[0];
        if (c) { a[1] = x; }
    }

    private static void Read(int[] a, out int y) { y = a[0]; }

    private static int Sum(int[] s) => s[0] + s[1] + s[2] + s[3];
}
