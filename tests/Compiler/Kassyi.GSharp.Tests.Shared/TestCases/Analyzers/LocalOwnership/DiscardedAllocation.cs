// ReSharper disable All
using System;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers.LocalOwnership;

public class OwnedResource : IDisposable
{
    public void Compute() { }
    public void Dispose() { }
}

public struct OwnedStruct : IDisposable
{
    public void Dispose() { }
}

public class NotDisposable
{
}

public class DiscardedAllocation
{
    public void DiscardedNew()
    {
        new OwnedResource();
    }

    public void DiscardedTargetTypedNew()
    {
        _ = new OwnedResource();
    }

    public void DiscardedStruct()
    {
        new OwnedStruct();
    }

    public void NotDisposableIsIgnored()
    {
        new NotDisposable();
    }
}
