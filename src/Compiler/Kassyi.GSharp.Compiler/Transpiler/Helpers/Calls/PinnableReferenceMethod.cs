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

    // Prefix of the local that holds the copy of a receiver (PinnableReceiverForm.Spill); only this helper spells it
    private const string SpillPrefix = "__gsharp_pin_";

    /// <summary>
    /// What <paramref name="declarator"/> of a <c>fixed</c> statement writes when its initializer is pinned through
    /// <c>GetPinnableReference</c>; null for any other declarator.
    /// </summary>
    public static PinnablePlan? Plan(VariableDeclaratorSyntax declarator, ITypeSymbol? pointerType,
        SemanticModel model)
    {
        if (declarator.Parent?.Parent is not FixedStatementSyntax ||
            declarator.Initializer?.Value is not { } receiver ||
            Find(receiver, model) is not { } method ||
            (method.ReducedFrom ?? method).Parameters is not [var receiverParameter, ..])
        {
            return null;
        }

        var form = receiverParameter.RefKind == RefKind.None
            ? PinnableReceiverForm.Value
            : RefSyntaxHelper.ClassifyStorage(receiver, model) == CppLocation.Storage
                ? PinnableReceiverForm.Address
                : PinnableReceiverForm.Spill;
        return new PinnablePlan(method, receiver, pointerType, method.ReturnsByRefReadonly, form,
            receiverParameter.Type);
    }

    /// <summary>
    /// Writes <c>&lt;type&gt; &lt;local&gt; = &lt;receiver&gt;;</c> and a line break for a plan that spills its
    /// receiver, and nothing for any other.
    /// </summary>
    public static void WriteSpill(VariableDeclaratorSyntax declarator, in PinnablePlan plan,
        ref TranspileContext context)
    {
        if (plan.Form != PinnableReceiverForm.Spill)
        {
            return;
        }

        StructOrdering.RegisterUsedStructIfApplicable(plan.ReceiverType, ref context);
        context.Emitter.Dialect.AppendType(ref context, plan.ReceiverType);
        context.Emitter.Writer.Append(' ');
        AppendSpillName(declarator, ref context);
        context.Emitter.Writer.Append(" = ");
        context.Visitor.Visit(plan.Receiver);
        context.Emitter.Writer.AppendLine(";");
    }

    /// <summary>
    /// Writes <c> = &amp;&lt;function&gt;(&lt;receiver&gt;)</c>, with the cast of a <c>ref readonly</c> result, for
    /// <paramref name="plan"/>; when the method cannot be called, reports why and writes the placeholder.
    /// </summary>
    public static void WriteInitializer(VariableDeclaratorSyntax declarator, in PinnablePlan plan,
        ref TranspileContext context)
    {
        context.Emitter.Writer.Append(" = ");
        if (!ImplicitDeviceCall.CheckCallable(plan.Receiver, plan.Method, ref context))
        {
            return;
        }

        // (<pointer type>)&<function>(<receiver>)
        if (plan.CastsToPointer)
        {
            using (context.Emitter.Writer.Parentheses())
            {
                context.Emitter.Dialect.AppendType(ref context, plan.PointerType);
            }
        }

        context.Emitter.Writer.Append('&');
        ImplicitDeviceCall.AppendName(plan.Method, ref context);
        using (context.Emitter.Writer.Parentheses())
        {
            switch (plan.Form)
            {
                case PinnableReceiverForm.Value:
                    context.Visitor.Visit(plan.Receiver);
                    break;
                case PinnableReceiverForm.Address:
                    context.Emitter.Writer.Append('&');
                    context.Visitor.Visit(plan.Receiver);
                    break;
                default:
                    context.Emitter.Writer.Append('&');
                    AppendSpillName(declarator, ref context);
                    break;
            }
        }
    }

    // <prefix><C++ name of the declared pointer>: unique among the declarators of the method
    private static void AppendSpillName(VariableDeclaratorSyntax declarator, ref TranspileContext context)
    {
        context.Emitter.Writer.Append(SpillPrefix);
        CppNameResolver.AppendDeclaredName(declarator, declarator.Identifier, ref context);
    }

    // A parameterless method that returns by reference: an instance method, or the reduced form of an extension method
    private static bool IsPinnable(ISymbol symbol, bool extension) =>
        symbol is IMethodSymbol { Parameters.Length: 0, IsStatic: false } method &&
        (method.ReturnsByRef || method.ReturnsByRefReadonly) &&
        (method.ReducedFrom is not null) == extension;
}
