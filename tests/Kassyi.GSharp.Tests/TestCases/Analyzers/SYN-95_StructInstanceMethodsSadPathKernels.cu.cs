using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public class SampleClass
{
    public int Value;
    public void DoSomething()
    {
        Value += 1;
    }
}

public abstract class BaseClass
{
    public abstract int Compute();
    public virtual int GetValue() => 42;
}

public class DerivedClass : BaseClass
{
    public override int Compute() => 100;
    public override int GetValue() => 200;
}

public struct StructWithOverride
{
    public int X;
    public override string ToString() => X.ToString();
}

public static class SYN_95_StructInstanceMethodsSadPathKernels
{
    [GpuKernel]
    public static void KernelWithClassInstanceMethod(int[] result)
    {
        SampleClass obj = new SampleClass();
        obj.DoSomething();
        result[0] = obj.Value;
    }

    [GpuKernel]
    public static void KernelWithDynamicDispatch(int[] result)
    {
        BaseClass obj = new DerivedClass();
        result[0] = obj.Compute() + obj.GetValue();
    }

    [GpuKernel]
    public static void KernelWithStructOverride(int[] result)
    {
        StructWithOverride s = new StructWithOverride { X = 10 };
        string str = s.ToString();
        result[0] = str.Length;
    }
}
