namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Calls;

/// <summary>
/// Plans and emits <c>GetPinnableReference</c> calls for <c>fixed (T* p = e)</c>
/// (<see cref="PinnableReferenceLookup.Find"/>).
/// </summary>
internal static class PinnableReferenceMethod
{
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
            PinnableReferenceLookup.Find(receiver, model) is not { } method)
        {
            return null;
        }

        // An instance method has the receiver as its own; an extension method takes it as its first parameter
        var receiverParameter = method.ReducedFrom?.Parameters is [var first, ..] ? first : null;
        var form = receiverParameter is null or { RefKind: RefKind.None }
            ? PinnableReceiverForm.Value
            : RefSyntaxHelper.ClassifyStorage(receiver, model) == CppLocation.Storage
                ? PinnableReceiverForm.Address
                : PinnableReceiverForm.Spill;
        return new PinnablePlan(method, receiver, pointerType, method.ReturnsByRefReadonly, form,
            receiverParameter?.Type ?? method.ContainingType);
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
}
