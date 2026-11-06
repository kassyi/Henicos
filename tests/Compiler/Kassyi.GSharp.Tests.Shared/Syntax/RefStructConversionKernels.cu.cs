using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

// The conversions compute their result, so a body that is ignored (the operand stored in the first field) shows
public ref struct ScaledRef
{
    public int V;

    public static implicit operator ScaledRef(int x) => new ScaledRef { V = x * 2 };

    public static implicit operator int(ScaledRef s) => s.V + 1;
}

public ref struct ExplicitScaledRef
{
    public int V;

    public static explicit operator ExplicitScaledRef(int x) => new ExplicitScaledRef { V = x * 3 };

    public static explicit operator int(ExplicitScaledRef s) => s.V - 1;
}

public static partial class RefStructConversionKernels
{
    [GpuKernel]
    public static void ImplicitRefStructConversionKernel(int[] input, int[] fields, int[] converted)
    {
        int i = ThreadIndex.X;
        ScaledRef s = input[i];
        fields[i] = s.V;
        int back = s;
        converted[i] = back;
    }

    [GpuKernel]
    public static void ExplicitRefStructConversionKernel(int[] input, int[] fields, int[] converted)
    {
        int i = ThreadIndex.X;
        ExplicitScaledRef s = (ExplicitScaledRef)input[i];
        fields[i] = s.V;
        converted[i] = (int)s;
    }
}
