// ReSharper disable All
using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public class GpuContextSad : IDisposable, IAsyncDisposable
{
    public void Compute() { }
    public void Dispose() { }
    public ValueTask DisposeAsync() => default;
}

public record HolderSad(GpuContextSad Ctx, int ID);

public class ResourceManagerSad
{
    public void Deconstruct(out GpuContextSad a, out GpuContextSad b)
    {
        a = new GpuContextSad();
        b = new GpuContextSad();
    }
}

public ref struct GpuSpanSad
{
    public void Dispose() { }
}

public static class GpuSad
{
    public static Task<GpuContextSad> CreateAsync() => Task.FromResult(new GpuContextSad());
}

public class MEM_04_AdvancedSyntaxAndFunctionalPatternsSadPath
{
    public (GpuContextSad, int) CreateTuple() => (new GpuContextSad(), 0);

    // 1. ❌ Banned: Undisposed Deconstruction
    [GpuKernel]
    public void Test01_UndisposedDeconstruction()
    {
        var (ctx, status) = CreateTuple();
        // Error: Missing ctx.Dispose()
    }

    // 2. ❌ Banned: Tuple Field Leak
    [GpuKernel]
    public void Test02_TupleFieldLeak()
    {
        var tuple = CreateTuple();
        // Error: Missing tuple.Item1.Dispose()
    }

    // 3. ❌ Banned: Access After Move
    [GpuKernel]
    public void Test03_AccessAfterMove()
    {
        var a = new HolderSad(new GpuContextSad(), 1);
        var b = a with { };
        a.Ctx.Dispose(); // Error: Access to moved resource
    }

    // 4. ❌ Banned: Double Dispose
    [GpuKernel]
    public void Test04_DoubleDispose()
    {
        var a = new HolderSad(new GpuContextSad(), 1);
        var b = a with { };
        a.Ctx.Dispose();
        b.Ctx.Dispose(); // Error: Double dispose
    }

    // 5. ❌ Banned: Deconstruct Ownership Splitting
    [GpuKernel]
    public void Test05_DeconstructOwnershipSplitting(ResourceManagerSad resourceManager)
    {
        var (ctx1, ctx2) = resourceManager; // Error: Deconstruction of owned resources is banned
    }

    // 6. ❌ Banned: Undisposed ref struct
    [GpuKernel]
    public void Test06_UndisposedRefStruct()
    {
        var span = new GpuSpanSad();
        // Error: Missing span.Dispose()
    }

    // 7. ❌ Banned: Escaping Borrowed Resource
    [GpuKernel]
    public Action Test07_EscapingBorrowedResource()
    {
        using var ctx = new GpuContextSad();
        return () => ctx.Compute(); // Error: Delegate escapes with borrowed resource
    }

    // 8. ❌ Banned: Undisposed Move Capture
    [GpuKernel]
    public Action Test08_UndisposedMoveCapture()
    {
        var ctx = new GpuContextSad();
        return () => ctx.Compute(); // Error: Captured resource never disposed
    }

    // 9. ❌ Banned: Local Functions with Implicit Capture
    [GpuKernel]
    public void Test09_LocalFunctionImplicitCapture(bool cond)
    {
        var ctx = new GpuContextSad();
        void LocalDispose() { ctx.Dispose(); } // Error: Local functions cannot capture owned resources
        if (cond) LocalDispose();
    }

    // 10. ❌ Banned: Multicast Delegate Double-Dispose (+=)
    [GpuKernel]
    public void Test10_MulticastDelegateDoubleDispose(GpuContextSad ctx)
    {
        Action act = () => ctx.Dispose();
        act += () => ctx.Dispose();
        act(); // Error: Structural double-dispose via multicast delegate
    }

    // 11. ❌ Banned: Expression Trees (Expression<T>)
    [GpuKernel]
    public void Test11_ExpressionTrees(GpuContextSad ctx)
    {
        Expression<Action> expr = () => ctx.Dispose(); // Error: Expression trees cannot capture resources
    }

    // 12. ❌ Banned: Leaking Reassignment
    [GpuKernel]
    public void Test12_LeakingReassignment(ref GpuContextSad ctx)
    {
        ctx = new GpuContextSad(); // Error: Reassigned without disposal
    }

    // 13. ❌ Banned: The in / ref readonly Mutation Trap
    [GpuKernel]
    public void Test13_InRefReadonlyMutationTrap(in GpuContextSad ctx)
    {
        ctx.Dispose(); // Error: Cannot mutate/dispose a readonly borrowed resource
    }

    // 14. ❌ Banned: Sync Dispose on Async Resource
    [GpuKernel]
    public async Task Test14_SyncDisposeOnAsyncResource()
    {
        var ctx = await GpuSad.CreateAsync();
        ctx.Dispose(); // Error: Should use await using
    }
}
