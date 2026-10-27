// User types that share the names of the Core intrinsic hosts: SyncIntrinsicClassifier must not take them for intrinsics
namespace SyncIntrinsicCases;

public static class Gpu
{
    public static void SyncThreads()
    {
    }

    public static void SyncGrid()
    {
    }

    public static int LaneId() => 0;
}

public static class Warp
{
    public static int ShuffleDownSync(uint mask, int value, int delta) => value;
}

public static class ThreadIndex
{
    public static int X => 0;
}
