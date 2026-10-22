namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

using Kassyi.GSharp.Compiler.Layout;

/// <summary>
/// Rejects identifiers declared in GPU code that contain <c>__</c> (GSH0055), including members of custom structs
/// reachable from a declaration's type.
/// </summary>
/// <remarks>
/// The prefix <c>__gsharp</c> and the <c>__</c> separator of global names belong to G#, and the C++ implementation
/// reserves <c>__</c>. Every other name, C++ keywords included, is derived from its symbol by
/// <see cref="CppNameResolver"/>, so it needs no check.
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
        ImmutableArray.Create(GSharpDiagnostics.DoubleUnderscoreIdentifier);

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

    private static bool IsReserved(string identifier) => identifier.Contains("__");

    private static void CheckIdentifier(SyntaxToken identifier, ref ValidationContext context)
    {
        if (IsReserved(identifier.ValueText))
        {
            context.ReportDiagnostic(GSharpDiagnostics.DoubleUnderscoreIdentifier, identifier.GetLocation(),
                identifier.ValueText);
        }
    }

    private static void CheckStructMembers(ITypeSymbol? type, SyntaxToken declaration, ref ValidationContext context)
    {
        if (type is null)
        {
            return;
        }

        var structType = UnwrapElementType(type);
        if (TryFindReservedMember(structType, depth: 0, out var memberPath))
        {
            context.ReportDiagnostic(GSharpDiagnostics.DoubleUnderscoreIdentifier, declaration.GetLocation(),
                $"{structType.Name}.{memberPath}");
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
    private static bool TryFindReservedMember(ITypeSymbol type, int depth, [NotNullWhen(true)] out string? memberPath)
    {
        // Value-type fields cannot form cycles; the bound only guards self-referencing array fields.
        const int MaxDepth = 8;

        memberPath = null;
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

            if (IsReserved(member.Name))
            {
                memberPath = member.Name;
                return true;
            }

            if (TryFindReservedMember(UnwrapElementType(memberType), depth + 1, out var nestedPath))
            {
                memberPath = $"{member.Name}.{nestedPath}";
                return true;
            }
        }

        return false;
    }
}
