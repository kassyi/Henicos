#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Diagnostics;

public static class OversizedSharedMemoryDiagnosticCases
{
    [GpuKernel(AllowOversizedSharedMemory = true)]
    public static void MarkedKernel()
    {
    }

    [GpuKernel(AllowOversizedSharedMemory = false)]
    public static void ExplicitlyUnmarkedKernel()
    {
    }

    [GpuKernel]
    public static void DefaultKernel()
    {
    }
}
