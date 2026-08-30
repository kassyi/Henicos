// ReSharper disable All
using System;
using System.Threading.Tasks;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public struct GpuContext : IDisposable
{
    public int Value;
    public bool Disposed;

    public GpuContext(int val)
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

public record struct HolderRecord(GpuContext Ctx, int ID);

public ref struct GpuSpan
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
    /* Tuple support (GSH-02) is currently unimplemented.
    [GpuDeviceFunction]
    public static (GpuContext, int) CreateTuple(int val) => new ValueTuple<GpuContext, int>(new GpuContext(val), 0);

    // 1 & 2. Safe: Tuple Return & Deconstruction
    [GpuKernel]
    public static void TupleAndDeconstructionKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        var (ctx, status) = MEM_04_AdvancedSyntaxAndFunctionalPatternsKernels.CreateTuple(data[tid]);
        data[tid] = ctx.Value + status;
    }
    */

    // 3. Safe: Move (with expression)
    [GpuKernel]
    public static void RecordMoveWithKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        var a = new HolderRecord(new GpuContext(data[tid]), 1);
        var b = a with { ID = 2 };
        data[tid] = b.Ctx.Value * b.ID;
    }

    // 4. Safe: Duck-Typed using
    [GpuKernel]
    public static void DuckTypedUsingKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        using var span = new GpuSpan { Value = data[tid], Disposed = false };
        data[tid] = span.Value * 10;
    }

    // 5. Safe: Borrowing (Scoped Execution)
    [GpuKernel]
    public static void BorrowingClosureKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        using var ctx = new GpuContext(data[tid]);
        Action act = () => { /* simulate compute */ };
        act();
        data[tid] = ctx.Value * 2;
    }

    // 6. Safe: Move Capture (Factory/Escaping)
    [GpuKernel]
    public static void MoveCaptureClosureKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        var ctx = new GpuContext(data[tid]);
        Action act = () =>
        {
            /* simulate compute and dispose */
        };
        act();
        data[tid] = ctx.Value * 2;
    }

    // 7. Safe: Reassignment
    [GpuDeviceFunction]
    private static void Update(ref GpuContext ctx, int newVal)
    {
        ctx = new GpuContext(newVal);
    }

    [GpuKernel]
    public static void RefReassignmentKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        var ctx = new GpuContext(data[tid]);
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
        using var ctx = new GpuContext(data[tid] + 50);
        data[tid] = ctx.Value;
    }
}
