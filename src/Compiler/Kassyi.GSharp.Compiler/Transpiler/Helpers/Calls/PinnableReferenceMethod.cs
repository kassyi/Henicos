namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Calls;

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
internal static class PinnableReferenceMethod
{
    private const string MethodName = "GetPinnableReference";

    /// <summary>
    /// The method that the <c>fixed</c> statement calls for <paramref name="initializer"/>, an extension method in its
    /// reduced form; null when it calls none.
    /// </summary>
    public static IMethodSymbol? Find(ExpressionSyntax initializer, SemanticModel model)
    {
        if (initializer is PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.AddressOfExpression } ||
            !model.TryGetOriginalNode(initializer, out var original, out var originalModel) ||
            originalModel.GetTypeInfo(original).Type is not
                INamedTypeSymbol { SpecialType: not SpecialType.System_String } type ||
            InitializationSyntaxExtractor.TryGetArrayElementType(type, out _, out _))
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

    /// <summary>
    /// Writes <c> = &amp;&lt;function&gt;(&lt;receiver&gt;)</c> for <paramref name="declarator"/> of a <c>fixed</c>
    /// statement whose initializer is pinned through <c>GetPinnableReference</c>; false, writing nothing, for any other
    /// declarator.
    /// </summary>
    public static bool TryWriteInitializer(VariableDeclaratorSyntax declarator, ITypeSymbol? pointerType,
        ref TranspileContext context)
    {
        if (declarator.Parent?.Parent is not FixedStatementSyntax ||
            declarator.Initializer?.Value is not { } value ||
            Find(value, context.Query.SemanticModel) is not { } method)
        {
            return false;
        }

        context.Emitter.Writer.Append(" = ");
        if (!ImplicitDeviceCall.CheckCallable(value, method, ref context))
        {
            return true;
        }

        // (<pointer type>)&<function>(<receiver>): a ref readonly result is a const reference in C++, which C# pins as
        // a pointer that may be written
        if (method.ReturnsByRefReadonly)
        {
            using (context.Emitter.Writer.Parentheses())
            {
                context.Emitter.Dialect.AppendType(ref context, pointerType);
            }
        }

        context.Emitter.Writer.Append('&');
        ImplicitDeviceCall.AppendName(method, ref context);
        using (context.Emitter.Writer.Parentheses())
        {
            AppendReceiver(value, method, ref context);
        }

        return true;
    }

    // The receiver of the extension method: a variable by its address for a ref or in receiver (C# converts its value
    // to the pointer, so the conversion does not make it a temporary), any other value of an in receiver in a
    // temporary
    private static void AppendReceiver(ExpressionSyntax receiver, IMethodSymbol method, ref TranspileContext context)
    {
        if (method.ReducedFrom is not { Parameters: [{ RefKind: not RefKind.None }, ..] })
        {
            context.Visitor.Visit(receiver);
            return;
        }

        if (RefSyntaxHelper.ClassifyStorage(receiver, context.Query.SemanticModel) == CppLocation.Storage)
        {
            context.Emitter.Writer.Append('&');
            context.Visitor.Visit(receiver);
            return;
        }

        RefSyntaxHelper.TranspileAsByRefArgument(ref context, receiver);
    }

    // A parameterless method that returns by reference: an instance method, or the reduced form of an extension method
    private static bool IsPinnable(ISymbol symbol, bool extension) =>
        symbol is IMethodSymbol { Parameters.Length: 0, IsStatic: false } method &&
        (method.ReturnsByRef || method.ReturnsByRefReadonly) &&
        (method.ReducedFrom is not null) == extension;
}
