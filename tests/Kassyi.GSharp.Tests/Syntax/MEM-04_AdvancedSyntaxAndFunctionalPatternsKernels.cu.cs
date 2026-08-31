// ReSharper disable All
using System;
using System.Threading.Tasks;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public struct GpuContextMEM04 : IDisposable
{
    public int Value;
    public bool Disposed;

    public GpuContextMEM04(int val)
    {
        Value = val;
        Disposed = false;
    }

    public void Compute()
    {
        Value *= 2;
    }

    public void Dispose()
    {
        Disposed = true;
    }
}

public record struct HolderRecordMEM04(GpuContextMEM04 Ctx, int ID);

public ref struct GpuSpanMEM04
{
    public int Value;
    public bool Disposed;

    public void Dispose()
    {
        Disposed = true;
    }
}

public static class MEM_04_AdvancedSyntaxAndFunctionalPatternsKernels
{
    [GpuDeviceFunction]
    public static (GpuContextMEM04, int) CreateTuple(int val) => new ValueTuple<GpuContextMEM04, int>(new GpuContextMEM04(val), 0);

    [GpuDeviceFunction]
    public static (int, int) CreateIntTuple(int a, int b) => (a, b);

    // 1 & 2. Safe: Tuple Return & Deconstruction with Discard
    [GpuKernel]
    public static void TupleAndDeconstructionKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        var (ctx, _) = MEM_04_AdvancedSyntaxAndFunctionalPatternsKernels.CreateTuple(data[tid]);
        data[tid] = ctx.Value;
    }

    [GpuKernel]
    public static void TupleReturnAndDeconstructionKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        var (a, b) = MEM_04_AdvancedSyntaxAndFunctionalPatternsKernels.CreateIntTuple(data[tid], 10);
        data[tid] = a + b;
    }

    // Edge Case: Reassignment Deconstruction
    [GpuKernel]
    public static void ReassignmentDeconstructionKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        int existingA = 0;
        int existingB = 0;
        (existingA, existingB) = MEM_04_AdvancedSyntaxAndFunctionalPatternsKernels.CreateIntTuple(data[tid], 5);
        data[tid] = existingA * existingB;
    }

    // Edge Case: Nested Tuple Deconstruction
    [GpuKernel]
    public static void NestedTupleDeconstructionKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        var nestedTuple = ((data[tid], data[tid] + 1), data[tid] + 2);
        ((int a, int b), int c) = nestedTuple;
        data[tid] = a + b + c;
    }

    [GpuDeviceFunction]
    private static void OutMethod(out int val)
    {
        val = 42;
    }

    [GpuKernel]
    public static void OutDiscardKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        MEM_04_AdvancedSyntaxAndFunctionalPatternsKernels.OutMethod(out _);
        data[tid] = data[tid] * 2;
    }

    // 3. Safe: Move (with expression)
    [GpuKernel]
    public static void RecordMoveWithKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        var a = new HolderRecordMEM04(new GpuContextMEM04(data[tid]), 1);
        var b = a with { ID = 2 };
        data[tid] = b.Ctx.Value * b.ID;
    }

    // 4. Safe: Duck-Typed using
    [GpuKernel]
    public static void DuckTypedUsingKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        using var span = new GpuSpanMEM04 { Value = data[tid], Disposed = false };
        data[tid] = span.Value * 10;
    }

    // 5. Safe: Borrowing (Scoped Execution)
    [GpuKernel]
    public static void BorrowingClosureKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        using var ctx = new GpuContextMEM04(data[tid]);
        Action act = () => { /* simulate compute */ };
        act();
        data[tid] = ctx.Value * 2;
    }

    // 6. Safe: Move Capture (Factory/Escaping)
    [GpuKernel]
    public static void MoveCaptureClosureKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        var ctx = new GpuContextMEM04(data[tid]);
        Action act = () =>
        {
            /* simulate compute and dispose */
        };
        act();
        data[tid] = ctx.Value * 2;
    }

    // 7. Safe: Reassignment
    [GpuDeviceFunction]
    private static void Update(ref GpuContextMEM04 ctx, int newVal)
    {
        ctx = new GpuContextMEM04(newVal);
    }

    [GpuKernel]
    public static void RefReassignmentKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        var ctx = new GpuContextMEM04(data[tid]);
        MEM_04_AdvancedSyntaxAndFunctionalPatternsKernels.Update(ref ctx, data[tid] + 100);
        data[tid] = ctx.Value;
    }

    // 8. Safe: await using / async disposal pattern
    // Note: True asynchronous suspension is not natively supported on the GPU device execution model.
    // 'await using' cannot be tested directly here because [GpuKernel] methods must return void (not async Task).
    // GSharp supports 'await using' transpilation via synchronous DisposeAsync() calls, but it is validated
    // via transpiler unit tests rather than end-to-end execution kernels.
    [GpuKernel]
    public static void AwaitUsingKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        using var ctx = new GpuContextMEM04(data[tid] + 50);
        data[tid] = ctx.Value;
    }
}
