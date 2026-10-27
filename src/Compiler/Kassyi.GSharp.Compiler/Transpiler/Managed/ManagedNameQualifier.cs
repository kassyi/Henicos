using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Writes the names of a managed body with their full path from <c>global::</c>, because the generated file that holds
/// the body has none of the user's <c>using</c> directives.
/// </summary>
/// <remarks>
/// <para>
/// [WHY] The managed fallback copies the body of a kernel or device function into a generated file with a fixed set of
/// <c>using</c> directives. A name that the user's file resolved through its own <c>using</c>, <c>using static</c> or
/// <c>using</c> alias would not resolve there (CS0103, #433). A simple name bound to a namespace, a type or a static
/// member is therefore written after the full path of what contains it (<c>QualifyPair</c> becomes
/// <c>global::N.QualifyPair</c>, <c>Max</c> through <c>using static System.Math</c> becomes
/// <c>global::System.Math.Max</c>); the name itself stays as written. An alias is written as what it names, an extension
/// method call in its reduced form as the static call, and <c>nameof(...)</c> as the string it is, so that no name in
/// the body needs the user's directives.
/// </para>
/// </remarks>
internal static class ManagedNameQualifier
{
    private const string AttributeSuffix = "Attribute";

    /// <summary>
    /// Qualifies every name inside <paramref name="node"/>, a node of the tree that <paramref name="semanticModel"/>
    /// binds or of a body lowered from it.
    /// </summary>
    public static TNode Qualify<TNode>(TNode node, SemanticModel semanticModel) where TNode : SyntaxNode =>
        new Rewriter(semanticModel).Visit(node) as TNode
        ?? throw new InvalidOperationException("Qualifying the names of a managed body removed a node.");

    /// <summary>
    /// The qualified form of the name <paramref name="node"/>, whose rewritten children are in <paramref name="visited"/>;
    /// <paramref name="visited"/> itself when the name stays as written. For the <c>Visit</c> overrides of a rewriter.
    /// </summary>
    public static SyntaxNode? QualifyName(SimpleNameSyntax node, SyntaxNode? visited, SemanticModel semanticModel) =>
        visited is SimpleNameSyntax name && TryQualify(node, name, semanticModel) is { } qualified ? qualified : visited;

    /// <summary>
    /// Rewrites a call that names something through the user's directives as a whole: <c>nameof(...)</c> becomes its
    /// string, an extension method call in its reduced form (<c>x.Ext(a)</c>) the static call
    /// <c>global::N.C.Ext(x, a)</c>. <paramref name="visitor"/> rewrites the receiver, the name and the arguments.
    /// <see langword="null"/> for any other call.
    /// </summary>
    /// <remarks>
    /// <para>
    /// [WHY] <c>nameof</c> of an alias gives the alias name, which qualifying would change; its value is a constant, so
    /// the string is written instead and nothing in it needs to resolve.
    /// </para>
    /// <para>
    /// The reduced form finds the method through the <c>using</c> of its namespace, which the generated file does not
    /// have (CS1061). The static call evaluates the receiver and then the arguments, as the reduced form does. A
    /// <c>ref this</c> receiver is passed with <c>ref</c>; an <c>in this</c> receiver with <c>in</c> when it is a
    /// variable of the parameter type, and without a modifier otherwise, where <c>in</c> would not compile (CS8156) and
    /// C# copies the value into a temporary in both forms.
    /// </para>
    /// </remarks>
    public static ExpressionSyntax? TryRewriteInvocation(InvocationExpressionSyntax node,
        CSharpSyntaxVisitor<SyntaxNode?> visitor, SemanticModel semanticModel)
    {
        if (node.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" } &&
            semanticModel.GetSymbol(node) is not IMethodSymbol &&
            semanticModel.GetSafeConstantValue(node) is { HasValue: true, Value: string value })
        {
            // "<value>"
            return SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(value))
                .WithTriviaFrom(node);
        }

        if (node.Expression is not MemberAccessExpressionSyntax access ||
            semanticModel.GetSymbol(node) is not IMethodSymbol { ReducedFrom: { Parameters.Length: > 0 } extension } ||
            semanticModel.GetOriginalNode(node) is not InvocationExpressionSyntax)
        {
            return null;
        }

