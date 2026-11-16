using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

public partial class DeviceDynamicSharedMemoryTests
{
    // CUDA traps when a region that is not the last one grows; the native CPU path throws (#544, C10)
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void GrowingHelper_AfterCallerDeclarationNative_ThrowsOnCpuAndTrapsOnCuda()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.GrowingHelperAfterCallerDeclarationThrows(s_runNative);
    }
}
