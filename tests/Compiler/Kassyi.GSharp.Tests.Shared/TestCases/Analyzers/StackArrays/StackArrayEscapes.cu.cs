using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers.StackArrays;

public struct EscapeHolder
{
    public int[] Field;
}

// GSH0091: a stack array (a local array, the stack array of a span, the storage of a stackalloc) used where it would
// outlive the C++ scope that holds it
[SkipSnapshotConsistency("Analyzer test case: StackArrayDiagnosticsTests")]
public static unsafe partial class StackArrayEscapeKernels
{
    // X1: returned from a device function
    [GpuDeviceFunction]
    public static int[] ReturnStackArray()
    {
        int[] a = new int[4];
        return a;
    }

    [GpuDeviceFunction]
    public static void Fill(ref int[] target) => target[0] = 1;

    // X2: reassigned, and passed by ref
    [GpuKernel]
    public static void ReassignKernel(int[] other, int[] output)
    {
        int[] a = new int[4];
        a = other;
        Fill(ref a);
        output[0] = a[0];
    }

    // X3: stored in a field of an element and in an element of a jagged array that the kernel receives
    [GpuKernel]
    public static void StoreKernel(int[][] jagged, EscapeHolder[] holders, int[] output)
    {
        int[] a = { 1, 2 };
        holders[0].Field = a;
        jagged[0] = a;
        output[0] = holders[0].Field[0];
    }

    // X4: assigned to a variable of the enclosing block
    [GpuKernel]
    public static void OuterBlockKernel(int[] output)
    {
        scoped Span<int> prev = default;
        {
            Span<int> cur = stackalloc int[4];
            prev = cur;
        }

        output[0] = prev[0];
    }

    // X4b: a stackalloc in the single-statement body of an if
    [GpuKernel]
    public static void IfBodyKernel(int[] output, int c)
    {
        scoped Span<int> s = default;
        if (c > 0) s = stackalloc int[4];
        output[0] = s.Length;
    }

    // X4c: a local array of a switch section assigned to a variable outside the switch
    [GpuKernel]
    public static void SwitchSectionKernel(int[] output, int k)
    {
        int[] outer = default;
        switch (k)
        {
            case 0:
                int[] a = new int[4];
                outer = a;
                break;
        }

        output[0] = outer[0];
    }

    // X9: through a local of the same scope that holds the stack array
    [GpuKernel]
    public static void AliasKernel(int[] output, int[] seed)
    {
        int[] outer = seed;
        for (int it = 0; it < 2; it++)
        {
            int[] a = { 11 * (it + 1), 0, 0, 0 };
            int[] b = a;
            if (it == 0) outer = b;
        }

        output[0] = outer[0];
    }

    [GpuDeviceFunction]
    public static void KeepIn(int[] x, ref int[] slot) => slot = x;

    // X10: through a callee that may store one argument in a ref argument that outlives it
    [GpuKernel]
    public static void CalleeKernel(int[] output, int[] seed)
    {
        int[] outer = seed;
        for (int it = 0; it < 2; it++)
        {
            int[] a = { 11 * (it + 1), 0, 0, 0 };
            if (it == 0) KeepIn(a, ref outer);
        }

        output[0] = outer[0];
    }

    // X11: a ref to an element, a tuple element and a struct that holds the array
    [GpuDeviceFunction]
    public static ref int RefToElement()
    {
        int[] a = new int[4];
        return ref a[0];
    }

    [GpuKernel]
    public static void TupleAndStructKernel(int[] output, int[] seed)
    {
        int[] outer = seed;
        EscapeHolder held = default;
        int x = 0;
        {
            int[] a = new int[4];
            (outer, x) = (a, 1);
            held = new EscapeHolder { Field = a };
        }

        output[0] = outer[0] + held.Field[0] + x;
    }

    // X12: a parameter, a ref local and the target of a pointer
    [GpuKernel]
    public static void ReferenceTargetsKernel(int[] output, int** slot)
    {
        int[] a = new int[4];
        output = a;
        ref int[] alias = ref output;
        alias = a;
        *slot = &a[0];
    }

