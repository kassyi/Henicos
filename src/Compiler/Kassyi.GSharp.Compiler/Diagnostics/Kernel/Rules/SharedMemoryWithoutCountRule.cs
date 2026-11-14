namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports each way that a <c>SharedMemory&lt;T&gt;</c> can come to exist without an element count (GSH0003): the
/// <c>default</c> of it, a local declared without an initializer, a struct local that holds one and is <c>default</c>
/// or <c>new T()</c> with no assignment, and a call of a generic method that uses <c>default</c> of a type
/// parameter which the call instantiates with it. Shared memory has no size until it is declared with an element
/// count.
/// </summary>
/// <remarks>
/// <c>new SharedMemory&lt;T&gt;()</c> needs no rule: its constructor is obsolete as an error, so the C# compiler
/// rejects it. Two shapes are not decided, because they need the flow of the whole program: a struct member that
/// no code assigns anywhere, and a <c>default</c> struct that another method or a field hands in. A struct local is
/// reported only when its method never assigns the local or a member of it and never passes it by reference.
/// </remarks>
internal sealed class SharedMemoryWithoutCountRule :
    ISyntaxValidationRule<LiteralExpressionSyntax>,
    ISyntaxValidationRule<DefaultExpressionSyntax>,
    ISyntaxValidationRule<VariableDeclaratorSyntax>,
    ISyntaxValidationRule<InvocationExpressionSyntax>
{
    // How many structs inside structs are followed when looking for a SharedMemory<T> member
    private const int MaxHolderDepth = 3;

    public static readonly SharedMemoryWithoutCountRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.UnsupportedSyntax);

    private SharedMemoryWithoutCountRule()
    {
    }

    public void Analyze(LiteralExpressionSyntax node, ref ValidationContext context)
    {
        if (node.IsKind(SyntaxKind.DefaultLiteralExpression))
        {
            ReportDefault(node, ref context);
        }
    }

    public void Analyze(DefaultExpressionSyntax node, ref ValidationContext context) =>
        ReportDefault(node, ref context);

    public void Analyze(VariableDeclaratorSyntax node, ref ValidationContext context)
    {
        if (node.Parent?.Parent is not LocalDeclarationStatementSyntax ||
            context.SemanticModel.GetDeclaredSymbol(node) is not ILocalSymbol { Type: { } type } local)
        {
            return;
        }

        if (type.IsSharedMemoryType())
        {
            if (node.Initializer == null)
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.UnsupportedSyntax,
                    node,
                    GpuCodeConstraints.SharedMemoryLocalWithoutInitializer);
            }

            return;
        }

        if (node.Initializer is { Value: var value } &&
            IsWithoutValue(value) &&
            HoldsSharedMemory(type, MaxHolderDepth) &&
            !IsAssignedOrShared(node, local, context.SemanticModel))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.UnsupportedSyntax,
                value,
                GpuCodeConstraints.SharedMemoryHolderWithoutMemory);
        }
    }

    public void Analyze(InvocationExpressionSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetSymbol(node) is not IMethodSymbol { IsGenericMethod: true } method ||
            !method.TypeArguments.AsStackEnumerable().Any(static type => type.IsSharedMemoryType()) ||
            !method.OriginalDefinition.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration))
        {
            return;
        }

        var declarationModel = context.SemanticModel.Compilation.GetSemanticModel(declaration.SyntaxTree);
        var usesDefaultOfTypeParameter = declaration
            .DescendantNodes()
            .AsStackEnumerable()
            .Any(declarationModel, static (syntax, model) =>
                IsDefault(syntax) && model.GetTypeSymbol(syntax) is ITypeParameterSymbol);
        if (usesDefaultOfTypeParameter)
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.UnsupportedSyntax,
                node,
                GpuCodeConstraints.SharedMemoryGenericDefault);
        }
    }

    // The type of a default literal is the type it converts to
    private static void ReportDefault(ExpressionSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetTypeSymbol(node).IsSharedMemoryType())
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.UnsupportedSyntax,
                node,
                GpuCodeConstraints.SharedMemoryWithoutCount);
        }
    }

    private static bool IsDefault(SyntaxNode node) =>
        node is DefaultExpressionSyntax || node.IsKind(SyntaxKind.DefaultLiteralExpression);

    // default, default(T), new T() and new(): a struct value with every member at its zero value
    private static bool IsWithoutValue(ExpressionSyntax value) =>
        IsDefault(value) ||
        value is BaseObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0, Initializer: null };

    // A SharedMemory<T> field or auto-property of the struct, or of a struct that it holds
    private static bool HoldsSharedMemory(ITypeSymbol type, int depth)
    {
        if (depth == 0 || type is not { TypeKind: TypeKind.Struct } || type.IsSharedMemoryType())
        {
            return false;
        }

        return type.GetMembers()
            .AsStackEnumerable()
            .Any(depth, static (member, remaining) => member switch
            {
                IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false, Type: { } fieldType } =>
                    fieldType.IsSharedMemoryType() || HoldsSharedMemory(fieldType, remaining - 1),
                IPropertySymbol { IsStatic: false, IsIndexer: false, Type: { } propertyType } =>
                    propertyType.IsSharedMemoryType() || HoldsSharedMemory(propertyType, remaining - 1),
                _ => false
            });
    }

    // The method may give the local a value: it assigns the local or a member of it, or passes it by reference
    private static bool IsAssignedOrShared(VariableDeclaratorSyntax declarator, ILocalSymbol local, SemanticModel model)
    {
        var scope = declarator.FirstAncestorOrSelf<BlockSyntax>();
        if (scope == null)
        {
            return true;
        }

        return scope
            .DescendantNodes()
            .AsStackEnumerable()
            .Any((local, model), static (syntax, state) => syntax switch
            {
                AssignmentExpressionSyntax assignment => IsRootedAt(assignment.Left, state.local, state.model),
                ArgumentSyntax { RefKindKeyword.RawKind: not (int)SyntaxKind.None } argument =>
                    IsRootedAt(argument.Expression, state.local, state.model),
                _ => false
            });
    }

    // The local itself, or a member access chain that starts at it
    private static bool IsRootedAt(ExpressionSyntax expression, ILocalSymbol local, SemanticModel model)
    {
        var current = IteratorTypeResolver.UnwrapParentheses(expression);
        while (current is MemberAccessExpressionSyntax access)
        {
            current = IteratorTypeResolver.UnwrapParentheses(access.Expression);
        }

        return current is IdentifierNameSyntax name &&
               SymbolEqualityComparer.Default.Equals(model.GetSymbol(name), local);
    }
}
