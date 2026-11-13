namespace Kassyi.GSharp.Compiler.NativeApi;

internal sealed class ApiMethodDefinition
{
    /// <summary>The CPU dialect in <see cref="UnsupportedDialects"/>, as <c>GSharpTargetDialect</c> names it.</summary>
    public const string CpuDialect = "Cpu";

    /// <summary>The CUDA dialect in <see cref="UnsupportedDialects"/>, as <c>GSharpTargetDialect</c> names it.
    /// </summary>
    public const string CudaDialect = "Cuda";

    public string Name { get; set; } = string.Empty;

    public string? NativeName { get; set; }

    /// <summary>
    /// Gets or sets the class that declares the method, overriding the definition's <c>className</c>. A dotted path
    /// (<c>Gpu.Unsafe</c>) declares the method in a nested static class; the outer classes are emitted as
    /// <c>partial</c> wrappers.
    /// </summary>
    public string? ClassName { get; set; }

    /// <summary>
    /// Gets or sets the native symbol name: the exported function for <see cref="NativeApiBindingKind.Library"/>
    /// or the kernel function for <see cref="NativeApiBindingKind.Module"/>. Defaults to <see cref="Name"/>.
    /// </summary>
    public string? EntryPoint { get; set; }

    public string? StringMarshalling { get; set; }

    /// <summary>Gets or sets whether the method is declared <c>unsafe</c> (it takes or returns a pointer).</summary>
    public bool IsUnsafe { get; set; }

    /// <summary>
    /// Gets or sets the member of <c>Kassyi.GSharp.Core</c> that runs an intrinsic on the managed CPU path
    /// (<c>managed: ManagedAtomics.Add</c>). The stub forwards its arguments to it instead of throwing; the member throws
    /// <c>GpuOnlyException</c> itself outside a managed launch. <see langword="null"/> for an intrinsic that only throws.
    /// </summary>
    public string? ManagedName { get; set; }

    /// <summary>
    /// Gets or sets the dialects that do not implement an intrinsic (<c>unsupported: [Cpu]</c>): <c>Cpu</c> or
    /// <c>Cuda</c>, as <c>GSharpTargetDialect</c> names them. A call in a project of such a dialect is reported
    /// (GSH0054) and replaced with a runtime failure, so the dialect's prelude does not define the native name.
    /// </summary>
    public ImmutableArray<string> UnsupportedDialects { get; set; } = ImmutableArray<string>.Empty;

    public string? Summary { get; set; }

    /// <summary>
    /// Gets or sets the whole XML documentation of the method (<c>xmlDoc</c>), written as is after <c>///</c>, for text
    /// that needs markup (<c>&lt;c&gt;</c>, <c>&lt;see cref&gt;</c>, <c>&lt;exception&gt;</c>). It replaces the
    /// plain-text <see cref="Summary"/> and descriptions, which are escaped.
    /// </summary>
    public string? XmlDoc { get; set; }

    /// <summary>
    /// Gets or sets the overloads to expand: one non-generic method per entry of <see cref="TypeTemplateDefinition.Types"/>,
    /// with the placeholder replaced in every type.
    /// </summary>
    public TypeTemplateDefinition? TypeTemplate { get; set; }

    /// <summary>
    /// Gets or sets the type parameters of a generic method (<c>typeParameters</c>, intrinsic definitions only). Unlike
    /// <see cref="TypeTemplate"/>, they produce a single generic method, called in C++ with explicit template arguments.
    /// </summary>
    public ImmutableArray<TypeParameterDefinition> TypeParameters { get; set; } =
        ImmutableArray<TypeParameterDefinition>.Empty;

    public string ReturnType { get; set; } = "void";

    public string? ReturnDescription { get; set; }

    public ImmutableArray<ParameterDefinition> Parameters { get; set; } = ImmutableArray<ParameterDefinition>.Empty;
}
