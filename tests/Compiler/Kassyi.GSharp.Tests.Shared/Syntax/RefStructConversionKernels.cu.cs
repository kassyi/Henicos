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

// Converts to a ref struct declared after it, which no field ties to it: the C++ definition of ZetaRef has to come
// first all the same
public ref struct AlphaRef
{
    public int V;

    public static implicit operator ZetaRef(AlphaRef a) => new ZetaRef { Pad = 9, W = a.V * 5 };

    public static explicit operator AlphaRef(ZetaRef z) => new AlphaRef { V = z.W - z.Pad };
}

public ref struct ZetaRef
{
    public int Pad;
    public int W;
}

// One conversion to it with a body and one that is an operator macro, which keeps the first-field member: both are
// constructors of MixedRef in C++, which needs one default constructor
public ref struct MixedRef
{
    public int V;

    public static implicit operator MixedRef(int x) => new MixedRef { V = x * 2 };

    [GpuOperatorMacro(typeof(RefStructConversionKernels), nameof(RefStructConversionKernels.FromLong))]
    public static implicit operator MixedRef(long x) => throw new GpuOnlyException();
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

    [GpuKernel]
    public static void RefStructToRefStructConversionKernel(int[] input, int[] pads, int[] converted, int[] back)
    {
        int i = ThreadIndex.X;
        AlphaRef a = new AlphaRef { V = input[i] };
        ZetaRef z = a;
        pads[i] = z.Pad;
        converted[i] = z.W;
        AlphaRef b = (AlphaRef)z;
        back[i] = b.V;
    }

    [GpuKernel]
    public static void MixedRefStructConversionKernel(int[] input, int[] fields)
    {
        int i = ThreadIndex.X;
        MixedRef m = input[i];
        fields[i] = m.V;
    }

    [GpuDeviceFunction]
    public static void FromLong(ref MixedRef result, long x)
    {
        result.V = (int)x;
    }
}
