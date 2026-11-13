using System.Buffers;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Writes a deconstruction assignment (<c>(a, (b, c)) = (x, (y, z))</c>, <c>var (p, q) = f ? (x, y) : (y, x)</c>) and
/// an assignment to a discard (<c>_ = x</c>).
/// </summary>
/// <remarks>
/// <para>
/// C# finds the locations of the targets from left to right, then evaluates the right side (only one branch of a
/// conditional), then assigns the elements from left to right (C# spec §12.21.2, #996, #1102). Nested tuples on both
/// sides are flattened into one list of targets and one list of values in source order; the values are evaluated into
/// <c>__gsharp_tuple_&lt;i&gt;</c> inside an immediately invoked lambda, so that a swap reads every value before it
/// writes. A conditional value declares the temporaries of its elements with the types of its tuple type and assigns
/// them in an <c>if</c>:
/// </para>
/// <code>
/// (a, b) = f ? (b, a) : (a, b);
/// ([&amp;]() { int __gsharp_tuple_0{}; int __gsharp_tuple_1{};
///   if (f) { __gsharp_tuple_0 = b; __gsharp_tuple_1 = a; } else { __gsharp_tuple_0 = a; __gsharp_tuple_1 = b; }
///   a = __gsharp_tuple_0; b = __gsharp_tuple_1; }());
/// </code>
/// <para>
/// A target whose location evaluation writes something, reads a variable that an earlier target writes, or interferes
/// with another target or a value is bound to a reference first. A discard keeps only the evaluation of its value
/// (#1289); the variables of a deconstruction declaration are declared before the statement
/// (<see cref="StatementPrelude"/>).
/// </para>
/// </remarks>
internal static class DeconstructionAssignment
{
    /// <summary>The GSH0003 text for a deconstruction whose right side has no tuple literal to take the elements from.</summary>
    public const string ValueNotATuple =
        "deconstruction of a value that is not a tuple literal or a conditional of tuple literals";

    /// <summary>The GSH0003 text for a deconstruction assignment whose value (a tuple) is used.</summary>
    public const string ValueUsed = "deconstruction assignment whose value is used";

    /// <summary>The GSH0003 text for a conditional whose element type C++ cannot declare before the branches.</summary>
    public const string UndeclarableElementType = "deconstruction of a conditional whose element type is generic";

    // A target of the flattened left side: an expression or a designation that names the location, or null for a discard
    private readonly record struct Target(SyntaxNode? Node, ExpressionEffects Location, VariableSet Root);

    // The right side, paired with the shape of the left side
    private abstract record Value;

    private sealed record LeafValue(ExpressionSyntax Expression, int TargetIndex) : Value;

    private sealed record TupleValue(ImmutableArray<Value> Elements) : Value;

    // The types of the targets FirstTarget.. that both branches assign, from the tuple type of the conditional (default
    // when it has none)
    private sealed record ConditionalValue(ExpressionSyntax Condition, Value WhenTrue, Value WhenFalse,
        ImmutableArray<ITypeSymbol> ElementTypes, int FirstTarget) : Value;

    // The bound temporary of a computed property target is its receiver's, which the store writes by visiting it
    private const int ReceiverBound = int.MaxValue;

    private readonly record struct Plan(ImmutableArray<Target> Targets, Value Value);

    /// <summary>Whether <paramref name="node"/> is a deconstruction (its left side is a tuple or a <c>var (...)</c>).</summary>
    public static bool IsDeconstruction(AssignmentExpressionSyntax node) =>
        node.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
        node.Left is TupleExpressionSyntax or
            DeclarationExpressionSyntax { Designation: ParenthesizedVariableDesignationSyntax };

    /// <summary>
    /// Writes the assignment <paramref name="node"/> to a discard as its right side: <c>(void)(&lt;right&gt;)</c> as a
    /// statement, <c>(&lt;right&gt;)</c> where its value is used.
    /// </summary>
    public static void AppendDiscardedValue(AssignmentExpressionSyntax node, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        writer.AppendIf(!IntegerSemanticsEmitter.IsValueUsed(node, context.Query.SemanticModel), "(void)");
        using (writer.Parentheses())
        {
            context.Visitor.Visit(node.Right);
        }
    }

