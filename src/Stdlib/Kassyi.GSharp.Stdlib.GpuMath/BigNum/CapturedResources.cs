namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// What a finished capture hands to its graph.
/// </summary>
/// <param name="Buffers">The buffers freed during the capture and not taken again; the graph returns them.</param>
/// <param name="Operands">
/// The handles the capture read that the graph does not own (operands, target); the graph checks that each is still
/// alive before every launch.
/// </param>
internal readonly record struct CapturedResources(GpuBigIntDeviceBuffer[] Buffers, GpuBigIntMemoryHandle[] Operands);
