namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;

/// <summary>
/// Transpiles Kassyi.ZeroAlloc.Linq method invocations into their corresponding C++ Prelude equivalents.
/// </summary>
public sealed class StackLinqTranspilerRule : IInvocationTranspilerRule
{
    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        if (!StackLinqHelper.IsStackLinqMethod(symbol))
        {
            return false;
        }

        var methodName = symbol.Name;

        // Detect indexed overloads and map to WhereAt / SelectAt / CountAt
        if (methodName is "Where" or "Select" or "Count")
        {
            var lastParam = symbol.Parameters.Length > 0 ? symbol.Parameters[^1] : null;
            if (lastParam?.Type is INamedTypeSymbol { DelegateInvokeMethod.Parameters.Length: 2 })
            {
                methodName += "At";
            }
        }

        uint siteId1 = 0;
        uint siteId2 = 0;
        if (context.Emitter.ErrorSites != null)
        {
            var model = context.Query.SemanticModel;
            var memberName = context.Emitter.CurrentMemberName ?? symbol.Name;
            switch (methodName)
            {
                case "ElementAt":
                    siteId1 = context.Emitter.ErrorSites.Register(node, model, memberName, "Index was out of range.", "ElementAt");
                    break;
                case "First":
                    var isPredicate = symbol.Parameters.Length > (symbol.IsExtensionMethod ? 1 : 0);
                    var firstMsg = isPredicate ? "Sequence contains no matching element" : "Sequence contains no elements";
                    siteId1 = context.Emitter.ErrorSites.Register(node, model, memberName, firstMsg, isPredicate ? "First.Predicate" : "First");
                    break;
                case "Last":
                    siteId1 = context.Emitter.ErrorSites.Register(node, model, memberName, "Sequence contains no elements", "Last");
                    break;
                case "Aggregate" when !HasSeedParameter(symbol):
                    siteId1 = context.Emitter.ErrorSites.Register(node, model, memberName, "Sequence contains no elements", "Aggregate");
                    break;
                case "Max":
                    siteId1 = context.Emitter.ErrorSites.Register(node, model, memberName, "Sequence contains no elements", "Max");
                    break;
                case "Min":
                    siteId1 = context.Emitter.ErrorSites.Register(node, model, memberName, "Sequence contains no elements", "Min");
                    break;
                case "Average":
                    siteId1 = context.Emitter.ErrorSites.Register(node, model, memberName, "Sequence contains no elements", "Average");
                    break;
                case "Single":
                    var isSinglePred = symbol.Parameters.Length > (symbol.IsExtensionMethod ? 1 : 0);
                    if (isSinglePred)
                    {
                        siteId1 = context.Emitter.ErrorSites.Register(node, model, memberName, "Sequence contains more than one matching element", "Single.MoreThanOneMatching");
                        siteId2 = context.Emitter.ErrorSites.Register(node, model, memberName, "Sequence contains no matching element", "Single.NoMatching");
                    }
                    else
                    {
                        siteId1 = context.Emitter.ErrorSites.Register(node, model, memberName, "Sequence contains no elements", "Single.Empty");
                        siteId2 = context.Emitter.ErrorSites.Register(node, model, memberName, "Sequence contains more than one element", "Single.MoreThanOne");
                    }
                    break;
                case "SingleOrDefault":
                    var isSingleOrDefaultPred = symbol.Parameters.Length > (symbol.IsExtensionMethod ? 1 : 0);
                    var singleOrDefaultMsg = isSingleOrDefaultPred ? "Sequence contains more than one matching element" : "Sequence contains more than one element";
                    siteId1 = context.Emitter.ErrorSites.Register(node, model, memberName, singleOrDefaultMsg, isSingleOrDefaultPred ? "SingleOrDefault.MoreThanOneMatching" : "SingleOrDefault.MoreThanOne");
                    break;
            }
        }

        // Output the C++ namespace for our StackLinq implementation
        context.Emitter.Writer.Append("Kassyi::ZeroAlloc::Linq::StackLinq::");
        context.Emitter.Writer.Append(methodName);

        using (context.Emitter.Writer.Parentheses())
        {
            var arguments = node.ArgumentList.Arguments;
            switch (symbol.IsExtensionMethod)
            {
                // If this is an extension method call via instance.Method(), the instance is the first argument
                case true when node.Expression is MemberAccessExpressionSyntax memberAccess:
                {
                    // The source template deduces GSharpArray<T>, which a stack array does not convert to
                    StackArrayLocals.AppendView(memberAccess.Expression, ref context);

                    if (arguments.Count > 0)
                    {
                        context.Emitter.Writer.Append(',');
                    }

                    break;
                }
                case true:
                    // Called as a normal static method, first arg is the instance, which is handled normally below
                    break;
            }

            for (var i = 0; i < arguments.Count; i++)
            {
                if (StackArrayLocals.IsStackArrayReference(arguments[i].Expression, context.Query.SemanticModel))
                {
                    StackArrayLocals.AppendView(arguments[i].Expression, ref context);
                }
                else
                {
                    context.Visitor.Visit(arguments[i]);
                }

                if (i < arguments.Count - 1)
                {
                    context.Emitter.Writer.Append(',');
                }
            }

            if (siteId1 != 0)
            {
                if (arguments.Count > 0 || (symbol.IsExtensionMethod && node.Expression is MemberAccessExpressionSyntax))
                {
                    context.Emitter.Writer.Append(',');
                }
                context.Emitter.Writer.Append($"{siteId1}u");
                if (siteId2 != 0)
                {
                    context.Emitter.Writer.Append($",{siteId2}u");
                }
            }
        }

        return true;
    }

    private static bool HasSeedParameter(IMethodSymbol symbol)
    {
        for (var i = 0; i < symbol.Parameters.Length; i++)
        {
            if (symbol.Parameters[i].Name == "seed")
            {
                return true;
            }
        }
        return false;
    }
}