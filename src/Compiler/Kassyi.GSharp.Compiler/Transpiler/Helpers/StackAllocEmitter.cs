namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Writes a <c>stackalloc</c> or collection expression outside the declaration of a stack array local
/// (<see cref="StackArrayClassifier"/>). C++ cannot declare a named array inside an expression, so the statement prelude
/// declares an uninitialized storage <c>__gsharp_stackalloc_&lt;n&gt;</c> before the statement, and the expression fills it
/// where C# evaluates it: every evaluation zeroes or initializes it again, and the elements are evaluated in their C# order
/// among the other operands.
/// </summary>
internal static class StackAllocEmitter
{
    // Only this emitter spells the storage names
    private const string StoragePrefix = "__gsharp_stackalloc_";

    // <element type> __gsharp_stackalloc_<n>[<length>];
    public static void DeclareStorage(SyntaxNode node, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        if (!StackArrayClassifier.NeedsStorage(node, model) ||
            !StackArrayClassifier.TryClassify(node, model, out var allocation, out _, out _) ||
            allocation.Length == 0)
        {
            return;
        }

        var number = ++context.Emitter.StackAllocCounter;
        context.Emitter.StackAllocStorages[node] = number;
        StructTranspiler.RegisterUsedStructIfApplicable(allocation.ElementType, ref context);
        context.Emitter.Dialect.AppendType(ref context, allocation.ElementType);
        context.Emitter.Writer.AppendLine($" {StoragePrefix}{number}[{allocation.Length}];");
    }

    /// <summary>
    /// Writes the value of <paramref name="node"/>; false for a collection expression that needs no storage, which the
    /// caller writes as an initializer list.
    /// </summary>
    /// <remarks>
    /// <code>
    /// Len(stackalloc int[4])            ->  Len(__gsharp_stackalloc_zero(__gsharp_stackalloc_0))
    /// Sum(stackalloc int[] { A(), B() })  ->  Sum((__gsharp_stackalloc_1[0]=A(),__gsharp_stackalloc_1[1]=B(),__gsharp_stackalloc_span(__gsharp_stackalloc_1)))
    /// Len(stackalloc int[0])            ->  Len(GSharpArray&lt;int&gt;())     (nullptr for a pointer)
    /// </code>
    /// Under <c>[SkipLocalsInit]</c> a <c>stackalloc</c> without an initializer is not zeroed:
    /// <c>__gsharp_stackalloc_span(storage)</c>.
    /// </remarks>
    public static bool TryEmit(SyntaxNode node, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        if (node is CollectionExpressionSyntax && !StackArrayClassifier.NeedsStorage(node, model))
        {
            return false;
        }

        if (!StackArrayClassifier.TryClassify(node, model, out var allocation, out var problemNode, out var problem))
        {
            Fail(node, problemNode, problem, ref context);
            return true;
        }

        if (allocation.Length == 0)
        {
            if (allocation.IsPointer)
            {
                context.Emitter.Writer.Append("nullptr");
                return true;
            }

            context.Emitter.Writer.Append("GSharpArray<");
            context.Emitter.Dialect.AppendType(ref context, allocation.ElementType);
            context.Emitter.Writer.Append(">()");
            return true;
        }

        if (!context.Emitter.StackAllocStorages.TryGetValue(node, out var number))
        {
            Fail(node, node, StackArrayClassifier.OutsideStatement, ref context);
            return true;
        }

        if (allocation.Elements is { } elements)
        {
            // (<storage>[0]=<e0>,...,__gsharp_stackalloc_span(<storage>)): a comma sequences the elements in C# order,
            // and an assignment converts each element as C# does (a braced list would reject a narrowing conversion)
            using (context.Emitter.Writer.Parentheses())
            {
                if (elements is InitializerExpressionSyntax initializer)
                {
                    for (var i = 0; i < initializer.Expressions.Count; i++)
                    {
                        AppendElement(number, i, initializer.Expressions[i], ref context);
                    }
                }
                else
                {
                    // TryClassify leaves only expression elements
                    var collection = (CollectionExpressionSyntax)elements;
                    for (var i = 0; i < collection.Elements.Count; i++)
                    {
                        if (collection.Elements[i] is ExpressionElementSyntax element)
                        {
                            AppendElement(number, i, element.Expression, ref context);
                        }
                    }
                }

                context.Emitter.Writer.Append($"{GSharpInternalNames.Prelude.StackAllocSpan}({StoragePrefix}{number})");
            }

            return true;
        }

        var view = model.AreLocalsZeroedAt(node)
            ? GSharpInternalNames.Prelude.StackAllocZero
            : GSharpInternalNames.Prelude.StackAllocSpan;
        context.Emitter.Writer.Append($"{view}({StoragePrefix}{number})");
        return true;
    }

    // <storage>[<index>]=<element>,
    private static void AppendElement(int number, int index, ExpressionSyntax element, ref TranspileContext context)
    {
        context.Emitter.Writer.Append($"{StoragePrefix}{number}[{index}]=");
        context.Visitor.Visit(element);
        context.Emitter.Writer.Append(',');
    }

    private static void Fail(SyntaxNode node, SyntaxNode problemNode, string problem, ref TranspileContext context)
    {
        context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, problemNode, problem);
        context.Visitor.AppendUnsupportedPlaceholder(node);
    }
}
