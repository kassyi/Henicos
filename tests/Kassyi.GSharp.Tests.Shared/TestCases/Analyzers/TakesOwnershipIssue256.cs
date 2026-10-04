using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

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
