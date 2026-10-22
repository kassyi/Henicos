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

/// <summary>
/// A single YAML native API definition file.
/// </summary>
internal sealed class NativeApiDefinition
{
    public string Namespace { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;

    /// <summary>Gets or sets the logical library name or path used in <c>[LibraryImport]</c>.</summary>
    public string? LibraryName { get; set; }

    /// <summary>Gets or sets platform-specific probing candidates for <see cref="LibraryName"/>.</summary>
    public NativeLibraryNames? LibraryNames { get; set; }

    /// <summary>Gets or sets the fatbin / cubin / PTX module path, relative to the application base directory.</summary>
    public string? ModuleName { get; set; }

    public ImmutableArray<ApiMethodDefinition> Methods { get; set; } = ImmutableArray<ApiMethodDefinition>.Empty;

    /// <summary>The line on which <c>className</c> is declared, for diagnostics.</summary>
    public int ClassNameLine { get; set; } = 1;

    public NativeApiBindingKind BindingKind =>
        !string.IsNullOrEmpty(LibraryName) ? NativeApiBindingKind.Library
        : !string.IsNullOrEmpty(ModuleName) ? NativeApiBindingKind.Module
        : NativeApiBindingKind.Intrinsic;
}

/// <summary>
/// Platform-specific library probing candidates. Each entry may be a bare file name, a path relative to
/// the application base directory, or an absolute path, and may contain <c>%VARIABLE%</c> references.
/// </summary>
internal sealed class NativeLibraryNames
{
    public ImmutableArray<string> Windows { get; set; } = ImmutableArray<string>.Empty;

    public ImmutableArray<string> Linux { get; set; } = ImmutableArray<string>.Empty;

    public ImmutableArray<string> OSX { get; set; } = ImmutableArray<string>.Empty;

    public bool IsEmpty => Windows.IsEmpty && Linux.IsEmpty && OSX.IsEmpty;
}

internal sealed class ApiMethodDefinition
{
    public string Name { get; set; } = string.Empty;

    public string? NativeName { get; set; }

    public string? ClassName { get; set; }

    /// <summary>
    /// Gets or sets the native symbol name: the exported function for <see cref="NativeApiBindingKind.Library"/>
    /// or the kernel function for <see cref="NativeApiBindingKind.Module"/>. Defaults to <see cref="Name"/>.
    /// </summary>
    public string? EntryPoint { get; set; }

    public string? StringMarshalling { get; set; }

    public bool IsUnsafe { get; set; }

    public string? Summary { get; set; }

    public TypeTemplateDefinition? TypeTemplate { get; set; }

    public string ReturnType { get; set; } = "void";

    public string? ReturnDescription { get; set; }

    public ImmutableArray<ParameterDefinition> Parameters { get; set; } = ImmutableArray<ParameterDefinition>.Empty;
}

internal sealed class TypeTemplateDefinition
{
    public string Placeholder { get; set; } = "T";

    public ImmutableArray<string> Types { get; set; } = ImmutableArray<string>.Empty;
}

internal sealed class ParameterDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? DefaultValue { get; set; }
}

internal enum NativeApiReadErrorKind
{
    /// <summary>The text is not valid YAML within the supported subset.</summary>
    Syntax,

    /// <summary>The YAML is well-formed but violates the native API definition schema.</summary>
    Schema
}

internal sealed class NativeApiReadError(NativeApiReadErrorKind kind, int line, string message)
{
    public NativeApiReadErrorKind Kind { get; } = kind;

    /// <summary>Gets the 1-based line the error refers to.</summary>
    public int Line { get; } = line;

    public string Message { get; } = message;

    /// <summary>
    /// Gets the diagnostic ID this error is reported under. The build-time generator executable cannot link the
    /// generated <c>GSharpDiagnostics</c>, so the IDs are mirrored here and pinned to <c>diagnostics.json</c> by a test.
    /// </summary>
    public string DiagnosticId => Kind == NativeApiReadErrorKind.Syntax ? "GSH0052" : "GSH0053";
}

internal sealed class NativeApiReadException(NativeApiReadErrorKind kind, int line, string message) : Exception(message)
{
    public NativeApiReadError ToError() => new(kind, line, Message);
}