        var receiverParameter = extension.Parameters[0];
        var receiverModifier = receiverParameter.RefKind switch
        {
            RefKind.Ref => SyntaxKind.RefKeyword,
            RefKind.In or RefKind.RefReadOnlyParameter when IsVariableOfType(access.Expression, receiverParameter.Type,
                semanticModel) => SyntaxKind.InKeyword,
            _ => SyntaxKind.None
        };

        var receiver = visitor.Visit(access.Expression) as ExpressionSyntax ?? access.Expression;
        var name = visitor.Visit(access.Name) as SimpleNameSyntax ?? access.Name;
        var arguments = visitor.Visit(node.ArgumentList) as ArgumentListSyntax ?? node.ArgumentList;

        var receiverArgument = SyntaxFactory.Argument(receiver.WithoutTrivia());
        if (receiverModifier != SyntaxKind.None)
        {
            receiverArgument = receiverArgument.WithRefKindKeyword(SyntaxFactory.Token(receiverModifier));
        }

        // global::<namespace>.<class>.<name>(<receiver>, <arguments>)
        return SyntaxFactory.InvocationExpression(
            SyntaxFactory.QualifiedName(
                SyntaxFactory.ParseName(extension.ContainingType.GetFullyQualifiedCsharpName()),
                name.WithoutTrivia()
            ),
            arguments.WithArguments(arguments.Arguments.Insert(0, receiverArgument))
        ).WithTriviaFrom(node);
    }

    /// <summary>
    /// The interpolation <paramref name="visited"/> with its expression in parentheses when a qualified name put
    /// <c>::</c> into it: the lexer would read <c>{global::N.C}</c> as the expression <c>global</c> with a format.
    /// </summary>
    public static SyntaxNode? ParenthesizeInterpolation(SyntaxNode? visited)
    {
        if (visited is not InterpolationSyntax { Expression: not ParenthesizedExpressionSyntax } interpolation)
        {
            return visited;
        }

        foreach (var token in interpolation.Expression.DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.ColonColonToken))
            {
                // {(<expression>)}
                return interpolation.WithExpression(SyntaxFactory.ParenthesizedExpression(interpolation.Expression));
            }
        }

        return visited;
    }

    // The qualified form of one name, or null when it stays as written. The checks run from the cheapest (syntax only)
    // to the most expensive (the original node of a lowered one).
    private static TypeSyntax? TryQualify(SimpleNameSyntax node, SimpleNameSyntax visited, SemanticModel semanticModel)
    {
        if (!IsQualifiablePosition(node) ||
            semanticModel.GetSymbol(node) is not { } symbol ||
            !TryGetNamedSymbol(node, symbol, out var named) ||
            !IsSameName(node, semanticModel))
        {
            return null;
        }

        // A using alias (using M = System.Math; using Pair = (int A, int B);) is written as what it names. A tuple, array
        // or pointer is not a name, but such an alias only stands where a type does, which takes any type syntax.
        if (!IsWrittenName(node, named))
        {
            return named is ITypeSymbol or INamespaceSymbol
                ? SyntaxFactory.ParseTypeName(FormatFullName(named)).WithTriviaFrom(visited)
                : null;
        }

        var name = visited.WithoutTrivia();
        var container = GetContainerName(named);
        return container.Length == 0
            // global::<name>
            ? SyntaxFactory.AliasQualifiedName(
                SyntaxFactory.IdentifierName(SyntaxFactory.Token(SyntaxKind.GlobalKeyword)),
                name
            ).WithTriviaFrom(visited)
            // <container>.<name>
            : SyntaxFactory.QualifiedName(SyntaxFactory.ParseName(container), name).WithTriviaFrom(visited);
    }

    // A name that already follows a qualifier, names a member of an object, or labels an argument stays as it is; so do
    // the contextual keywords that look like names (var, nint, nuint, unmanaged, notnull)
    private static bool IsQualifiablePosition(SimpleNameSyntax node)
    {
        if (node is IdentifierNameSyntax { IsVar: true } or IdentifierNameSyntax { IsNint: true } or
            IdentifierNameSyntax { IsNuint: true } or IdentifierNameSyntax { IsUnmanaged: true } or
            IdentifierNameSyntax { IsNotNull: true })
        {
            return false;
        }

        return node.Parent switch
        {
            MemberAccessExpressionSyntax access => access.Name != node,
            QualifiedNameSyntax qualified => qualified.Right != node,
            MemberBindingExpressionSyntax or AliasQualifiedNameSyntax or NameColonSyntax or NameEqualsSyntax => false,
            _ => true
        };
    }

    // [WHY] Defensive: a node of a lowered body is bound through its original-span annotation, and a node that lowering
    // synthesized can carry the annotation of the source node it stands for, which need not be this name (a hoisted
    // local rewritten to a context field carries the span of its declarator). Such a node is left alone rather than
    // replaced by the name of whatever the span binds to.
    private static bool IsSameName(SimpleNameSyntax node, SemanticModel semanticModel) =>
        semanticModel.GetOriginalNode(node) is SimpleNameSyntax original &&
        string.Equals(original.Identifier.ValueText, node.Identifier.ValueText, StringComparison.Ordinal);

    // What the name names: a namespace, a type, a static member, or the attribute class of an attribute's name (which
    // binds to the constructor)
    private static bool TryGetNamedSymbol(SimpleNameSyntax node, ISymbol symbol, [NotNullWhen(true)] out ISymbol? named)
    {
        named = symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor, ContainingType: { } attributeType }
                when node.Parent is AttributeSyntax attribute && attribute.Name == node => attributeType,
            INamespaceSymbol { IsGlobalNamespace: false } => symbol,
            INamedTypeSymbol { TypeKind: not TypeKind.Error } => symbol,
            IArrayTypeSymbol or IPointerTypeSymbol => symbol,
            IMethodSymbol { IsStatic: true, MethodKind: MethodKind.Ordinary, ReducedFrom: null } or
                IFieldSymbol { IsStatic: true } or IPropertySymbol { IsStatic: true } or IEventSymbol { IsStatic: true }
                when symbol.ContainingType != null => symbol,
            _ => null
        };
        return named != null;
    }

    // Whether the name is the symbol's own name rather than an alias of it; an attribute may leave out "Attribute"
    private static bool IsWrittenName(SimpleNameSyntax node, ISymbol named)
    {
        var written = node.Identifier.ValueText;
        return string.Equals(written, named.Name, StringComparison.Ordinal) ||
               (node.Parent is AttributeSyntax &&
                named.Name.Length == written.Length + AttributeSuffix.Length &&
                named.Name.StartsWith(written, StringComparison.Ordinal) &&
                named.Name.EndsWith(AttributeSuffix, StringComparison.Ordinal));
    }

    // The full name of what contains the symbol, from global::; empty for the global namespace
    private static string GetContainerName(ISymbol symbol) =>
        symbol.ContainingType is { } containingType
            ? containingType.GetFullyQualifiedCsharpName()
            : GetNamespaceName(symbol.ContainingNamespace);

    private static string GetNamespaceName(INamespaceSymbol? ns) =>
        ns is null or { IsGlobalNamespace: true } ? string.Empty : "global::" + ns.GetFullNamespaceName();

    // [WHY] The C# 12 alias of any type (using Ints = int[]; using Pair = (int A, int B);) keeps its tuple element
    // names, which only the display format writes; this runs once per alias use, not per emitted line.
    private static string FormatFullName(ISymbol symbol) =>
#pragma warning disable RS0030 // The tuple element names of an alias target are written by no SourceWriter helper
        symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
#pragma warning restore RS0030

    private static bool IsVariableOfType(ExpressionSyntax receiver, ITypeSymbol type, SemanticModel semanticModel) =>
        semanticModel.GetSafeOperation(receiver) is { } operation &&
        operation is ILocalReferenceOperation or IParameterReferenceOperation or IFieldReferenceOperation
            or IArrayElementReferenceOperation &&
        operation.Type.IsEqualTo(type);

    private sealed class Rewriter(SemanticModel semanticModel) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) =>
            QualifyName(node, base.VisitIdentifierName(node), semanticModel);

        public override SyntaxNode? VisitGenericName(GenericNameSyntax node) =>
            QualifyName(node, base.VisitGenericName(node), semanticModel);

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node) =>
            TryRewriteInvocation(node, this, semanticModel) ?? base.VisitInvocationExpression(node);

        public override SyntaxNode? VisitInterpolation(InterpolationSyntax node) =>
            ParenthesizeInterpolation(base.VisitInterpolation(node));
    }
}
