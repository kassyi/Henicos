using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The walk of <see cref="SplicedFlowBuilder"/> over the operations of a block in the order C# evaluates them: the
/// dispatch by operation kind, and the references, increments, flow captures and object creations. Assignments are
/// <see cref="SplicedAssignmentWalker"/>'s, calls <see cref="SplicedCallWalker"/>'s and bodies
/// <see cref="SplicedBodySplicer"/>'s.
/// </summary>
internal static class SplicedOperationWalker
{
    /// <summary>
    /// An operation of a block: in a <c>Gpu.SyncGrid</c> body, a split comes first when a gap lies between it and the
    /// operation before it in the same block.
    /// </summary>
    public static void WalkTop(SplicedFlowContext context, IOperation operation, ControlFlowGraph graph)
    {
        context.TopRange = context.Gaps?.RangeOf(operation.Syntax);
        if (context.TopRange is { } range && context.Gaps is { } gaps)
        {
            if (context.LastRange is { } last && gaps.HasGap(last.Last, range.First))
            {
                context.Record(
                    static (recorder, node) =>
                        recorder.OnSplit(new SplitPoint(SplitKind.Gap, null, null, null, null), node),
                    context.Current.Index
                );
            }

            context.Current.First ??= range;
        }

        Walk(context, operation, graph);
        foreach (var recorder in context.Recorders)
        {
            recorder.OnTopOperation(operation, graph, context.Current.Index);
        }

        if (context.TopRange is { } walked)
        {
            context.Current.First ??= walked;
            context.Current.Last = walked;
            context.LastRange = walked;
        }
    }

    /// <summary>The operations of <paramref name="operation"/> in the order C# evaluates them.</summary>
    public static void Walk(SplicedFlowContext context, IOperation operation, ControlFlowGraph graph)
    {
        switch (operation)
        {
            case IInvocationOperation invocation:
                SplicedCallWalker.WalkInvocation(context, invocation, graph);
                return;
            case IFlowAnonymousFunctionOperation function:
                SplicedBodySplicer.SpliceLambda(context, function, graph);
                return;
            case IDelegateCreationOperation
            {
                Target: IMethodReferenceOperation { Method.MethodKind: MethodKind.LocalFunction } reference
            }:
                SplicedBodySplicer.SpliceLocalFunction(context, reference.Method.OriginalDefinition, graph,
                    SplicedBodyKind.LocalFunctionDelegate);
                return;
            case ILocalReferenceOperation reference:
                WalkLocalReference(context, reference);
                return;
            case IParameterReferenceOperation reference:
                WalkParameterReference(context, reference);
                return;
            case IFlowCaptureReferenceOperation reference:
                WalkFlowCaptureReference(context, reference);
                return;
            case ISimpleAssignmentOperation assignment:
                SplicedAssignmentWalker.WalkSimpleAssignment(context, assignment, graph);
                return;
            case IDeconstructionAssignmentOperation deconstruction:
                SplicedAssignmentWalker.WalkDeconstruction(context, deconstruction, graph);
                return;
            case IAssignmentOperation assignment:
                SplicedAssignmentWalker.WalkCompoundAssignment(context, assignment, graph);
                return;
            case IIncrementOrDecrementOperation increment:
                WalkIncrement(context, increment, graph);
                return;
            case IFlowCaptureOperation capture:
                WalkFlowCapture(context, capture, graph);
                return;
            case IObjectCreationOperation creation:
                WalkObjectCreation(context, creation, graph);
                return;
            case IPropertyReferenceOperation property:
                WalkPropertyReference(context, property, graph);
                return;
            case IConversionOperation conversion:
                WalkConversion(context, conversion, graph);
                return;
            case ICollectionExpressionOperation collection:
                WalkCollectionExpression(context, collection, graph);
                return;
            case IBinaryOperation binary:
                WalkBinary(context, binary, graph);
                return;
            case IUnaryOperation unary:
                WalkUnary(context, unary, graph);
                return;
        }

        WalkChildren(context, operation, graph);
    }

    private static void WalkChildren(SplicedFlowContext context, IOperation operation, ControlFlowGraph graph)
    {
        foreach (var child in operation.ChildOperations)
        {
            Walk(context, child, graph);
        }
    }

    private static void WalkLocalReference(SplicedFlowContext context, ILocalReferenceOperation reference)
    {
        foreach (var recorder in context.Recorders)
        {
            recorder.OnLocalReference(reference, context.Current.Index);
        }
    }

    private static void WalkParameterReference(SplicedFlowContext context, IParameterReferenceOperation reference)
    {
        foreach (var recorder in context.Recorders)
        {
            recorder.OnParameterReference(reference, context.Current.Index);
        }
    }

    private static void WalkFlowCaptureReference(SplicedFlowContext context, IFlowCaptureReferenceOperation reference)
    {
        foreach (var recorder in context.Recorders)
        {
            recorder.OnFlowCaptureReference(reference, context.Current.Index);
        }
    }

    private static void WalkIncrement(SplicedFlowContext context, IIncrementOrDecrementOperation increment,
        ControlFlowGraph graph)
    {
        Walk(context, increment.Target, graph);
        SplicedCallWalker.RecordMethodSplitIfSuspending(context, increment.OperatorMethod, increment, graph);
        foreach (var recorder in context.Recorders)
        {
            recorder.OnIncrement(increment, graph, context.Current.Index);
        }
    }

