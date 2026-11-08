using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// A discard passed to an <c>out</c> parameter (<c>TryParse(s, out _)</c>, <c>out int _</c>): C# gives the callee a
/// variable that nobody reads; C++ needs one to take the address of (#1289). The statement prelude declares
/// <c>T __gsharp_discard_&lt;n&gt;{};</c> and the argument is written <c>&amp;__gsharp_discard_&lt;n&gt;</c>.
/// </summary>
internal static class DiscardOutArgument
{
    // Only this helper spells the variable names
    private const string StoragePrefix = "__gsharp_discard_";

    /// <summary>The GSH0003 text for a discard <c>out</c> argument where no statement declares its variable.</summary>
    public const string NoStatement = "discard out argument outside a statement that can declare its variable";

    /// <summary>Declares the variable of <paramref name="node"/> when it is a discard <c>out</c> argument.</summary>
    public static void DeclareStorage(SyntaxNode node, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        if (node is not ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.OutKeyword, Expression: var expression } ||
            !model.IsDiscard(expression) ||
            GetType(expression, model) is not { } type)
        {
            return;
        }

        var number = context.Emitter.DiscardStorages.Count;
        context.Emitter.DiscardStorages[expression] = number;
        StructOrdering.RegisterUsedStructIfApplicable(type, ref context);
        context.Emitter.Dialect.AppendType(ref context, type);
        context.Emitter.Writer.AppendLine($" {StoragePrefix}{number}{{}};");
    }

    /// <summary>
    /// Writes <c>&amp;__gsharp_discard_&lt;n&gt;</c> when <paramref name="expression"/>, passed by reference, is a discard;
    /// false for any other expression.
    /// </summary>
    public static bool TryAppend(ExpressionSyntax expression, ref TranspileContext context)
    {
        if (!context.Query.SemanticModel.IsDiscard(expression))
        {
            return false;
        }

        if (!context.Emitter.DiscardStorages.TryGetValue(expression, out var number))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, expression, NoStatement);
            context.Visitor.AppendUnsupportedPlaceholder(expression);
            return true;
        }

        context.Emitter.Writer.Append($"&{StoragePrefix}{number}");
        return true;
    }

    // The type of the parameter the discard is passed to
    private static ITypeSymbol? GetType(ExpressionSyntax discard, SemanticModel model) =>
        model.GetTypeSymbol(discard) ?? (model.GetSafeOperation(discard) as IDiscardOperation)?.Type;
}
