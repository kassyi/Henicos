namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>Represents the capture status of a CUDA stream.</summary>
public enum CUstreamCaptureStatus
{
    None = 0,
    Active = 1,
    Invalidated = 2
}
