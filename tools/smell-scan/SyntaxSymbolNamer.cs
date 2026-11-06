using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Builds the qualified name of a member from its syntax alone, such as <c>Ns.Type.Method(int,string)</c>.
/// The name does not contain the file, so it stays the same when a type moves to another file.
/// </summary>
internal static class SyntaxSymbolNamer
{
    /// <summary>Gets the qualified name of <paramref name="node"/> and the members, types and namespaces around it.</summary>
    public static string Name(SyntaxNode node)
    {
        var segments = new List<string>();
        for (var current = node; current is not null; current = current.Parent)
        {
            var segment = SegmentOf(current);
            if (segment is not null)
            {
                segments.Add(segment);
            }
        }

        segments.Reverse();
        return string.Join('.', segments);
    }

    private static string? SegmentOf(SyntaxNode node) => node switch
    {
        BaseNamespaceDeclarationSyntax ns => SyntaxText.Compact(ns.Name),
        TypeDeclarationSyntax type => TypeSegment(type),
        EnumDeclarationSyntax enumDeclaration => enumDeclaration.Identifier.Text,
        MethodDeclarationSyntax method => ExplicitInterface(method.ExplicitInterfaceSpecifier)
            + method.Identifier.Text + TypeParameters(method.TypeParameterList) + Parameters(method.ParameterList),
        ConstructorDeclarationSyntax constructor => constructor.Identifier.Text + Parameters(constructor.ParameterList),
        DestructorDeclarationSyntax destructor => "~" + destructor.Identifier.Text
            + Parameters(destructor.ParameterList),
        OperatorDeclarationSyntax op => "operator " + op.OperatorToken.Text + Parameters(op.ParameterList),
        ConversionOperatorDeclarationSyntax conversion => conversion.ImplicitOrExplicitKeyword.Text
            + " operator " + SyntaxText.Compact(conversion.Type) + Parameters(conversion.ParameterList),
        LocalFunctionStatementSyntax local => local.Identifier.Text + TypeParameters(local.TypeParameterList)
            + Parameters(local.ParameterList),
        AnonymousFunctionExpressionSyntax => "<lambda>",
        PropertyDeclarationSyntax property => ExplicitInterface(property.ExplicitInterfaceSpecifier) + property.Identifier.Text,
        IndexerDeclarationSyntax indexer => ExplicitInterface(indexer.ExplicitInterfaceSpecifier) + "this["
            + ParameterTypes(indexer.ParameterList.Parameters) + "]",
        EventDeclarationSyntax eventDeclaration => ExplicitInterface(eventDeclaration.ExplicitInterfaceSpecifier)
            + eventDeclaration.Identifier.Text,
        AccessorDeclarationSyntax accessor => accessor.Keyword.Text,
        GlobalStatementSyntax => TopLevelStatements.Symbol,
        _ => null,
    };

    /// <summary>Gets the name of the primary constructor of <paramref name="type"/>, written like an explicit one.</summary>
    public static string PrimaryConstructorName(TypeDeclarationSyntax type) =>
        Name(type) + "." + type.Identifier.Text + (type.ParameterList is null ? "()" : Parameters(type.ParameterList));

    /// <summary>
    /// Gets the name that the parts of a partial type share: the namespaces and types around it, each type with its number
    /// of type parameters (a part may rename them), such as <c>Ns.Outer`1.Inner</c>.
    /// </summary>
    public static string PartialTypeKey(TypeDeclarationSyntax type)
    {
        var segments = new List<string>();
        for (SyntaxNode? current = type; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case BaseNamespaceDeclarationSyntax ns:
                    segments.Add(SyntaxText.Compact(ns.Name));
                    break;
                case TypeDeclarationSyntax enclosing:
                    var arity = enclosing.TypeParameterList?.Parameters.Count ?? 0;
                    segments.Add(arity == 0 ? enclosing.Identifier.Text : enclosing.Identifier.Text + "`" + arity);
                    break;
            }
        }

        segments.Reverse();
        return string.Join('.', segments);
    }

    private static string TypeSegment(TypeDeclarationSyntax type)
    {
        // An extension block has no name; its receiver parameter tells two blocks apart.
        if (type.Identifier.Text.Length == 0)
        {
            return type.Keyword.Text + TypeParameters(type.TypeParameterList)
                + (type.ParameterList is null ? string.Empty : Parameters(type.ParameterList));
        }

        return type.Identifier.Text + TypeParameters(type.TypeParameterList);
    }

    private static string ExplicitInterface(ExplicitInterfaceSpecifierSyntax? specifier) =>
        specifier is null ? string.Empty : SyntaxText.Compact(specifier.Name) + ".";

    private static string TypeParameters(TypeParameterListSyntax? list) =>
        list is null ? string.Empty : SyntaxText.Compact(list);

    private static string Parameters(ParameterListSyntax list) => "(" + ParameterTypes(list.Parameters) + ")";

    private static string ParameterTypes(SeparatedSyntaxList<ParameterSyntax> parameters)
    {
        var parts = new List<string>(parameters.Count);
        foreach (var parameter in parameters)
        {
            var prefix = string.Empty;
            foreach (var modifier in parameter.Modifiers)
            {
                prefix += modifier.Text + " ";
            }

            parts.Add(prefix + (parameter.Type is null ? string.Empty : SyntaxText.Compact(parameter.Type)));
        }

        return string.Join(',', parts);
    }
}
