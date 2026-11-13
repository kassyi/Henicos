#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// The forms of <see cref="ThrowOrderDifferentialKernels.ThrowOrderKernel"/>: one operand writes memory (a call, an
/// assignment, <c>++</c>; <c>o[1]</c> records the write), another holds a check that may throw (the null check of
/// <c>a</c>, the check of an element of <c>s</c>).
/// </summary>
public static class ThrowOrderForms
{
    // --- The write first, the check later: C# makes the write, then throws ---
    public const int CallThenLength = 0;
    public const int AssignmentThenLength = 1;
    public const int IncrementThenLength = 2;
    public const int CallThenLengthArgument = 3;
    public const int CallThenCheckedArgument = 4;
    public const int CallThenNotNull = 5;
    public const int IndexCallThenCompoundLength = 6;
    public const int IndexCallThenAssignedLength = 7;

    // --- The check first, the write later: C# throws before the write ---
    public const int LengthThenCall = 8;
    public const int LengthThenAssignment = 9;
    public const int LengthThenIncrement = 10;
    public const int LengthArgumentThenCall = 11;
    public const int IndexLengthThenCompoundCall = 12;
    public const int IndexLengthThenAssignedCall = 13;

    // --- C# checks in the callee, after every argument: the write is made ---
    public const int CheckedArgumentThenCall = 14;

    // --- A simple assignment checks its array element when it stores the value, after the right side; a compound
    // assignment as it reads the element, before the right side ---
    public const int ElementThenAssignedCall = 15;
    public const int ReadIndexElementThenAssignedCall = 16;
    public const int ElementThenCompoundCall = 17;
    public const int NullElementThenAssignedCall = 18;

    // --- An element of a struct array: C# on the CLR checks it before the right side (ldelema), also in a simple
    // assignment ---
    public const int StructElementThenAssignedCall = 19;
    public const int StructReadIndexElementThenAssignedCall = 20;

    public const int Count = 21;

    /// <summary>Whether the form reads <c>a</c>, so that a null <c>a</c> makes it throw.</summary>
    public static bool ReadsA(int form) => form is < ElementThenAssignedCall or NullElementThenAssignedCall;

    /// <summary>Whether the form stores into <c>s</c>, so that a null <c>s</c> makes it throw.</summary>
    public static bool StoresS(int form) => form >= StructElementThenAssignedCall;
}

/// <summary>An element of the struct array of <see cref="ThrowOrderDifferentialKernels.ThrowOrderKernel"/>.</summary>
public struct ThrowOrderPair
{
    public int X;
    public int Y;
}

/// <summary>
/// Kernels whose operands write memory on one side and hold a check that may throw on the other (#1786 (a throwing
/// check in a later operand runs before an earlier call on MSVC)): C# evaluates the operands from left to right, so the
/// write of an earlier operand is made before the exception of a later one and the write of a later operand is never
/// made after the exception of an earlier one. Thread 0 evaluates one form (<see cref="ThrowOrderForms"/>).
/// </summary>
#pragma warning disable CS8602, CS8604 // C#: NullReferenceException on a null array, which the tests check
public static partial class ThrowOrderDifferentialKernels
{
    [GpuKernel]
    public static void ThrowOrderKernel(int[] o, int[]? a, ThrowOrderPair[]? s, int k, int form)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        switch (form)
        {
            case ThrowOrderForms.CallThenLength:
                o[0] = Bump(o) + a.Length;
                break;
            case ThrowOrderForms.AssignmentThenLength:
                o[0] = (o[1] = 5) + a.Length;
                break;
            case ThrowOrderForms.IncrementThenLength:
                o[0] = o[1]++ + a.Length;
                break;
            case ThrowOrderForms.CallThenLengthArgument:
                o[0] = Add(Bump(o), a.Length);
                break;
            case ThrowOrderForms.CallThenCheckedArgument:
                o[0] = AddLength(Bump(o), a);
                break;
            case ThrowOrderForms.CallThenNotNull:
                o[0] = Bump(o) + Gpu.NotNull(a).Length;
                break;
            case ThrowOrderForms.IndexCallThenCompoundLength:
                o[Bump(o)] += a.Length;
                break;
            case ThrowOrderForms.IndexCallThenAssignedLength:
                o[Bump(o)] = a.Length;
                break;
            case ThrowOrderForms.LengthThenCall:
                o[0] = a.Length + Bump(o);
                break;
            case ThrowOrderForms.LengthThenAssignment:
                o[0] = a.Length + (o[1] = 5);
                break;
            case ThrowOrderForms.LengthThenIncrement:
                o[0] = a.Length + o[1]++;
                break;
            case ThrowOrderForms.LengthArgumentThenCall:
                o[0] = Add(a.Length, Bump(o));
                break;
            case ThrowOrderForms.IndexLengthThenCompoundCall:
                o[a.Length] += Bump(o);
                break;
            case ThrowOrderForms.IndexLengthThenAssignedCall:
                o[a.Length] = Bump(o);
                break;
            case ThrowOrderForms.CheckedArgumentThenCall:
                o[0] = LengthAdd(a, Bump(o));
                break;
            case ThrowOrderForms.ElementThenAssignedCall:
                o[k] = Bump(o);
                break;
            case ThrowOrderForms.ReadIndexElementThenAssignedCall:
                o[Pick(o, k)] = Bump(o);
                break;
            case ThrowOrderForms.ElementThenCompoundCall:
                o[k] += Bump(o);
                break;
            case ThrowOrderForms.NullElementThenAssignedCall:
                a[Pick(o, k)] = Bump(o);
                break;
            case ThrowOrderForms.StructElementThenAssignedCall:
                s[k] = Make(o);
                break;
            case ThrowOrderForms.StructReadIndexElementThenAssignedCall:
                s[Pick(o, k)] = Make(o);
                break;
        }
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
