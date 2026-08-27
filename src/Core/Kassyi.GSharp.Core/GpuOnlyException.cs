namespace Kassyi.GSharp.Core;

/// <summary>
/// The exception that is thrown when a GPU-only intrinsic, macro, or device syntax marker is executed on the host CPU.
/// </summary>
/// <remarks>
/// Members decorated with GSharp intrinsic attributes or GPU markers exist to provide C# type-safety, IntelliSense, and Roslyn syntax trees. They are replaced during source generation and cannot be invoked at CPU runtime.
/// </remarks>
public class GpuOnlyException() : InvalidOperationException("This code can only be executed on the GPU.")
{
    /// <summary>
    /// Throws a new instance of <see cref="GpuOnlyException"/>.
    /// </summary>
    /// <exception cref="GpuOnlyException">Always thrown upon invocation.</exception>
    public static void Throw() => throw new GpuOnlyException();

    /// <summary>
    /// Throws a new instance of <see cref="GpuOnlyException"/> with a generic return type placeholder for expressions.
    /// </summary>
    /// <typeparam name="T">The expected return type of the enclosing expression.</typeparam>
    /// <returns>This method never returns normally.</returns>
    /// <exception cref="GpuOnlyException">Always thrown upon invocation.</exception>
    public static T Throw<T>() => throw new GpuOnlyException();
}