    // X14: through the value parameter of a local function that writes a captured variable
    [GpuKernel]
    public static void LocalFunctionKernel(int[] output, int[] seed)
    {
        int[] outer = seed;

        void Put(int[] v)
        {
            outer = v;
        }

        for (int it = 0; it < 2; it++)
        {
            int[] a = { 11 * (it + 1), 0 };
            if (it == 0) Put(a);
        }

        output[0] = outer[0];
    }

    // X15: through a local that a ref argument filled, and through a pointer offset
    [GpuKernel]
    public static void RefAndPointerKernel(int[] output, int[] seed)
    {
        int[] outer = seed;
        int* p = null;
        {
            int[] a = new int[4];
            int[] inner = seed;
            KeepIn(a, ref inner);
            outer = inner;
            fixed (int* q = a)
            {
                p = q + 1;
            }
        }

        output[0] = outer[0] + *p;
    }

    // Not reported: a value parameter given a stack array of its own body
    [GpuDeviceFunction]
    public static int OwnArray(int[] p)
    {
        int[] a = { 1, 2 };
        p = a;
        return p[0];
    }

    // Not reported: uses that stay within the scope of the storage, and indices or lengths taken from a stack array
    [GpuKernel]
    public static void SameScopeKernel(int[] output, int* basePtr)
    {
        int[] a = new int[4];
        int[] b = a;
        scoped Span<int> s = stackalloc int[4];
        scoped Span<int> t = default;
        t = s;
        var holder = new EscapeHolder();
        holder.Field = a;
        {
            scoped Span<int> inner = default;
            inner = s;
            output[1] = inner.Length;
        }

        Span<int> o = output;
        scoped Span<int> w = default;
        int* q = null;
        {
            int[] idx = { 1, 2 };
            w = o.Slice(idx[0], idx.Length);
            q = basePtr + idx[0];
        }

        // The parameter of a local function ends before the stack array it is given
        int Peek(int[] p)
        {
            p = a;
            return p[0];
        }

        int OwnLocal(int[] p)
        {
            int[] own = { 3, 4 };
            p = own;
            return p[0];
        }

        int x = 0;
        x = a[0];
        output[0] = b[0] + t[0] + x + w[0] + *q + holder.Field[0] + Peek(output) + OwnLocal(output);
    }

    [GpuDeviceFunction]
    public static void Shift(ref Span<int> values) => values = values.Slice(1);

    // X13: the stack array of a span is a C++ array as well, so it cannot be reassigned or passed by ref
    [GpuKernel]
    public static void SpanReassignKernel(int[] output)
    {
        Span<int> s = stackalloc int[4];
        s = s.Slice(1);
        Shift(ref s);
        output[0] = s.Length;
    }

    // X16: a collection expression created in an argument, kept by a local function in a captured variable
    [GpuKernel]
    public static void ArgumentStorageLocalFunctionKernel(int[] output, int[] seed)
    {
        int[] outer = seed;

        void Put(int[] v)
        {
            outer = v;
        }

        for (int it = 0; it < 2; it++)
        {
            if (it == 0) Put([11 * (it + 1), 0]);
        }

        output[0] = outer[0];
    }

    // X17: a collection expression created in an argument, kept through a ref argument that outlives it
    [GpuKernel]
    public static void ArgumentStorageRefKernel(int[] output, int[] seed)
    {
        int[] outer = seed;
        for (int it = 0; it < 2; it++)
        {
            if (it == 0) KeepIn([11, 0], ref outer);
        }

        output[0] = outer[0];
    }

    // X18: a pointer stackalloc of an inner block (C# keeps it until the method returns)
    [GpuKernel]
    public static void PointerStackAllocKernel(int[] output)
    {
        int* p = null;
        {
            p = stackalloc int[4];
        }

        output[0] = p[0];
    }
}
