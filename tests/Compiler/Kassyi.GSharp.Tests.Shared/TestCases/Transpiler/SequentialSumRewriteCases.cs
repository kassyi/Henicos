using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

public static partial class SequentialSumForms
{
    [GpuDeviceFunction]
    public static double RewriteAllForms(float[] f, double[] d, int[] n)
    {
        float f0 = f.AsStackEnumerable().Sum();
        float f1 = f.AsStackEnumerable().Sum(x => x * 2f);
        float f2 = f.AsStackEnumerable().Where(x => x > 0f).Select(x => x + 1f).Take(4).Sum();
        float f3 = StackLinqExtensions.Sum(f.AsStackEnumerable());
        float f4 = StackLinqExtensions.Sum(f.AsStackEnumerable(), x => x * 3f);
        float f5 = StackLinqExtensions.Sum<ArrayEnumerator<float>>(f.AsStackEnumerable());

        double d0 = d.AsStackEnumerable().Sum();
        double d1 = d.AsStackEnumerable().Sum(x => x * 2.0);
        double d2 = d.AsStackEnumerable().Where(x => x > 0.0).Select(x => x + 1.0).Take(4).Sum();
        double d3 = StackLinqExtensions.Sum(d.AsStackEnumerable());
        double d4 = StackLinqExtensions.Sum(d.AsStackEnumerable(), x => x * 3.0);

        int i0 = n.AsStackEnumerable().Sum();
        int i1 = n.AsStackEnumerable().Sum(x => x + 1);
        float fp = f.AsStackEnumerable().AsParallel().Sum();

        return f0 + f1 + f2 + f3 + f4 + f5 + d0 + d1 + d2 + d3 + d4 + i0 + i1 + fp;
    }
}