    /// <summary>Writes the deconstruction <paramref name="node"/>, or reports why it cannot be written.</summary>
    public static void Transpile(AssignmentExpressionSyntax node, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        if (IntegerSemanticsEmitter.IsValueUsed(node, model))
        {
            Fail(node, ValueUsed, ref context);
            return;
        }

        var targets = ImmutableArray.CreateBuilder<Target>();
        var next = 0;
        if (CreateValue(node.Left, node.Right, targets, model, ref next) is not { } value)
        {
            Fail(node.Right, ValueNotATuple, ref context);
            return;
        }

        if (FindUndeclarableType(value) is { } conditional)
        {
            Fail(conditional, UndeclarableElementType, ref context);
            return;
        }

        Emit(new Plan(targets.ToImmutable(), value), ref context);
    }

    // Pairs the shape of the left side with the right side; null when a nested left side meets a value that is not a
    // tuple literal or a conditional. The targets are numbered from <next> in source order and added to <targets> the
    // first time (the second branch of a conditional assigns the same ones; <targets> is then null)
    private static Value? CreateValue(SyntaxNode left, ExpressionSyntax right, ImmutableArray<Target>.Builder? targets,
        SemanticModel model, ref int next)
    {
        var shape = GetShape(left);
        if (shape.IsDefault)
        {
            targets?.Add(CreateTarget(left, model));
            return new LeafValue(right, next++);
        }

        right = IntegerSemanticsEmitter.Unparenthesize(right);
        switch (right)
        {
            case TupleExpressionSyntax tuple when tuple.Arguments.Count == shape.Length:
            {
                var elements = ImmutableArray.CreateBuilder<Value>(shape.Length);
                for (var i = 0; i < shape.Length; i++)
                {
                    if (CreateValue(shape[i], tuple.Arguments[i].Expression, targets, model, ref next) is not
                        { } element)
                    {
                        return null;
                    }

                    elements.Add(element);
                }

                return new TupleValue(elements.MoveToImmutable());
            }
            case ConditionalExpressionSyntax conditional:
            {
                var firstTarget = next;
                if (CreateValue(left, conditional.WhenTrue, targets, model, ref next) is not { } whenTrue)
                {
                    return null;
                }

                var end = next;
                next = firstTarget;
                if (CreateValue(left, conditional.WhenFalse, null, model, ref next) is not { } whenFalse)
                {
                    return null;
                }

                var types = ImmutableArray.CreateBuilder<ITypeSymbol>(end - firstTarget);
                AddElementTypes(left, model.GetTypeSymbol(conditional) ?? model.GetConvertedTypeSymbol(conditional),
                    types);
                return new ConditionalValue(conditional.Condition, whenTrue, whenFalse,
                    types.Count == end - firstTarget ? types.MoveToImmutable() : default, firstTarget);
            }
            default:
                return null;
        }
    }

    // The elements of a nested left side, or default for a single target
    private static ImmutableArray<SyntaxNode> GetShape(SyntaxNode left)
    {
        switch (left)
        {
            case TupleExpressionSyntax tuple:
            {
                var elements = ImmutableArray.CreateBuilder<SyntaxNode>(tuple.Arguments.Count);
                foreach (var argument in tuple.Arguments)
                {
                    elements.Add(argument.Expression);
                }

                return elements.MoveToImmutable();
            }
            case DeclarationExpressionSyntax { Designation: ParenthesizedVariableDesignationSyntax designation }:
                return GetShape(designation);
            case ParenthesizedVariableDesignationSyntax designation:
            {
                var elements = ImmutableArray.CreateBuilder<SyntaxNode>(designation.Variables.Count);
                foreach (var variable in designation.Variables)
                {
                    elements.Add(variable);
                }

                return elements.MoveToImmutable();
            }
            default:
                return default;
        }
    }

    private static Target CreateTarget(SyntaxNode node, SemanticModel model) => node switch
    {
        DiscardDesignationSyntax => default,
        ExpressionSyntax expression when model.IsDiscard(expression) => default,
        // A variable declared by the deconstruction: its location is found without evaluating anything
        SingleVariableDesignationSyntax or DeclarationExpressionSyntax => new Target(node, default, default),
        // C# names every target location (an array element with its null and bounds check, ldelema) before it evaluates
        // the values, unlike a simple assignment, which checks its element when it stores
        ExpressionSyntax expression => new Target(expression, ExpressionEffects.OfLocation(expression, model),
            ExpressionEffects.RootOf(expression, model)),
        _ => default
    };

