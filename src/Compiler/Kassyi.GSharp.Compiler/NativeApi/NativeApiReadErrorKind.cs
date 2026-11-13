namespace Kassyi.GSharp.Compiler.NativeApi;

internal enum NativeApiReadErrorKind
{
    /// <summary>The text is not valid YAML within the supported subset.</summary>
    Syntax,

    /// <summary>The YAML is well-formed but violates the native API definition schema.</summary>
    Schema
}
