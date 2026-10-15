namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// Receives a notification when a CUDA stream reaches an armed completion signal of <see cref="CudaStreamCompletionSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> The awaiter of a completion signal may stop waiting (cancellation) while device work it started is still
/// running. Resources that the device writes to, such as pinned staging buffers, must only be released once that work
/// has actually finished, which is exactly when the driver invokes the host function behind the signal.
/// </para>
/// <para>
/// <b>Execution context:</b> <see cref="OnStreamCompleted"/> runs on the CUDA driver's host-function thread, which is
/// shared by every stream of the context. Implementations must not call any CUDA API, must not block, and must not throw.
/// It runs exactly once per armed signal, and never if a context fault prevents the stream from reaching the signal.
/// </para>
/// </remarks>
internal interface IStreamCompletionHook
{
    /// <summary>
    /// Called on the driver's host-function thread after all work enqueued before the completion signal has finished.
    /// </summary>
    void OnStreamCompleted();
}
