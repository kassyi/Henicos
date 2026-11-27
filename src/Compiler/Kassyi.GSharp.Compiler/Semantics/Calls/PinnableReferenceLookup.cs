namespace Kassyi.GSharp.Compiler.Semantics;

/// <summary>
/// The <c>GetPinnableReference</c> that C# calls for <c>fixed (T* p = e)</c> when <c>e</c> is not an address, an array,
/// a string or a span: <c>fixed (int* p = s)</c> pins <c>ref s.GetPinnableReference()</c>, an instance method or else
/// an extension method of the type of <c>s</c>.
/// </summary>
/// <remarks>
/// [WHY] Roslyn 4.8 does not show the call as an operation, so the method is looked up as C# looks it up: the
/// parameterless instance methods of that name first, then the extension methods in scope, each returning by
/// reference. G# writes arrays, strings and spans as its own views, which convert to a pointer without the call.
/// </remarks>
internal static class PinnableReferenceLookup
{
    private const string MethodName = "GetPinnableReference";

    /// <summary>
    /// The method that the <c>fixed</c> statement calls for <paramref name="initializer"/>, an extension method in its
    /// reduced form; <see langword="null"/> when it calls none.
    /// </summary>
    public static IMethodSymbol? Find(ExpressionSyntax initializer, SemanticModel model)
    {
        if (initializer is PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.AddressOfExpression } ||
            !model.TryGetOriginalNode(initializer, out var original, out var originalModel) ||
            originalModel.GetTypeInfo(original).Type is not
                INamedTypeSymbol { SpecialType: not SpecialType.System_String } type ||
            IsSpanType(type))
        {
            return null;
        }

        var candidates = originalModel.LookupSymbols(original.SpanStart, type, MethodName,
            includeReducedExtensionMethods: true);
        return candidates
                   .AsStackEnumerable()
                   .FirstOrDefault(static symbol => IsPinnable(symbol, extension: false)) as IMethodSymbol ??
               candidates
                   .AsStackEnumerable()
                   .FirstOrDefault(static symbol => IsPinnable(symbol, extension: true)) as IMethodSymbol;
    }

    private static bool IsSpanType(INamedTypeSymbol type) =>
        type is { TypeArguments.Length: 1, Name: TypeConstants.SpanType or TypeConstants.ReadOnlySpanType } &&
        type.ContainingNamespace.IsSystemNamespace();

    // A parameterless method that returns by reference: an instance method, or the reduced form of an extension method
    private static bool IsPinnable(ISymbol symbol, bool extension) =>
        symbol is IMethodSymbol { Parameters.Length: 0, IsStatic: false } method &&
        (method.ReturnsByRef || method.ReturnsByRefReadonly) &&
        (method.ReducedFrom is not null) == extension;
}
