// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

// A record struct is built by a factory function, whose arguments C++ evaluates in an unspecified order (#608)
public record struct EoPair(int X, int Y);

// A [GpuOperatorMacro] operator: c = a - b becomes CombineInto(&c, a, b), a call whose arguments C++ evaluates in an
// unspecified order
public struct EoScalar
{
    public int Value;

    [GpuOperatorMacro(typeof(EvaluationOrderKernels), nameof(EvaluationOrderKernels.CombineInto))]
    public static EoScalar operator -(EoScalar a, EoScalar b) => throw new GpuOnlyException();
}

/// <summary>
/// Expressions whose operands have side effects that another operand observes. C# evaluates the operands from left to
/// right (C# spec §12.4.1); C++ leaves the order of function arguments and of binary operands unspecified and evaluates
/// the right side of an assignment first, so the transpiler must keep the C# order (#608). Each kernel runs on one
/// thread and writes what it computed to <c>result</c>; the expected values are in
/// <see cref="EvaluationOrderScenarios"/>.
/// </summary>
public static partial class EvaluationOrderKernels
{
    // Returns the counter, then increments it: two calls in one expression return 0 and 1 in the C# order
    [GpuDeviceFunction]
    public static int Next(ref int counter) => counter++;

    [GpuDeviceFunction]
    public static int Sub(int a, int b) => a - b;

    // Not symmetric in a and b, so swapped operands show
    [GpuDeviceFunction]
    public static int Combine(int a, int b) => a * 10 + b;

    // Writes memory that another operand reads
    [GpuDeviceFunction]
    public static int Store(int[] data, int value)
    {
        data[0] = value;
        return value;
    }

    [GpuDeviceFunction]
    public static int Bump(int[] data)
    {
        data[0] += 100;
        return 0;
    }

    // Writes memory, but only memory that no other operand of the test expressions reads
    [GpuDeviceFunction]
    public static int Record(int[] log, int index)
    {
        log[index] += 1;
        return index * 2;
    }

    // No side effect: never a reason to keep its argument in a temporary
    [GpuDeviceFunction]
    public static int Pure(int x) => x * 3 + 1;

    [GpuDeviceFunction]
    public static uint RotR(uint x, int n) => (x >> n) | (x << (32 - n));

    [GpuDeviceFunction]
    public static int ReadIn(in int a, int b) => a * 10 + b;

    [GpuDeviceFunction]
    public static int SplitOut(out int product, int a, int b)
    {
        product = a * b;
        return a * 10 + b;
    }

    [GpuDeviceFunction]
    public static int Accumulate(ref int total)
    {
        total += 10;
        return 1;
    }

    // The target of the EoScalar operator macro
    [GpuDeviceFunction]
    public static void CombineInto(ref EoScalar result, EoScalar left, EoScalar right)
    {
        result.Value = left.Value * 10 + right.Value;
    }

    [GpuDeviceFunction]
    public static EoScalar Take(ref int counter) => new EoScalar { Value = counter++ };

    // Suspends on the CPU dialects, so the state machine lowering spills its arguments
    [GpuDeviceFunction]
    public static int CombineAcrossBarrier(int a, int b)
    {
        Gpu.SyncThreads();
        return a * 10 + b;
    }

    // --- Function arguments (C# spec §12.6.2.3: the arguments are evaluated in order, from left to right) ---

    [GpuKernel]
    public static void CallArgumentsKernel(int[] buffer, int[] scratch, int[] result)
    {
        int i = 0;
        result[0] = Sub(Next(ref i), Next(ref i));

        int j = 0;
        var pair = new EoPair(buffer[j++], buffer[j++]);
        result[1] = pair.X;
        result[2] = pair.Y;

        // A local read before a call that writes it through ref
        int t = 7;
        result[3] = Sub(t, Next(ref t));

        // Memory read before a call that writes it
        scratch[0] = 40;
        result[4] = Sub(scratch[0], Store(scratch, 5));

        // The out argument is declared before the statement; the next two arguments interfere
        int k = 0;
        result[5] = SplitOut(out int product, Next(ref k), k);
        result[6] = product;

        // An in argument names the element of the index evaluated first
        int m = 0;
        result[7] = ReadIn(in buffer[m++], buffer[m++]);
    }