    // The element types of a tuple type, flattened as the left side is nested
    private static void AddElementTypes(SyntaxNode left, ITypeSymbol? type, ImmutableArray<ITypeSymbol>.Builder types)
    {
        var shape = GetShape(left);
        if (shape.IsDefault)
        {
            if (type != null)
            {
                types.Add(type);
            }

            return;
        }

        if (type is not INamedTypeSymbol { IsTupleType: true, TupleElements: var elements } ||
            elements.Length != shape.Length)
        {
            return;
        }

        for (var i = 0; i < shape.Length; i++)
        {
            AddElementTypes(shape[i], elements[i].Type, types);
        }
    }

    // The first conditional whose element temporaries C++ cannot declare before its branches
    private static ExpressionSyntax? FindUndeclarableType(Value value)
    {
        switch (value)
        {
            case TupleValue tuple:
                foreach (var element in tuple.Elements)
                {
                    if (FindUndeclarableType(element) is { } found)
                    {
                        return found;
                    }
                }

                return null;
            case ConditionalValue conditional:
                if (conditional.ElementTypes.IsDefault ||
                    !conditional.ElementTypes
                        .AsStackEnumerable()
                        .All(static type => TypeMapping.HasDirectCppType(type)))
                {
                    return conditional.Condition.Parent as ExpressionSyntax;
                }

                return FindUndeclarableType(conditional.WhenTrue) ?? FindUndeclarableType(conditional.WhenFalse);
            default:
                return null;
        }
    }

    private static void Emit(in Plan plan, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        var targets = plan.Targets;
        var values = ImmutableArray.CreateBuilder<ExpressionEffects>();
        AddValueEffects(plan.Value, model, values);
        var boundTargets = ArrayPool<int>.Shared.Rent(targets.Length);
        var spilled = context.Emitter.SpilledExpressions;
        try
        {
            boundTargets.AsSpan(0, targets.Length).Fill(-1);
            using (context.Emitter.Writer.ImmediatelyInvokedLambda())
            {
                for (var i = 0; i < targets.Length; i++)
                {
                    boundTargets[i] = targets[i].Node is ExpressionSyntax target && NeedsBinding(i, targets, values)
                        ? BindTarget(target, ref context)
                        : -1;
                }

                EmitValue(plan.Value, plan, declared: false, ref context);
                for (var i = 0; i < targets.Length; i++)
                {
                    EmitStore(targets[i].Node, boundTargets[i], i, ref context);
                }
            }
        }
        finally
        {
            for (var i = 0; i < targets.Length; i++)
            {
                if (boundTargets[i] == ReceiverBound &&
                    targets[i].Node is ExpressionSyntax target &&
                    IntegerSemanticsEmitter.Unparenthesize(target) is MemberAccessExpressionSyntax
                    {
                        Expression: var receiver
                    })
                {
                    spilled.Remove(receiver);
                }
            }

            ArrayPool<int>.Shared.Return(boundTargets);
        }
    }

    // A target is bound to a reference (auto&& __gsharp_seq_<n> = <target>); a property whose setter is called cannot
    // be, so its
    // receiver is, and the setter is called on it. Returns the temporary, or ReceiverBound for a receiver (the store
    // visits the target, which writes the receiver's temporary)
    private static int BindTarget(ExpressionSyntax target, ref TranspileContext context)
    {
        if (!PropertyAccessorCall.CallsSetter(target, ref context))
        {
            return EvaluationOrder.DeclareTemp(ref context, target, reference: true);
        }

        if (IntegerSemanticsEmitter.Unparenthesize(target) is MemberAccessExpressionSyntax { Expression: var receiver })
        {
            context.Emitter.SpilledExpressions[receiver] =
                EvaluationOrder.DeclareTemp(ref context, receiver, reference: true);
        }

        return ReceiverBound;
    }

    // <target> = __gsharp_tuple_<i>; (<target>(__gsharp_tuple_<i>); for a setter call); nothing for a discard
    private static void EmitStore(SyntaxNode? target, int bound, int index, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        if (target == null)
        {
            return;
        }

        if (target is ExpressionSyntax expression &&
            PropertyAccessorCall.CallsSetter(expression, ref context))
        {
            context.Visitor.Visit(expression);
            using (writer.Parentheses())
            {
                writer.Append($"{GSharpInternalNames.Lowering.TupleTempPrefix}{index}");
            }

            writer.AppendLine(";");
            return;
        }

        if (bound >= 0)
        {
            EvaluationOrder.AppendTempName(writer, bound);
        }
        else
        {
            context.Visitor.Visit(target);
        }

        writer
            .Append($" = {GSharpInternalNames.Lowering.TupleTempPrefix}{index}")
            .AppendLine(";");
    }

