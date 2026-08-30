// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public struct Point2D
{
    public int X;
    public int Y;

    // Parameterless instance method & implicit field access
    public void ResetImplicit()
    {
        X = 1;
        Y = 2;
    }

    // Explicit field access using `this.` and calling another instance method via `this.`
    public void MethodB()
    {
        this.Y = this.Y + 10;
    }

    public void ResetExplicitThis()
    {
        this.X = 100;
        this.MethodB();
    }

    // Instance method with parameter and return value using `this.`
    public int Add(int val)
    {
        return this.X + val;
    }
}

public struct IntBox
{
    public int Value;

    public int GetValue()
    {
        return this.Value;
    }

    public void SetValue(int val)
    {
        this.Value = val;
    }
}

public struct DirectCalc
{
    public int BaseVal;

    public DirectCalc(int baseVal)
    {
        BaseVal = baseVal;
    }

    public int Compute(int factor)
    {
        return BaseVal * factor;
    }
}

public struct MergeableStruct
{
    public int Value;

    public void Merge(MergeableStruct other)
    {
        this.Value += other.Value;
    }
}

public static class SYN_95_StructInstanceMethodsKernels
{
    // Happy Path 1: Parameterless method & implicit field access
    [GpuKernel]
    public static void ImplicitFieldAccessKernel(Point2D[] points)
    {
        int tid = ThreadIndex.X;
        Point2D p = points[tid];
        p.ResetImplicit();
        points[tid] = p;
    }

    // Happy Path 2: Explicit `this` access & `this.MethodB()` call
    [GpuKernel]
    public static void ExplicitThisMethodCallKernel(Point2D[] points)
    {
        int tid = ThreadIndex.X;
        Point2D p = points[tid];
        p.ResetExplicitThis();
        points[tid] = p;
    }

    // Happy Path 3: Method with arguments & return value
    [GpuKernel]
    public static void MethodWithArgsAndReturnKernel(Point2D[] points, int[] results, int val)
    {
        int tid = ThreadIndex.X;
        Point2D p = points[tid];
        results[tid] = p.Add(val);
    }

    // Edge Case 1: Struct instance method with state modification & getter/setter methods
    [GpuKernel]
    public static void IntBoxMethodKernel(IntBox[] boxes, int newValue)
    {
        int tid = ThreadIndex.X;
        IntBox box = boxes[tid];
        box.SetValue(newValue);
        boxes[tid] = box;
    }

    // Edge Case 2: Direct call without temporary variable (new DirectCalc(5).Compute(3))
    [GpuKernel]
    public static void DirectCallKernel(int[] results)
    {
        int tid = ThreadIndex.X;
        results[tid] = new DirectCalc(5 + tid).Compute(3);
    }

    // Edge Case 3: Instance method receiving same struct type parameter
    [GpuKernel]
    public static void SameTypeParameterKernel(MergeableStruct[] items)
    {
        int tid = ThreadIndex.X;
        MergeableStruct first = items[0];
        MergeableStruct current = items[tid];
        current.Merge(first);
        items[tid] = current;
    }
}
