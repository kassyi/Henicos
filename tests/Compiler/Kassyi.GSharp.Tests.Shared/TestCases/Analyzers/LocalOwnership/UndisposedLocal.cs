// ReSharper disable All
using System;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers.LocalOwnership;

public class OwnedResource : IDisposable
{
    public void Compute() { }
    public void Dispose() { }
}

public class UndisposedLocal
{
    public void NeverDisposed()
    {
        var ctx = new OwnedResource();
    }

    public void UsedButNeverDisposed()
    {
        var ctx = new OwnedResource();
        ctx.Compute();
    }

    public void TargetTypedNew()
    {
        OwnedResource ctx = new();
        ctx.Compute();
    }

    public void BorrowedByCallee()
    {
        var ctx = new OwnedResource();
        Borrow(ctx);
    }

    private static void Borrow(OwnedResource resource) => resource.Compute();
}
