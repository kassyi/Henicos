using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public struct S
{
    public int V;

    [GpuDeviceFunction]
    public int Twice() => V * 2;
}

public partial class C
{
    [GpuDeviceFunction]
    public int Add(int x) => x + 1;
}
