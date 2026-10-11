namespace Kassyi.GSharp.Tests.Infrastructure;

/// <summary>
/// Specifies the target execution environment for the test.
/// </summary>
public enum ExecutionTarget
{
    /// <summary>Tests executed on actual NVIDIA GPU device.</summary>
    GpuExecution,

    /// <summary>Tests executed purely on the host CPU baseline/emulation.</summary>
    CpuExecution
}
