#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>An element of the struct arrays of <see cref="ThrowOrderDifferentialKernels"/>.</summary>
public struct ThrowOrderPair
{
    public int X;
    public int Y;
}

/// <summary>
/// Kernels whose operands write memory on one side and hold a check that may throw on the other (#1786 (a throwing
/// check in a later operand runs before an earlier call on MSVC)): C# evaluates the operands from left to right, so the
/// write of an earlier operand is made before the exception of a later one and the write of a later operand is never
/// made after the exception of an earlier one. <c>o[1]</c> records the write (a call, an assignment, <c>++</c>); the
/// check is the null check of <c>a</c> or of the element of <c>s</c>. Each kernel runs on one thread.
/// </summary>
#pragma warning disable CS8602, CS8604 // C#: NullReferenceException on a null array, which the tests check
public static partial class ThrowOrderDifferentialKernels
{
    // --- The write first, the check later: C# makes the write, then throws ---

    [GpuKernel]
    public static void CallThenLengthKernel(int[] o, int[]? a) => o[0] = Bump(o) + a.Length;

    [GpuKernel]
    public static void AssignmentThenLengthKernel(int[] o, int[]? a) => o[0] = (o[1] = 5) + a.Length;

    [GpuKernel]
    public static void IncrementThenLengthKernel(int[] o, int[]? a) => o[0] = o[1]++ + a.Length;

    [GpuKernel]
    public static void CallThenLengthArgumentKernel(int[] o, int[]? a) => o[0] = Add(Bump(o), a.Length);

    // C# throws in AddLength, after every argument; G# checks the non-nullable argument at the call
    [GpuKernel]
    public static void CallThenCheckedArgumentKernel(int[] o, int[]? a) => o[0] = AddLength(Bump(o), a);

    [GpuKernel]
    public static void CallThenNotNullKernel(int[] o, int[]? a) => o[0] = Bump(o) + Gpu.NotNull(a).Length;

    [GpuKernel]
    public static void IndexCallThenCompoundLengthKernel(int[] o, int[]? a) => o[Bump(o)] += a.Length;

    [GpuKernel]
    public static void IndexCallThenAssignedLengthKernel(int[] o, int[]? a) => o[Bump(o)] = a.Length;

    // --- The check first, the write later: C# throws before the write ---

    [GpuKernel]
    public static void LengthThenCallKernel(int[] o, int[]? a) => o[0] = a.Length + Bump(o);

    [GpuKernel]
    public static void LengthThenAssignmentKernel(int[] o, int[]? a) => o[0] = a.Length + (o[1] = 5);

    [GpuKernel]
    public static void LengthThenIncrementKernel(int[] o, int[]? a) => o[0] = a.Length + o[1]++;

    [GpuKernel]
    public static void LengthArgumentThenCallKernel(int[] o, int[]? a) => o[0] = Add(a.Length, Bump(o));

    [GpuKernel]
    public static void IndexLengthThenCompoundCallKernel(int[] o, int[]? a) => o[a.Length] += Bump(o);

    [GpuKernel]
    public static void IndexLengthThenAssignedCallKernel(int[] o, int[]? a) => o[a.Length] = Bump(o);

    // C# throws in LengthAdd, after every argument: the write is made
    [GpuKernel]
    public static void CheckedArgumentThenCallKernel(int[] o, int[]? a) => o[0] = LengthAdd(a, Bump(o));

    // --- A simple assignment checks its array element when it stores the value, after the right side, for an int
    // array and a struct array alike; a compound assignment checks it as it reads the element, before ---

    [GpuKernel]
    public static void ElementThenAssignedCallKernel(int[] o, int k) => o[k] = Bump(o);

    [GpuKernel]
    public static void ReadIndexElementThenAssignedCallKernel(int[] o, int k) => o[Pick(o, k)] = Bump(o);

    [GpuKernel]
    public static void ElementThenCompoundCallKernel(int[] o, int k) => o[k] += Bump(o);

    [GpuKernel]
    public static void NullElementThenAssignedCallKernel(int[] o, int[]? a, int k) => a[Pick(o, k)] = Bump(o);

    [GpuKernel]
    public static void StructElementThenAssignedCallKernel(int[] o, ThrowOrderPair[]? s, int k) => s[k] = Make(o);

    [GpuKernel]
    public static void StructReadIndexElementThenAssignedCallKernel(int[] o, ThrowOrderPair[]? s, int k) =>
        s[Pick(o, k)] = Make(o);

    // --- A deconstruction names every target location, its element check included, before the values ---

    [GpuKernel]
    public static void DeconstructionElementThenCallKernel(int[] o, int[]? a, int k)
    {
        int x;
        (a[k], x) = (Bump(o), 1);
        o[0] = x;
    }

    [GpuKernel]
    public static void DeconstructionStructElementThenCallKernel(int[] o, ThrowOrderPair[]? s, int k)
    {
        int x;
        (s[k], x) = (Make(o), 1);
        o[0] = x;
    }

    // Writes memory and returns 1, a valid index of o
    [GpuDeviceFunction]
    private static int Bump(int[] o)
    {
        o[1] = 5;
        return 1;
    }

    // Writes memory and returns a pair
    [GpuDeviceFunction]
    private static ThrowOrderPair Make(int[] o)
    {
        o[1] = 5;
        return new ThrowOrderPair { X = 3, Y = 4 };
    }

    [GpuDeviceFunction]
    private static int Add(int x, int y) => x * 10 + y;

    // values is non-nullable: the caller checks it at the call
    [GpuDeviceFunction]
    private static int AddLength(int x, int[] values) => x * 10 + values.Length;

    [GpuDeviceFunction]
    private static int LengthAdd(int[] values, int x) => values.Length * 10 + x;

    // k, after a read of memory (o[0]), so that the index interferes with a call that writes memory
    [GpuDeviceFunction]
    private static int Pick(int[] o, int k) => o[0] < 1000 ? k : 0;
}
