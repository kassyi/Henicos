namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>The partial class that the generated host members of a kernel or device function go into.</summary>
/// <param name="HintName">The source hint name of the class's wrapper (<see cref="GlobalCppNames.GetHintName"/>).</param>
/// <param name="Namespace">
/// The namespace as C# source spells it (<c>@event.P3</c>), or empty for the global namespace.
/// </param>
/// <param name="Name">
/// The class name and type parameters as C# source spells them (<c>@class</c>, <c>G&lt;T&gt;</c>).
/// </param>
/// <param name="Keyword">
/// The keyword of the user's declaration after <c>partial</c> (<c>class</c>, or <c>record</c> for a record class),
/// which every part must repeat (CS0261).
/// </param>
/// <param name="Accessibility">The declared accessibility keywords, which the generated part must repeat (CS0262).</param>
/// <param name="IsPublic">Whether the class is public, which decides the CS0649 suppression.</param>
/// <param name="OuterDeclarations">
/// The partial declarations of the types that contain the class, outermost first (<c>partial class Outer</c>, names
/// as <see cref="CSharpTypeNameWriter.GetDeclaredTypeName"/> spells them), so
/// that a part of a nested class is written inside them.
/// </param>
/// <param name="FullyQualifiedName">The <c>global::</c>-qualified C# type name, including any enclosing types.</param>
/// <param name="WarmupRelayHosts">
/// The <c>global::</c>-qualified names of the containing types that relay the warmups of the class's kernels, outermost
/// first, or empty when the module context can name the class (<see cref="CreateWarmupRelayHosts"/>, #1282). Only a
/// kernel class has them (<see cref="CreateForKernels"/>).
/// </param>
/// <param name="DefinedSymbols">
/// The symbols that the generated files of the class <c>#define</c>, so that C# keeps there the
/// <c>[Conditional]</c> calls that a <c>#define</c> of the user's file keeps
/// (<see cref="RemovedCalls.GetSymbolsToDefine"/>).
/// </param>
internal readonly record struct HostClassHeader(
    string HintName,
    string Namespace,
    string Name,
    string Keyword,
    string Accessibility,
    bool IsPublic,
    EquatableArray<string> OuterDeclarations,
    string FullyQualifiedName,
    EquatableArray<string> DefinedSymbols = default,
    EquatableArray<string> WarmupRelayHosts = default)
{
    public bool IsGlobalNamespace => Namespace.Length == 0;

    public static HostClassHeader Create(INamedTypeSymbol type, Compilation compilation) => new(
        GlobalCppNames.GetHintName(type),
        type.ContainingNamespace.GetCsharpNamespaceName(),
        type.GetDeclaredTypeName(),
        GetKeyword(type),
        SyntaxFacts.GetText(type.DeclaredAccessibility),
        type.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public,
        CreateOuterDeclarations(type),
        type.GetFullyQualifiedCsharpName(),
        RemovedCalls.GetSymbolsToDefine(type, compilation));

    /// <summary>
    /// <see cref="Create"/> for the class of a kernel, with the containers that relay its warmups
    /// (<see cref="CreateWarmupRelayHosts"/>).
    /// </summary>
    public static HostClassHeader CreateForKernels(INamedTypeSymbol type, Compilation compilation) =>
        Create(type, compilation) with { WarmupRelayHosts = CreateWarmupRelayHosts(type, compilation) };

    /// <summary>
    /// The containing types of <paramref name="type"/> that relay its warmups, outermost first: the innermost container
    /// that code anywhere in the assembly can name, and each container inside it. Empty when the assembly can name
    /// <paramref name="type"/> itself.
    /// </summary>
    /// <remarks>
    /// The module context, a class of its own namespace, cannot name a private or protected nested class. Each relay
    /// host declares an internal method that calls the same method of the next host, and the last one calls the
    /// warmup method of the class; a nested type is accessible within its container whatever its accessibility.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown for a class in or under a file-local type, which the module context in another file can never name:
    /// <see cref="KernelDeclaration.IsLaunchable"/> rejects a kernel there (a member of a file-local type), so its class
    /// never gets here.
    /// </exception>
    public static EquatableArray<string> CreateWarmupRelayHosts(INamedTypeSymbol type, Compilation compilation)
    {
        for (INamedTypeSymbol? current = type; current != null; current = current.ContainingType)
        {
            if (current.IsFileLocal)
            {
                throw new InvalidOperationException(
                    $"The kernel class '{type.GetFullyQualifiedCsharpName()}' is in a file-local type, which the " +
                    "module context cannot name; such a kernel is not launchable and must not reach the warmups.");
            }
        }

        if (compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
        {
            return default;
        }

        var hosts = ImmutableArray.CreateBuilder<string>();
        for (var outer = type.ContainingType; outer != null; outer = outer.ContainingType)
        {
            hosts.Insert(0, outer.GetFullyQualifiedCsharpName());
            if (compilation.IsSymbolAccessibleWithin(outer, compilation.Assembly))
            {
                return hosts.ToImmutable().AsEquatableArray();
            }
        }

        // A top-level type that is not file-local is accessible within its own assembly
        throw new InvalidOperationException(
            $"No type around the kernel class '{type.GetFullyQualifiedCsharpName()}' is accessible from its assembly.");
    }

    /// <summary>
    /// The partial declarations of the types that contain <paramref name="type"/>, outermost first
    /// (<c>partial class Outer&lt;T&gt;</c>), or empty for a type that is not nested.
    /// </summary>
    public static EquatableArray<string> CreateOuterDeclarations(INamedTypeSymbol type)
    {
        if (type.ContainingType == null)
        {
            return default;
        }

        var declarations = ImmutableArray.CreateBuilder<string>();
        for (var outer = type.ContainingType; outer != null; outer = outer.ContainingType)
        {
            declarations.Insert(0, $"partial {GetKeyword(outer)} {outer.GetDeclaredTypeName()}");
        }

        return declarations.ToImmutable().AsEquatableArray();
    }

    /// <summary>
    /// The keyword that declares <paramref name="type"/> after <c>partial</c>: <c>class</c>, <c>struct</c>,
    /// <c>interface</c>, <c>record</c> or <c>record struct</c>.
    /// </summary>
    public static string GetKeyword(INamedTypeSymbol type) => type.IsRecord
        ? type.TypeKind == TypeKind.Struct ? "record struct" : "record"
        : type.TypeKind == TypeKind.Struct ? "struct" : type.TypeKind == TypeKind.Interface ? "interface" : "class";
}
