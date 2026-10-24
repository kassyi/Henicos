using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// The getter of a computed property is transpiled with the struct (#471); a construct it cannot emit there is a failure of
// the kernel's transpilation, reported by the analyzer at the getter
public struct CheckedTwice
{
    public int A;

    public int Twice => checked(A * 2);
}

public static partial class ComputedPropertyGetterFailureKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ReadTwiceKernel(int[] data, CheckedTwice value)
    {
        data[0] = value.Twice;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void UnsubstitutedTypeParameterKernel(int[] data)
    {
        var s = new UnsubstitutedOuter<int>.Inner();
        data[0] = s.Copy;
    }
}

public static class UnsubstitutedOuter<T> where T : unmanaged
{
    public struct Inner
    {
        public int Value;

        public int Copy
        {
            get
            {
                T temp = default;
                return Value;
            }
        }
    }
}
