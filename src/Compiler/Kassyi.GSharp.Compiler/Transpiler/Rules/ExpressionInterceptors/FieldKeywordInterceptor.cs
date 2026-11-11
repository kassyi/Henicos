using Kassyi.GSharp.Compiler.Analysis;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionInterceptors;

/// <summary>
/// Writes the <c>field</c> keyword in an accessor of a struct property as the backing field of the property
/// (<c>__gsharp_bf_&lt;property&gt;</c>, <see cref="CppNameResolver"/>), a member of the struct whose member function
/// the accessor is. The keyword is recognized by <see cref="FieldKeywordExpression"/>.
/// </summary>
internal sealed class FieldKeywordInterceptor : IExpressionInterceptor
{
    /// <inheritdoc />
    public bool TryAppend(ExpressionSyntax expression, ref TranspileContext context)
    {
        if (!FieldKeywordExpression.TryGetBackingField(expression, context.Query.SemanticModel, out var field))
        {
            return false;
        }

        CppNameResolver.AppendSymbolName(field, ref context);
        return true;
    }
}
