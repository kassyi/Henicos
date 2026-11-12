namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>Selects which API calls a stream capture forbids while it is active (<c>CUstreamCaptureMode</c>).</summary>
public enum CUstreamCaptureMode
{
    /// <summary>Calls that are unsafe during a capture fail on every thread while this capture is active.</summary>
    Global = 0,

    /// <summary>Calls that are unsafe during a capture fail only on the thread that began this capture.</summary>
    ThreadLocal = 1,

    /// <summary>No call is forbidden; the caller makes sure that nothing unsafe runs during the capture.</summary>
    Relaxed = 2
}
