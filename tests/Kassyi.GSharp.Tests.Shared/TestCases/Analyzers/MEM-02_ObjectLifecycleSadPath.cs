// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

// Dummy GpuContext for Roslyn AST & SemanticModel analysis
public class GpuContext : IDisposable
{
    public void Compute() { }
    public void Dispose() { }
}

public class MyWrapper
{
    // 11.3.2 Implicit Cast Operator allocating disposable resource
    public static implicit operator GpuContext(MyWrapper w) => new GpuContext();
}

// 11.3.1 Missing IDisposable on Class
public class ClassMissingIDisposable
{
    private GpuContext _c = new GpuContext(); // Error: GSH0020
}

// 11.3.1 Undisposed Field in Dispose()
public class ClassUndisposedField : IDisposable
{
    private GpuContext _c = new GpuContext();

    public void Dispose()
    {
        // Error: GSH0021 - AST missing _c.Dispose()
    }
}

// 11.3.1 Safe: Clean Chained Disposal
public class ClassCleanChainedDisposal : IDisposable
{
    private GpuContext _c = new GpuContext();

    public void Dispose()
    {
        _c.Dispose(); // Safe chained disposal
    }
}

// 11.3.1 Unsafe Field Overwrite
public class ClassUnsafeFieldOverwrite : IDisposable
{
    private GpuContext _c = new GpuContext();

    public void Update()
    {
        _c = new GpuContext(); // Error: GSH0022
    }

    public void Dispose()
    {
        _c.Dispose();
    }
}

// 11.3.2 Property Getter Side-Effects
public class ClassPropertyGetterSideEffect
{
    public GpuContext Context => new GpuContext(); // Error: GSH0023

    public void Execute()
    {
        Context.Compute(); // Leak! Discarded allocation via property getter
    }
}

// 11.3.2 Implicit Cast Operator Invocation
public class ClassImplicitCastInvocation
{
    public void TestImplicitCast(MyWrapper wrapper)
    {
        GpuContext ctx = wrapper; // Error: GSH0024
    }
}

// 11.3.3 Unguarded Multi-Allocation
public class ClassUnguardedMultiAllocation
{
    private GpuContext _c1;
    private GpuContext _c2;

    public ClassUnguardedMultiAllocation()
    {
        _c1 = new GpuContext();
        _c2 = new GpuContext(); // Error: GSH0025
    }
}

// 11.3.3 Safe: Factory Pattern
public class ClassFactoryPattern : IDisposable
{
    private readonly GpuContext _c1;
    private readonly GpuContext _c2;

    public ClassFactoryPattern(GpuContext c1, GpuContext c2)
    {
        _c1 = c1;
        _c2 = c2;
    }

    public static ClassFactoryPattern Create()
    {
        var c1 = new GpuContext();
        try
        {
            var c2 = new GpuContext();
            return new ClassFactoryPattern(c1, c2);
        }
        catch
        {
            c1.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _c1.Dispose();
        _c2.Dispose();
    }
}

// 11.3.3 Static Lifecycle
public static class GpuCache
{
    public static readonly GpuContext GlobalCtx = new GpuContext(); // Error: GSH0026
}

// 11.3.4 Standard Injection (Borrowing)
public class BorrowedResourceClass : IDisposable
{
    private GpuContext _c;

    public BorrowedResourceClass(GpuContext c)
    {
        _c = c; // Borrowed
    }

    public void Dispose()
    {
        _c.Dispose(); // Error: GSH0027
    }
}

// 11.3.4 Explicit Ownership Transfer
public class OwnedResourceClass : IDisposable
{
    private GpuContext _c;

    public OwnedResourceClass([TakesOwnership] GpuContext c)
    {
        _c = c; // Owned
    }

    public void Dispose()
    {
        _c.Dispose(); // Required and safe because ownership was transferred
    }
}

// 11.3.5 Default Interface Methods (DIM)
public interface IResourceBlackbox
{
    void Process(GpuContext ctx)
    {
        ctx.Dispose(); // Error: GSH0028
    }
}