    [GpuKernel]
    public static void VectorArgumentsKernel(float[] buffer, float[] result)
    {
        // Brace initialization: C++ evaluates it from left to right already
        int i = 0;
        var v = new System.Numerics.Vector3(buffer[i++], buffer[i++], buffer[i++]);
        result[0] = v.X;
        result[1] = v.Y;
        result[2] = v.Z;

        // Vector3(Vector2, float) is a call of the function the dialect maps it to
        int j = 0;
        var w = new System.Numerics.Vector3(new System.Numerics.Vector2(buffer[j++], buffer[j++]), buffer[j++]);
        result[3] = w.X;
        result[4] = w.Y;
        result[5] = w.Z;
    }

    // No two arguments interfere: the call stays as written (the snapshot shows no temporaries)
    [GpuKernel]
    public static void NoSpillCallsKernel(int[] a, int[] b, int[] result)
    {
        int tid = ThreadIndex.X;
        int x = a[tid];
        result[0] = Combine(Pure(x), a[tid]);
        result[1] = Combine(a[tid], b[tid]);
        int i = 0;
        result[2] = System.Math.Max(Next(ref i), 5);
    }

    // On the CPU dialects the state machine lowering moves the arguments of a suspending call into temporaries
    [GpuKernel]
    public static void SuspendingCallArgumentsKernel(int[] result)
    {
        int a = 0;
        result[0] = CombineAcrossBarrier(a, Next(ref a));
        result[1] = a;
    }

    // --- Operator macros (the operands of the operator, from left to right) ---

    [GpuKernel]
    public static void OperatorMacroKernel(int[] result)
    {
        int i = 0;
        EoScalar c = default;
        c = Take(ref i) - Take(ref i);
        result[0] = c.Value;
    }

    // The operands are locals that nothing changes: the call stays as written
    [GpuKernel]
    public static void NoSpillOperatorMacroKernel(int[] input, int[] result)
    {
        EoScalar a = default;
        EoScalar b = default;
        EoScalar c = default;
        a.Value = input[0];
        b.Value = input[1];
        c = a - b;
        result[0] = c.Value;
    }

    // --- Assignment (C# spec §12.21.2: the location of x first, then y) ---

    [GpuKernel]
    public static void AssignmentKernel(int[] a, int[] b, int[] e)
    {
        int i = 1;
        a[i] = Next(ref i);

        int c = 0;
        b[c++] = c;

        int n = 0;
        e[++n] = n;
    }

    // --- Compound assignment (C# spec §12.21.4: the location of x, the value of x, then y) ---

    [GpuKernel]
    public static void CompoundAssignmentKernel(int[] d, int[] result)
    {
        int i = 0;
        d[Next(ref i)] += Next(ref i);

        int sum = 5;
        sum += Accumulate(ref sum);
        result[0] = sum;
    }

    // The right side writes memory that the left side does not read: the assignments stay as written
    [GpuKernel]
    public static void NoSpillAssignmentsKernel(int[] output, int[] log, int[] result)
    {
        int tid = ThreadIndex.X;
        output[tid] = Record(log, tid);
        int sum = 0;
        for (int t = 0; t < 4; t++)
        {
            sum += Record(log, t);
        }

        result[0] = sum;
    }

    // --- Binary operators (C# spec §12.4.1: the operands are evaluated from left to right) ---

    [GpuKernel]
    public static void BinaryOperatorKernel(int[] data, int[] result)
    {
        int i = 0;
        result[0] = Next(ref i) - Next(ref i);

        int j = 0;
        result[1] = Next(ref j) * 10 + Next(ref j);

        data[0] = 7;
        result[2] = data[0] + Bump(data);

        // << evaluates its left operand first in C++17; | does not
        int k = 0;
        result[3] = (Next(ref k) << 4) | Next(ref k);

        // The condition of ?: is evaluated first in C++; < is not
        int m = 0;
        result[4] = Next(ref m) < Next(ref m) ? 1 : 0;
    }

    // Neither operand has a side effect: the operators stay as written
    [GpuKernel]
    public static void NoSpillBinaryKernel(uint[] input, int[] a, int[] b, uint[] rotated, int[] sums)
    {
        int tid = ThreadIndex.X;
        uint x = input[tid];
        rotated[tid] = RotR(x, 2) ^ RotR(x, 13);
        sums[tid] = a[tid] + b[tid];
    }
}
