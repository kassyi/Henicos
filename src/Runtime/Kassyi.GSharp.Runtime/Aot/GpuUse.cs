namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>Where a piece of work that may use the GPU runs.</summary>
internal enum GpuUse
{
    /// <summary>On the GPU.</summary>
    Gpu,

    /// <summary>On the CPU (forced, or the driver fell back).</summary>
    Cpu,

    /// <summary>Nowhere: the driver is too old, and the caller throws.</summary>
    Unsupported,
}
