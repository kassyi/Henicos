namespace Kassyi.GSharp.Compiler.Backends;

/// <summary>Which backend families are asked to take the IR route.</summary>
[Flags]
internal enum IrRouteRequest
{
    /// <summary>No IR route requested.</summary>
    None = 0,

    /// <summary>
    /// Native C++ routes (<see cref="IrBodyRoute.NativePlain"/> and <see cref="IrBodyRoute.NativeStack"/>).
    /// </summary>
    Native = 1,

    /// <summary>The CUDA C++ route (<see cref="IrBodyRoute.Cuda"/>).</summary>
    Cuda = 2
}
