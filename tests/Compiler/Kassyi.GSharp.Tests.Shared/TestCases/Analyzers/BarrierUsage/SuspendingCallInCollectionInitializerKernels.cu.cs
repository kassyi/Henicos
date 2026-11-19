using System.Collections;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public struct SplitBag : IEnumerable
{
    public int Sum;

    public SplitBag(int start)
    {
        Sum = start;
    }

    public IEnumerator GetEnumerator() => throw new System.NotSupportedException();
}

public struct SplitHolder
{
    public int First;
    public SplitBag Inner;
}

public struct SplitItem
{
    public int A;
    public int B;
}

public struct SplitOuter : IEnumerable
{
    public int Sum;

    public IEnumerator GetEnumerator() => throw new System.NotSupportedException();
}

public static partial class SplitBagExtensions
{
    [GpuDeviceFunction]
    public static void Add(this ref SplitBag bag, int value) => bag.Sum += value;

    [GpuDeviceFunction]
    public static void Add(this ref SplitBag bag, int left, int right) => bag.Sum += left * right;

    [GpuDeviceFunction]
    public static void Add(this ref SplitOuter outer, SplitItem item) => outer.Sum += (item.A * 1000) + item.B;
}

public static partial class SuspendingCallInCollectionInitializerKernels
{
    [GpuDeviceFunction]
    public static int Broadcast(int value) => Warp.ShuffleSync(0xFFFFFFFFu, value, 0);

    [GpuDeviceFunction]
    public static int Pair(int left, int right) => (left * 16) + right;

    // The first value C# evaluates in a creation that initializes or is assigned to a local: split as written
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void FirstValueKernel(int[] buffer)
    {
        int x = buffer[ThreadIndex.X];
        var plain = new SplitBag { Broadcast(x), 7 };
        var complex = new SplitBag { { Broadcast(x), x } };
        var member = new SplitHolder { Inner = { Broadcast(x) } };
        var conditional = new SplitBag { buffer.Length > 0 ? Broadcast(x) : 0 };
        SplitBag assigned;
        assigned = new SplitBag { Broadcast(x) };
        var memberCreation = new SplitHolder { Inner = new SplitBag { Broadcast(x) } };
        var constant = new SplitBag { { 1, Broadcast(x) } };
        buffer[ThreadIndex.X] = plain.Sum + complex.Sum + member.Inner.Sum + conditional.Sum + assigned.Sum +
            memberCreation.Inner.Sum + constant.Sum;
    }

    // GSH0062: C# evaluates another value or calls Add or a constructor before the call, which the CPU paths would
    // run first
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void LaterValueKernel(int[] buffer)
    {
        int x = buffer[ThreadIndex.X];
        int k = 0;
        var later = new SplitBag { k++, Broadcast(k) };
        var laterArgument = new SplitBag { { k++, Broadcast(k) } };
        var constructed = new SplitBag(k++) { Broadcast(k) };
        var afterMember = new SplitHolder { First = k++, Inner = { Broadcast(k) } };
        var inCall = Pair(k++, new SplitBag { Broadcast(k) }.Sum);
        var shuffled = new SplitBag { 1, Warp.ShuffleSync(0xFFFFFFFFu, x, 0) };
        var afterCollection = new SplitHolder { Inner = { k++ }, First = Broadcast(k) };
        var afterCreation = new SplitHolder { Inner = new SplitBag { k++ }, First = Broadcast(k) };
        var inElement = new SplitOuter { new SplitItem { A = k++, B = Broadcast(k) } };
        SplitBag first = new SplitBag { k++ }, second = new SplitBag { Broadcast(k) };
        buffer[ThreadIndex.X] = later.Sum + laterArgument.Sum + constructed.Sum + afterMember.Inner.Sum + inCall +
            shuffled.Sum + afterCollection.First + afterCreation.First + inElement.Sum + first.Sum + second.Sum;
    }
}
