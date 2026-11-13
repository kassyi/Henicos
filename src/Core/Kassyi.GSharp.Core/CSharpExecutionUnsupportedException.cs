namespace Kassyi.GSharp.Core;

/// <summary>
/// The exception that is thrown when a kernel launched under <c>KernelExecution.CSharp</c> cannot run one GPU thread at
/// a time: it reaches an operation that waits for or reads the other threads of its block or grid (a barrier, a warp
/// operation, shared memory), or code whose meaning only the code G# generates gives (an operator macro).
/// </summary>
/// <remarks>
/// The launcher throws it before any thread runs when the compiler finds such an operation in the kernel; an operation
/// the compiler cannot see, such as one reached through a delegate, throws it when a thread reaches it. Launch the
/// kernel under <c>KernelExecution.Default</c> or <c>KernelExecution.Managed</c> instead.
/// </remarks>
/// <param name="message">The message: the kernel or the operation, and why it cannot run as C#.</param>
public sealed class CSharpExecutionUnsupportedException(string message) : InvalidOperationException(message);