    private static void WalkFlowCapture(SplicedFlowContext context, IFlowCaptureOperation capture,
        ControlFlowGraph graph)
    {
        if (capture.Value is IPropertyReferenceOperation property)
        {
            context.RecordCapturedProperty(graph, capture.Id, property);
        }

        Walk(context, capture.Value, graph);
        foreach (var recorder in context.Recorders)
        {
            recorder.OnFlowCapture(capture, graph, context.Current.Index);
        }
    }

    private static void WalkObjectCreation(SplicedFlowContext context, IObjectCreationOperation creation,
        ControlFlowGraph graph)
    {
        foreach (var argument in creation.Arguments)
        {
            if (!SplicedCallWalker.IsWrittenByCallee(argument))
            {
                Walk(context, argument.Value, graph);
            }
        }

        foreach (var recorder in context.Recorders)
        {
            recorder.OnObjectCreation(creation, context.Current.Index);
        }

        if (creation.Constructor is { } constructor &&
            SplicedCallWalker.GetMemberSplit(context.Model.Compilation, constructor, creation.Arguments, creation,
                graph) is { } split)
        {
            context.Record((recorder, node) => recorder.OnSplit(split, node), context.Current.Index);
        }

        foreach (var recorder in context.Recorders)
        {
            recorder.OnObjectCreationReturned(creation, graph, context.Current.Index);
        }

        if (creation.Initializer is { } initializer)
        {
            Walk(context, initializer, graph);
        }
    }

    private static void WalkPropertyReference(SplicedFlowContext context, IPropertyReferenceOperation property,
        ControlFlowGraph graph)
    {
        if (property.Instance is { } instance)
        {
            Walk(context, instance, graph);
        }

        foreach (var argument in property.Arguments)
        {
            Walk(context, argument.Value, graph);
        }

        var (reads, writes) = OperationCalls.GetAccessorUsage(property);
        var compilation = context.Model.Compilation;
        if (reads && property.Property.GetMethod is { } getter &&
            SplicedCallWalker.GetMemberSplit(compilation, getter, property.Arguments, property, graph) is
                { } readSplit)
        {
            context.Record((recorder, node) => recorder.OnSplit(readSplit, node), context.Current.Index);
        }

        if (writes && property.Property.SetMethod is { } setter &&
            SplicedCallWalker.GetMemberSplit(compilation, setter, property.Arguments, property, graph) is
                { } writeSplit)
        {
            context.Record((recorder, node) => recorder.OnSplit(writeSplit, node), context.Current.Index);
        }
    }

    private static void WalkConversion(SplicedFlowContext context, IConversionOperation conversion,
        ControlFlowGraph graph)
    {
        Walk(context, conversion.Operand, graph);
        if (conversion.OperatorMethod is { } operatorMethod &&
            SplicedCallWalker.GetMemberSplit(context.Model.Compilation, operatorMethod,
                ImmutableArray<IArgumentOperation>.Empty, conversion, graph) is { } split)
        {
            context.Record((recorder, node) => recorder.OnSplit(split, node), context.Current.Index);
        }
    }

    private static void WalkCollectionExpression(SplicedFlowContext context, ICollectionExpressionOperation collection,
        ControlFlowGraph graph)
    {
        WalkChildren(context, collection, graph);
        if (collection.ConstructMethod is { } constructMethod &&
            SplicedCallWalker.GetMemberSplit(context.Model.Compilation, constructMethod,
                ImmutableArray<IArgumentOperation>.Empty, collection, graph) is { } split)
        {
            context.Record((recorder, node) => recorder.OnSplit(split, node), context.Current.Index);
        }
    }

    private static void WalkBinary(SplicedFlowContext context, IBinaryOperation binary, ControlFlowGraph graph)
    {
        Walk(context, binary.LeftOperand, graph);
        Walk(context, binary.RightOperand, graph);
        if (binary.OperatorMethod is { } operatorMethod)
        {
            SplicedCallWalker.RecordMethodSplitIfSuspending(context, operatorMethod, binary, graph);
            if (OperatorMacroResolver.TryGetTarget(operatorMethod, context.Model.Compilation, out var target))
            {
                SplicedCallWalker.RecordMethodSplitIfSuspending(context, target, binary, graph);
            }
        }
    }

    private static void WalkUnary(SplicedFlowContext context, IUnaryOperation unary, ControlFlowGraph graph)
    {
        Walk(context, unary.Operand, graph);
        if (unary.OperatorMethod is { } operatorMethod)
        {
            var siteSyntax = GetUnarySiteSyntax(unary);
            SplicedCallWalker.RecordMethodSplitIfSuspending(context, operatorMethod, unary, graph, siteSyntax);
            if (OperatorMacroResolver.TryGetTarget(operatorMethod, context.Model.Compilation, out var target))
            {
                SplicedCallWalker.RecordMethodSplitIfSuspending(context, target, unary, graph, siteSyntax);
            }
        }
    }

    private static SyntaxNode GetUnarySiteSyntax(IUnaryOperation unary)
    {
        if (unary.OperatorMethod is { MethodKind: MethodKind.UserDefinedOperator, Name: "op_True" or "op_False" })
        {
            var current = unary.Syntax;
            while (current.Parent is ParenthesizedExpressionSyntax parenthesized)
            {
                current = parenthesized;
            }

            if (current.Parent is BinaryExpressionSyntax binary &&
                binary.IsKind(SyntaxKind.LogicalAndExpression, SyntaxKind.LogicalOrExpression) &&
                binary.Left == current)
            {
                return binary;
            }
        }

        return unary.Syntax;
    }
}
