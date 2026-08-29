// ReSharper disable All
using System;
using System.Threading.Tasks;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public struct GpuContext : IDisposable, IAsyncDisposable
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

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return default;
    }

    public static ValueTask<GpuContext> CreateAsync(int val)
    {
        return new ValueTask<GpuContext>(new GpuContext(val));
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
    public static (GpuContext, int) CreateTuple(int val) => (new GpuContext(val), 0);

    // 1 & 2. Safe: Tuple Return & Deconstruction
    [GpuKernel]
    public static void TupleAndDeconstructionKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        var (ctx, status) = CreateTuple(data[tid]);
        data[tid] = ctx.Value + status;
        ctx.Dispose();
    }

    // 3. Safe: Move (with expression)
    [GpuKernel]
    public static void RecordMoveWithKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        var a = new HolderRecord(new GpuContext(data[tid]), 1);
        var b = a with { ID = 2 };
        data[tid] = b.Ctx.Value * b.ID;
        b.Ctx.Dispose();
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
        Action act = () => { ctx.Compute(); };
        act();
        data[tid] = ctx.Value;
    }

    // 6. Safe: Move Capture (Factory/Escaping)
    [GpuKernel]
    public static void MoveCaptureClosureKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        var ctx = new GpuContext(data[tid]);
        Action act = () =>
        {
            ctx.Compute();
            ctx.Dispose();
        };
        act();
        data[tid] = ctx.Value;
    }

    // 7. Safe: Reassignment
    private static void Update(ref GpuContext ctx, int newVal)
    {
        ctx.Dispose();
        ctx = new GpuContext(newVal);
    }

    [GpuKernel]
    public static void RefReassignmentKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        var ctx = new GpuContext(data[tid]);
        Update(ref ctx, data[tid] + 100);
        data[tid] = ctx.Value;
        ctx.Dispose();
    }

    // 8. Safe: await using / async disposal pattern
    [GpuKernel]
    public static void AwaitUsingKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        var ctx = new GpuContext(data[tid] + 50);
        data[tid] = ctx.Value;
        ctx.Dispose();
    }
}
