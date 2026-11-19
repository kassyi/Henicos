namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.ReadOnlyArrays;

/// <summary>
/// Records tuple deconstruction assignments for <see cref="AliasSources"/>, and marks as given an unread value the
/// targets of a deconstruction that is not a tuple literal, of a <c>ref</c> or <c>out</c> argument, of a mutable
/// <c>ref</c> and of an address.
/// </summary>
internal static class AliasDeconstructionSources
{
    /// <summary>
    /// Gives each variable of <paramref name="target"/> its element of <paramref name="value"/> when that is a tuple
    /// literal of the same shape, and an unread value otherwise (also when <paramref name="value"/> is
    /// <see langword="null"/>).
    /// </summary>
    public static void AddDeconstruction(AliasSources sources, SyntaxNode target, ExpressionSyntax? value)
    {
        if (target is DeclarationExpressionSyntax declaration)
        {
            target = declaration.Designation;
        }

        var tuple = value?.UnwrapParentheses() as TupleExpressionSyntax;
        switch (target)
        {
            case TupleExpressionSyntax left:
                for (var i = 0; i < left.Arguments.Count; i++)
                {
                    AddDeconstruction(sources, left.Arguments[i].Expression, ElementOf(tuple, i, left.Arguments.Count));
                }

                return;
            case ParenthesizedVariableDesignationSyntax left:
                for (var i = 0; i < left.Variables.Count; i++)
                {
                    AddDeconstruction(sources, left.Variables[i], ElementOf(tuple, i, left.Variables.Count));
                }

                return;
            case SingleVariableDesignationSyntax designation when
                sources.Model.GetDeclaredSymbol(designation) is ILocalSymbol local:
                Give(sources, AliasKey.ForVariable(local), value);
                return;
            case ExpressionSyntax expression when !sources.Model.IsDiscard(expression) &&
                AliasKey.Of(expression, sources.Model) is { } key:
                Give(sources, key, value);
                return;
        }
    }

    /// <summary>Marks every variable of <paramref name="target"/> as given an unread value.</summary>
    public static void MarkTargetUnread(AliasSources sources, SyntaxNode target) =>
        AddDeconstruction(sources, target, null);

    private static ExpressionSyntax? ElementOf(TupleExpressionSyntax? tuple, int index, int count) =>
        tuple is not null && tuple.Arguments.Count == count ? tuple.Arguments[index].Expression : null;

    private static void Give(AliasSources sources, AliasKey key, ExpressionSyntax? value)
    {
        if (value is null)
        {
            sources.MarkUnread(key);
        }
        else
        {
            sources.AddValue(key, value);
        }
    }
}
