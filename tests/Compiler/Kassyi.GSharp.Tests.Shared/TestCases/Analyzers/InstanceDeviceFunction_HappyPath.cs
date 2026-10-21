using Kassyi.GSharp.Core.Attributes;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public struct S
{
    public int V;

    [GpuDeviceFunction]
    public static int Twice(S s) => s.V * 2;

    public int HostOnly() => V * 2;
}
