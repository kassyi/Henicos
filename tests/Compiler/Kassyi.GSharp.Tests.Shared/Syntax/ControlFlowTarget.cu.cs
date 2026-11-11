namespace Kassyi.GSharp.Tests.Shared.Syntax;

using Kassyi.GSharp.Core;

public struct SampleData
{
    public int X;
    public float Y;
}

public class ControlFlowTarget
{
    [GpuDeviceFunction]
    public static void IncrementRef(ref int target)
    {
        target += 1;
    }

    public static void RefConditionalExpression(int[] a, int[] b, bool useFirst)
    {
        IncrementRef(ref (useFirst ? ref a[0] : ref b[0]));
    }

    public static void DefaultLiteral(SampleData[] items, int[] counts)
    {
        SampleData s = default;
        int c = default(int);
        items[0] = s;
        counts[0] = c;
    }
}
