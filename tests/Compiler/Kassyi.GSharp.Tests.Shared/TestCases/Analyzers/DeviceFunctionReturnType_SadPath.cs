using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public static class DeviceFunctionReturnType_SadPath
{
    // A device function whose return type is not supported
    [GpuDeviceFunction]
    public static string ManagedResult(int x) => "";
}
