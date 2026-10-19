namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

/// <summary>
/// Rejects identifiers declared in GPU code that cannot be emitted verbatim into C++/CUDA, including members of custom
/// structs reachable from a declaration's type.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description>GSH0057 (permanent): identifiers containing <c>__</c>, reserved for G# internals (<c>__gsharp*</c>) and the C++ implementation.</description></item>
///   <item><description>GSH0058 (temporary): C++ keywords. Removed once the transpiler renames them automatically (#369).</description></item>
/// </list>
/// </remarks>
internal sealed class CppReservedIdentifierRule :
    ISyntaxValidationRule<MethodDeclarationSyntax>,
    ISyntaxValidationRule<ParameterSyntax>,
    ISyntaxValidationRule<VariableDeclaratorSyntax>,
    ISyntaxValidationRule<SingleVariableDesignationSyntax>,
    ISyntaxValidationRule<LocalFunctionStatementSyntax>,
    ISyntaxValidationRule<ForEachStatementSyntax>
{
    public static readonly CppReservedIdentifierRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.DoubleUnderscoreIdentifier, GSharpDiagnostics.CppKeywordIdentifier);

    private CppReservedIdentifierRule()
    {
    }

    public void Analyze(MethodDeclarationSyntax node, ref ValidationContext context) =>
        CheckIdentifier(node.Identifier, ref context);

    public void Analyze(LocalFunctionStatementSyntax node, ref ValidationContext context) =>
        CheckIdentifier(node.Identifier, ref context);

    public void Analyze(ParameterSyntax node, ref ValidationContext context)
    {
        CheckIdentifier(node.Identifier, ref context);
        CheckStructMembers(context.SemanticModel.GetDeclaredSymbol<IParameterSymbol>(node)?.Type, node.Identifier,
            ref context);
    }

    public void Analyze(VariableDeclaratorSyntax node, ref ValidationContext context)
    {
        CheckIdentifier(node.Identifier, ref context);
        CheckStructMembers(context.SemanticModel.GetDeclaredSymbol<ILocalSymbol>(node)?.Type, node.Identifier,
            ref context);
    }

    public void Analyze(SingleVariableDesignationSyntax node, ref ValidationContext context)
    {
        CheckIdentifier(node.Identifier, ref context);
        CheckStructMembers(context.SemanticModel.GetDeclaredSymbol<ILocalSymbol>(node)?.Type, node.Identifier,
            ref context);
    }

    public void Analyze(ForEachStatementSyntax node, ref ValidationContext context)
    {
        CheckIdentifier(node.Identifier, ref context);
        CheckStructMembers(context.SemanticModel.GetDeclaredSymbol<ILocalSymbol>(node)?.Type, node.Identifier,
            ref context);
    }

    private static DiagnosticDescriptor? GetViolation(string identifier)
    {
        if (identifier.Contains("__"))
        {
            return GSharpDiagnostics.DoubleUnderscoreIdentifier;
        }

        return CppNameFormatter.IsReservedCppKeyword(identifier) ? GSharpDiagnostics.CppKeywordIdentifier : null;
    }

    private static void CheckIdentifier(SyntaxToken identifier, ref ValidationContext context)
    {
        if (GetViolation(identifier.ValueText) is { } descriptor)
        {
            context.ReportDiagnostic(descriptor, identifier.GetLocation(), identifier.ValueText);
        }
    }

    private static void CheckStructMembers(ITypeSymbol? type, SyntaxToken declaration, ref ValidationContext context)
    {
        if (type is null)
        {
            return;
        }

        var structType = UnwrapElementType(type);
        if (TryFindReservedMember(structType, depth: 0, out var memberPath, out var descriptor))
        {
            context.ReportDiagnostic(descriptor, declaration.GetLocation(), $"{structType.Name}.{memberPath}");
        }
    }

    private static ITypeSymbol UnwrapElementType(ITypeSymbol type)
    {
        while (true)
        {
            if (type is IPointerTypeSymbol pointer)
            {
                type = pointer.PointedAtType;
            }
            else if (type.TryGetArrayLikeElementType(out var elementType))
            {
                type = elementType;
            }
            else
            {
                return type;
            }
        }
    }

    // [WHY] Struct definitions are emitted once per kernel and dialect, so reporting at the declaration site that
    // pulls the struct into GPU code avoids duplicate diagnostics and keeps host-only structs unaffected.
    private static bool TryFindReservedMember(ITypeSymbol type, int depth, [NotNullWhen(true)] out string? memberPath,
        [NotNullWhen(true)] out DiagnosticDescriptor? descriptor)
    {
        // Value-type fields cannot form cycles; the bound only guards self-referencing array fields.
        const int MaxDepth = 8;

        memberPath = null;
        descriptor = null;
        if (depth > MaxDepth || !CudaLayoutRules.IsCustomStruct(type))
        {
            return false;
        }

        foreach (var member in type.GetMembers())
        {
            var memberType = member switch
            {
                IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false } field => field.Type,
                IPropertySymbol { IsStatic: false } property => property.Type,
                _ => null
            };

            if (memberType is null)
            {
                continue;
            }

            descriptor = GetViolation(member.Name);
            if (descriptor is not null)
            {
                memberPath = member.Name;
                return true;
            }

            if (TryFindReservedMember(UnwrapElementType(memberType), depth + 1, out var nestedPath, out descriptor))
            {
                memberPath = $"{member.Name}.{nestedPath}";
                return true;
            }
        }

        return false;
    }
}
