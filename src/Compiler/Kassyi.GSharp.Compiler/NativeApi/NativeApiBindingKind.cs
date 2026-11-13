namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// Describes how the methods of a <see cref="NativeApiDefinition"/> are bound to native code.
/// </summary>
internal enum NativeApiBindingKind
{
    /// <summary>GPU intrinsics transpiled through <c>[CudaIntrinsic]</c> (built-in definitions only).</summary>
    Intrinsic,

    /// <summary>Host shared library functions bound with <c>[LibraryImport]</c> (<c>libraryName</c>).</summary>
    Library,

    /// <summary>Kernels loaded from a fatbin / cubin / PTX module and launched via the driver API (<c>moduleName</c>).</summary>
    Module
}
