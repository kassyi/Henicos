namespace Kassyi.GSharp.Compiler.NativeApi;

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

    /// <summary>
    /// Gets or sets the documentation of the classes the methods are declared in (<c>classes</c>, intrinsic definitions
    /// only), for a nested class that has no hand-written declaration of its own.
    /// </summary>
    public ImmutableArray<ClassDocumentationDefinition> Classes { get; set; } =
        ImmutableArray<ClassDocumentationDefinition>.Empty;

    /// <summary>The line on which <c>className</c> is declared, for diagnostics.</summary>
    public int ClassNameLine { get; set; } = 1;

    public NativeApiBindingKind BindingKind =>
        !string.IsNullOrEmpty(LibraryName) ? NativeApiBindingKind.Library
        : !string.IsNullOrEmpty(ModuleName) ? NativeApiBindingKind.Module
        : NativeApiBindingKind.Intrinsic;
}
