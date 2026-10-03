// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

// Dummy GpuWorkspace for Roslyn AST & SemanticModel analysis
public class GpuWorkspace : IGpuResourceScope
{
    public void Track(IDisposable resource) {}
    public IGpuResourceScope CreateScope() => throw new NotImplementedException();
    public void Compute() { }
    public void Dispose() { }
}

public class MyWrapper
{
    // 11.3.2 Implicit Cast Operator allocating disposable resource
    public static implicit operator GpuWorkspace(MyWrapper w) => new GpuWorkspace();
}

// 11.3.1 Missing IDisposable on Class
public class ClassMissingIDisposable
{
    private GpuWorkspace _c = new GpuWorkspace(); // Error: GSH0020
}

// 11.3.1 Undisposed Field in Dispose()
public class ClassUndisposedField : IDisposable
{
    private GpuWorkspace _c = new GpuWorkspace();

    public void Dispose()
    {
        // Error: GSH0021 - AST missing _c.Dispose()
    }
}

// 11.3.1 Safe: Clean Chained Disposal
public class ClassCleanChainedDisposal : IDisposable
{
    private GpuWorkspace _c = new GpuWorkspace();

    public void Dispose()
    {
        _c.Dispose(); // Safe chained disposal
    }
}

// 11.3.1 Unsafe Field Overwrite
public class ClassUnsafeFieldOverwrite : IDisposable
{
    private GpuWorkspace _c = new GpuWorkspace();

    public void Update()
    {
        _c = new GpuWorkspace(); // Error: GSH0022
    }

    public void Dispose()
    {
        _c.Dispose();
    }
}

// 11.3.2 Property Getter Side-Effects
public class ClassPropertyGetterSideEffect
{
    public GpuWorkspace Context => new GpuWorkspace(); // Error: GSH0023

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
        GpuWorkspace ctx = wrapper; // Error: GSH0024
    }
}

// 11.3.3 Unguarded Multi-Allocation
public class ClassUnguardedMultiAllocation
{
    private GpuWorkspace _c1;
    private GpuWorkspace _c2;

    public ClassUnguardedMultiAllocation()
    {
        _c1 = new GpuWorkspace();
        _c2 = new GpuWorkspace(); // Error: GSH0025
    }
}

// 11.3.3 Safe: Factory Pattern
public class ClassFactoryPattern : IDisposable
{
    private readonly GpuWorkspace _c1;
    private readonly GpuWorkspace _c2;

    public ClassFactoryPattern(GpuWorkspace c1, GpuWorkspace c2)
    {
        _c1 = c1;
        _c2 = c2;
    }

    public static ClassFactoryPattern Create()
    {
        var c1 = new GpuWorkspace();
        try
        {
            var c2 = new GpuWorkspace();
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
    public static readonly GpuWorkspace GlobalCtx = new GpuWorkspace(); // Error: GSH0026
}

// 11.3.4 Standard Injection (Borrowing)
public class BorrowedResourceClass : IDisposable
{
    private GpuWorkspace _c;

    public BorrowedResourceClass(GpuWorkspace c)
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
    private GpuWorkspace _c;

    public OwnedResourceClass([TakesOwnership] GpuWorkspace c)
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
    void Process(GpuWorkspace ctx)
    {
        ctx.Dispose(); // Error: GSH0028
    }
}

// Issue 256: IDisposable struct wrapper tests (No IGpuResourceScope, No [GpuResource])
public readonly struct DeviceResource : IDisposable
{
    public void Dispose() { }
}

// Expected: GSH0020 (Missing IDisposable implementation)
public readonly struct ResourceWrapperPrimary([TakesOwnership] DeviceResource resource)
{
    private readonly DeviceResource _resource = resource;
}

// Expected: GSH0020 (Missing IDisposable implementation)
public class ResourceWrapperStandard
{
    private readonly DeviceResource _resource;

    public ResourceWrapperStandard([TakesOwnership] DeviceResource resource)
    {
        _resource = resource;
    }
}
