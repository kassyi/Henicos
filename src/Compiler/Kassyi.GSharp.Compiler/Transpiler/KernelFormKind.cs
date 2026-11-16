namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Identifies which kernel form is actively being emitted during CUDA AST transpilation.
/// </summary>
internal enum KernelFormKind
{
    /// <summary>
    /// The standard form where all parameters (including in parameters) are passed and accessed by value.
    /// </summary>
    ByValue = 0,

    /// <summary>
    /// The by-address form where candidate in parameters are passed as pointers (const T*) and dereferenced.
    /// </summary>
    ByAddress = 1
}
