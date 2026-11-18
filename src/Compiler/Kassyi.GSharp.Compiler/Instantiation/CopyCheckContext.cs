namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// What the comparison of a copy with its declaration reads, node by node: both semantic models, the plan the copy was
/// written from, the type arguments of the instance and the node numbers of the declaration.
/// </summary>
internal sealed class CopyCheckContext(
    SemanticModel model,
    SemanticModel copyModel,
    GenericCopyPlan plan,
    InstanceSymbolMap map,
    CopySymbolComparer comparer,
    Dictionary<SyntaxNode, int> originalNodes)
{
    private INamedTypeSymbol? _mathType;

    /// <summary>The semantic model of the declaration.</summary>
    public SemanticModel Model => model;

    /// <summary>The semantic model of the copy.</summary>
    public SemanticModel CopyModel => copyModel;

    /// <summary>The plan the copy was written from.</summary>
    public GenericCopyPlan Plan => plan;

    /// <summary>The type arguments of the instance.</summary>
    public InstanceSymbolMap Map => map;

    /// <summary>Compares symbols and types of the declaration (read as the instance) with those of the copy.</summary>
    public CopySymbolComparer Comparer => comparer;

    /// <summary>The number of each node of the declaration.</summary>
    public Dictionary<SyntaxNode, int> OriginalNodes => originalNodes;

    /// <summary>Whether <paramref name="type"/> is <c>System.Math</c>; the type is looked up once per check.</summary>
    public bool IsSystemMath(ITypeSymbol? type)
    {
        _mathType ??= copyModel.Compilation.GetTypeByMetadataName("System.Math");
        return _mathType is not null && SymbolEqualityComparer.Default.Equals(_mathType, type);
    }

    /// <summary>The type of the declaration read as the instance.</summary>
    public ITypeSymbol? Substitute(ITypeSymbol? type) => type is null ? null : map.Substitute(type);

    /// <summary>The node without the parentheses and the <c>checked</c> / <c>unchecked</c> around it.</summary>
    public static SyntaxNode UnwrapNode(SyntaxNode node)
    {
        while (node is ParenthesizedExpressionSyntax or CheckedExpressionSyntax)
        {
            node = node is ParenthesizedExpressionSyntax parenthesized
                ? parenthesized.Expression
                : ((CheckedExpressionSyntax)node).Expression;
        }

        return node;
    }
}
