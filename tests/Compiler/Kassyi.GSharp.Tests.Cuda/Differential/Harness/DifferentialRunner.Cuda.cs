namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class DifferentialRunner
{
    static partial void EnsureEnvironmentReady() => CudaCapability.EnsureGpuAvailable();
}
