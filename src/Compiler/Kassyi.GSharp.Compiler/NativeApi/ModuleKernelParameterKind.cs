namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>How a module (kernel) launcher passes a parameter to the kernel.</summary>
internal enum ModuleKernelParameterKind
{
    /// <summary>An unmanaged value, passed by the address of a local copy.</summary>
    Value,

    /// <summary>A pointer or function pointer, passed by the address of a local copy.</summary>
    Pointer,

    /// <summary>
    /// A one-dimensional array of unmanaged elements, staged through a device buffer and passed as its device pointer;
    /// null is passed as a null pointer when the array is declared nullable, and rejected otherwise.
    /// </summary>
    Array,

    /// <summary>
    /// A multidimensional array of unmanaged elements, staged through one device buffer in row-major order (the order
    /// in which C# stores the elements) and passed as its device pointer; null is handled as for
    /// <see cref="Array"/>.
    /// </summary>
    MultidimensionalArray,

    /// <summary>
    /// A <c>ref</c>, <c>out</c> or <c>in</c> unmanaged value, staged through a one-element device buffer and passed as
    /// its device pointer.
    /// </summary>
    ByReference,
}