    // auto __gsharp_tuple_<i> = <value>; in order, (void)(<value>); for a discard, and the if of a conditional
    private static void EmitValue(Value value, in Plan plan, bool declared, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        switch (value)
        {
            case LeafValue leaf:
                if (plan.Targets[leaf.TargetIndex].Node == null)
                {
                    writer.Append("(void)");
                    using (writer.Parentheses())
                    {
                        context.Visitor.Visit(leaf.Expression);
                    }
                }
                else
                {
                    writer.Append(declared
                        ? $"{GSharpInternalNames.Lowering.TupleTempPrefix}{leaf.TargetIndex} = "
                        : $"auto {GSharpInternalNames.Lowering.TupleTempPrefix}{leaf.TargetIndex} = ");
                    context.Visitor.Visit(leaf.Expression);
                }

                writer.AppendLine(";");
                return;
            case TupleValue tuple:
                foreach (var element in tuple.Elements)
                {
                    EmitValue(element, plan, declared, ref context);
                }

                return;
            case ConditionalValue conditional:
                if (!declared)
                {
                    DeclareElements(conditional, plan, ref context);
                }

                writer.Append("if");
                using (writer.Parentheses())
                {
                    context.Visitor.Visit(conditional.Condition);
                }

                writer.AppendLine();
                using (writer.Scope())
                {
                    EmitValue(conditional.WhenTrue, plan, declared: true, ref context);
                }

                writer.AppendLine("else");
                using (writer.Scope())
                {
                    EmitValue(conditional.WhenFalse, plan, declared: true, ref context);
                }

                return;
        }
    }

    // <T> __gsharp_tuple_<i>{}; for each target that the conditional assigns
    private static void DeclareElements(ConditionalValue conditional, in Plan plan, ref TranspileContext context)
    {
        for (var k = 0; k < conditional.ElementTypes.Length; k++)
        {
            var index = conditional.FirstTarget + k;
            if (plan.Targets[index].Node == null)
            {
                continue;
            }

            context.Emitter.Dialect.AppendType(ref context, conditional.ElementTypes[k]);
            context.Emitter.Writer.AppendLine($" {GSharpInternalNames.Lowering.TupleTempPrefix}{index}{{}};");
        }
    }

    // The effects of every value and condition, in evaluation order
    private static void AddValueEffects(Value value, SemanticModel model, ImmutableArray<ExpressionEffects>.Builder effects)
    {
        switch (value)
        {
            case LeafValue leaf:
                effects.Add(ExpressionEffects.Of(leaf.Expression, model));
                return;
            case TupleValue tuple:
                foreach (var element in tuple.Elements)
                {
                    AddValueEffects(element, model, effects);
                }

                return;
            case ConditionalValue conditional:
                effects.Add(ExpressionEffects.Of(conditional.Condition, model));
                AddValueEffects(conditional.WhenTrue, model, effects);
                AddValueEffects(conditional.WhenFalse, model, effects);
                return;
        }
    }

    private static bool NeedsBinding(int index, ImmutableArray<Target> targets,
        ImmutableArray<ExpressionEffects>.Builder values)
    {
        var current = targets[index].Location;
        if (current.IsEmpty)
        {
            return false;
        }

        if (!current.Writes.IsEmpty)
        {
            return true;
        }

        for (var j = 0; j < index; j++)
        {
            if (current.Reads.Overlaps(targets[j].Root))
            {
                return true;
            }
        }

        for (var j = 0; j < targets.Length; j++)
        {
            if (j != index && current.InterferesWith(targets[j].Location))
            {
                return true;
            }
        }

        foreach (var value in values)
        {
            if (current.InterferesWith(value))
            {
                return true;
            }
        }

        return false;
    }

    private static void Fail(SyntaxNode node, string message, ref TranspileContext context)
    {
        context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, message);
        context.Visitor.AppendUnsupportedPlaceholder(node);
    }
}
