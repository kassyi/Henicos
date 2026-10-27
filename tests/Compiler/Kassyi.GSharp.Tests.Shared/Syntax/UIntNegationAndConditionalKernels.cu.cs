// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Unary minus on <see langword="uint"/> and a conditional expression whose branches are <see langword="uint"/> and
/// <see langword="int"/>. C# negates a <see langword="uint"/> as a <see langword="long"/> (C# spec §12.4.7.2) and converts
/// both branches of a target-typed conditional to the target type (§12.18); C++ keeps <c>unsigned int</c> for both, so
/// the transpiler writes the C# conversions (#671). <see cref="UIntNegationAndConditionalScenarios"/> compares the
/// results with the same device functions run as C# on the host.
/// </summary>
public static partial class UIntNegationAndConditionalKernels
{
    /// <summary>The <see langword="long"/> results that one negation writes.</summary>
    public const int NegationValues = 3;

    [GpuDeviceFunction]
    public static void Negate(uint u, long[] values, int index)
    {
        long x = -u;
        values[index * NegationValues] = x;
        values[index * NegationValues + 1] = -u * 2;

        // var is auto in C++: the declared type must be long long as well
        var negated = -u;
        values[index * NegationValues + 2] = negated;
    }

    [GpuKernel]
    public static void NegationKernel(uint[] us, long[] values)
    {
        int tid = ThreadIndex.X;
        Negate(us[tid], values, tid);
    }

    /// <summary>The <see langword="long"/> results that one conditional writes.</summary>
    public const int ConditionalValues = 2;

    // uint and int have no common type, so each conditional takes the type of its target (C# 9)
    [GpuDeviceFunction]
    public static void Choose(bool c, uint u, int n, long[] longs, double[] doubles, int index)
    {
        long y = c ? u : n;
        longs[index * ConditionalValues] = y;
        longs[index * ConditionalValues + 1] = c ? u : -1;
        double d = c ? u : n;
        doubles[index] = d;
    }

    // Thread 2 * pair takes c = true, thread 2 * pair + 1 takes c = false
    [GpuKernel]
    public static void ConditionalKernel(uint[] us, int[] ns, long[] longs, double[] doubles)
    {
        int tid = ThreadIndex.X;
        int pair = tid / 2;
        Choose(tid % 2 == 0, us[pair], ns[pair], longs, doubles, tid);
    }
}
