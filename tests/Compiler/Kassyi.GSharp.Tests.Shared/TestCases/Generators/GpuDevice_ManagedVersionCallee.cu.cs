using Kassyi.GSharp.Core;

namespace TestNamespace.ManagedVersionCaching;

public struct CacheValue
{
    public int V;

    public CacheValue(int v) => V = v;

    [GpuOperatorMacro(typeof(ManagedVersionHelpers), nameof(ManagedVersionHelpers.Add))]
    public static CacheValue operator +(CacheValue left, CacheValue right) => throw new GpuOnlyException();
}

// The test turns the sum of the fields into the operator macro, which gives Sum a managed version (#711)
public static partial class ManagedVersionHelpers
{
    [GpuDeviceFunction]
    public static void Add(ref CacheValue result, CacheValue left, CacheValue right)
    {
        result.V = left.V + right.V;
    }

    [GpuDeviceFunction]
    public static int Sum(int a, int b)
    {
        CacheValue x = new CacheValue(a);
        CacheValue y = new CacheValue(b);
        CacheValue z = new CacheValue(0);
        z.V = x.V + y.V;
        return z.V;
    }
}
