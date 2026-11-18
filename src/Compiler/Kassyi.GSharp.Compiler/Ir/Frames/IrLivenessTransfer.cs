using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Linear;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir.Frames;

/// <summary>Computes gen/kill <see cref="IrTransfer"/> functions for IR statements and expressions.</summary>
internal static class IrLivenessTransfer
{
    /// <summary>The transfer function of evaluating <paramref name="expression"/> for its value.</summary>
    public static IrTransfer OfValue(IrExpression expression, IReadOnlyDictionary<IrVariable, int> indices,
        int variableCount) => expression switch
    {
        IrConstant or IrDefaultValue or IrDiscard or IrInitializedObject => IrTransfer.Empty(variableCount),
        IrVariableReference reference => ReadVariable(reference.Variable, indices, variableCount),
        IrFieldReference or IrElementReference => OfLocation(expression, indices, variableCount),
        IrPropertyReference property => OfPropertyReference(property, indices, variableCount),
        IrCall call => OfCall(call, includeOutWrites: true, indices, variableCount),
        IrRuntimeCall runtime => OfRuntimeCall(runtime, indices, variableCount),
        IrUnary unary => OfValue(unary.Operand, indices, variableCount),
        IrBinary binary => IrTransfer.Then(
            OfValue(binary.Left, indices, variableCount),
            OfValue(binary.Right, indices, variableCount)),
        IrConversion conversion => OfValue(conversion.Operand, indices, variableCount),
        IrNullTest test => OfValue(test.Operand, indices, variableCount),
        IrLogical logical => IrTransfer.Either(
            OfValue(logical.Left, indices, variableCount),
            OfValue(logical.Right, indices, variableCount),
            IrTransfer.Empty(variableCount)),
        IrConditional conditional => OfConditional(conditional, indices, variableCount),
        IrCoalesce coalesce => IrTransfer.Either(
            OfValue(coalesce.Value, indices, variableCount),
            OfValue(coalesce.WhenNull, indices, variableCount),
            IrTransfer.Empty(variableCount)),
        IrAssignment assignment => OfAssignment(assignment, indices, variableCount),
        IrCompoundAssignment compound => OfUpdate(compound.Target, compound.Value, indices, variableCount),
        IrIncrement increment => OfUpdate(increment.Target, null, indices, variableCount),
        IrAddressOf address => OfLocation(address.Operand, indices, variableCount),
        IrStructCreation creation => OfStructCreation(creation, indices, variableCount),
        IrArrayCreation array => OfArrayCreation(array, indices, variableCount),
        IrOpaqueExpression => throw Unsupported(expression),
        IrSequence => throw Unsupported(expression),
        _ => throw Unsupported(expression)
    };

    /// <summary>The transfer function of evaluating <paramref name="expression"/> as a read location.</summary>
    public static IrTransfer OfLocation(IrExpression expression, IReadOnlyDictionary<IrVariable, int> indices,
        int variableCount) => expression switch
    {
        IrDiscard => IrTransfer.Empty(variableCount),
        IrVariableReference reference => ReadVariable(reference.Variable, indices, variableCount),
        IrFieldReference field => OfFieldLocation(field, readRoot: true, indices, variableCount),
        IrElementReference element => IrTransfer.Then(
            OfValue(element.Receiver, indices, variableCount),
            OfValue(element.Index, indices, variableCount)),
        IrPropertyReference property => OfPropertyReference(property, indices, variableCount),
        IrConditional { IsRef: true } conditional => IrTransfer.Either(
            OfValue(conditional.Condition, indices, variableCount),
            OfLocation(conditional.WhenTrue, indices, variableCount),
            OfLocation(conditional.WhenFalse, indices, variableCount)),
        _ => OfValue(expression, indices, variableCount)
    };

    /// <summary>The transfer function of executing <paramref name="statement"/>.</summary>
    public static IrTransfer OfStatement(IrStatement statement, IReadOnlyDictionary<IrVariable, int> indices,
        int variableCount) => statement switch
    {
        IrLocalDeclaration declaration => OfLocalDeclaration(declaration, indices, variableCount),
        IrExpressionStatement exprStmt => OfValue(exprStmt.Expression, indices, variableCount),
        IrReturn ret => ret.Value is { } value
            ? OfValue(value, indices, variableCount)
            : IrTransfer.Empty(variableCount),
        IrBlock block => OfBlock(block, indices, variableCount),
        IrIf @if => IrTransfer.Either(
            OfValue(@if.Condition, indices, variableCount),
            OfStatement(@if.Then, indices, variableCount),
            @if.Else is { } @else ? OfStatement(@else, indices, variableCount) : IrTransfer.Empty(variableCount)),
        IrWhile @while => IrTransfer.Loop(
            OfValue(@while.Condition, indices, variableCount),
            OfStatement(@while.Body, indices, variableCount)),
        IrStateJump jump => jump.Condition is { } cond
            ? OfValue(cond, indices, variableCount)
            : IrTransfer.Empty(variableCount),
        IrSuspend => IrTransfer.Empty(variableCount),
        IrStateMachineExit => IrTransfer.Empty(variableCount),
        IrResumeCall resume => CombineResumeCall(resume, indices, variableCount),
        IrLabel => throw Unsupported(statement),
        IrGoto => throw Unsupported(statement),
        IrConditionalGoto => throw Unsupported(statement),
        IrOpaqueStatement => throw Unsupported(statement),
        _ => throw Unsupported(statement)
    };

    /// <summary>
    /// The split transfer of <paramref name="resume"/>: <c>Read</c> (call receiver and arguments without
    /// <see langword="out"/> writes) and <c>Write</c> (<see langword="out"/> writes and <c>ResultTarget</c> write).
    /// </summary>
    public static (IrTransfer Read, IrTransfer Write) OfResumeCall(IrResumeCall resume,
        IReadOnlyDictionary<IrVariable, int> indices, int variableCount)
    {
        var read = OfCall(resume.Call, includeOutWrites: false, indices, variableCount);
        var write = OfCallOutWrites(resume.Call, indices, variableCount);
        if (resume.ResultTarget is { } target)
        {
            write = IrTransfer.Then(write, WriteVariable(target.Variable, indices, variableCount));
        }

        return (read, write);
    }

    private static IrTransfer CombineResumeCall(IrResumeCall resume, IReadOnlyDictionary<IrVariable, int> indices,
        int variableCount)
    {
        var (read, write) = OfResumeCall(resume, indices, variableCount);
        return IrTransfer.Then(read, write);
    }

    private static IrTransfer OfLocalDeclaration(IrLocalDeclaration declaration,
        IReadOnlyDictionary<IrVariable, int> indices, int variableCount)
    {
        var transfer = declaration.Initializer switch
        {
            null => IrTransfer.Empty(variableCount),
            var init when declaration.Variable.RefKind != RefKind.None => OfLocation(init, indices, variableCount),
            var init => OfValue(init, indices, variableCount)
        };
        return IrTransfer.Then(transfer, WriteVariable(declaration.Variable, indices, variableCount));
    }

    private static IrTransfer OfBlock(IrBlock block, IReadOnlyDictionary<IrVariable, int> indices, int variableCount)
    {
        var transfer = IrTransfer.Empty(variableCount);
        foreach (var statement in block.Statements)
        {
            transfer = IrTransfer.Then(transfer, OfStatement(statement, indices, variableCount));
        }

        return transfer;
    }

    private static IrTransfer OfConditional(IrConditional conditional, IReadOnlyDictionary<IrVariable, int> indices,
        int variableCount)
    {
        var cond = OfValue(conditional.Condition, indices, variableCount);
        var whenTrue = conditional.IsRef
            ? OfLocation(conditional.WhenTrue, indices, variableCount)
            : OfValue(conditional.WhenTrue, indices, variableCount);
        var whenFalse = conditional.IsRef
            ? OfLocation(conditional.WhenFalse, indices, variableCount)
            : OfValue(conditional.WhenFalse, indices, variableCount);
        return IrTransfer.Either(cond, whenTrue, whenFalse);
    }

    private static IrTransfer OfAssignment(IrAssignment assignment, IReadOnlyDictionary<IrVariable, int> indices,
        int variableCount)
    {
        var part = OfLocationPart(assignment.Target, indices, variableCount);
        var value = assignment.IsRef
            ? OfLocation(assignment.Value, indices, variableCount)
            : OfValue(assignment.Value, indices, variableCount);
        var write = WriteTarget(assignment.Target, assignment.IsRef, indices, variableCount);
        return IrTransfer.Then(part, IrTransfer.Then(value, write));
    }

    private static IrTransfer OfUpdate(IrExpression target, IrExpression? value,
        IReadOnlyDictionary<IrVariable, int> indices, int variableCount)
    {
        var part = OfLocationPart(target, indices, variableCount);
        var readRoot = ReadLocationRoot(target, indices, variableCount);
        var transfer = IrTransfer.Then(part, readRoot);
        if (value is not null)
        {
            transfer = IrTransfer.Then(transfer, OfValue(value, indices, variableCount));
        }

        return IrTransfer.Then(transfer, WriteTarget(target, isRefAssignment: false, indices, variableCount));
    }

    private static IrTransfer OfPropertyReference(IrPropertyReference property,
        IReadOnlyDictionary<IrVariable, int> indices, int variableCount)
    {
        var transfer = property.Receiver is { } receiver
            ? OfLocation(receiver, indices, variableCount)
            : IrTransfer.Empty(variableCount);
        if (!property.Arguments.IsEmpty)
        {
            var outTargets = ImmutableArray.CreateBuilder<IrExpression>();
            transfer = IrTransfer.Then(transfer,
                EvaluateArguments(property.Arguments, outTargets, indices, variableCount));
            transfer = IrTransfer.Then(transfer, WriteOutTargets(outTargets, indices, variableCount));
        }

        return transfer;
    }

    private static IrTransfer OfCall(IrCall call, bool includeOutWrites,
        IReadOnlyDictionary<IrVariable, int> indices, int variableCount)
    {
        var transfer = call.Receiver is { } receiver
            ? (call.Method.ContainingType.IsValueType
                ? OfLocation(receiver, indices, variableCount)
                : OfValue(receiver, indices, variableCount))
            : IrTransfer.Empty(variableCount);
        var outTargets = ImmutableArray.CreateBuilder<IrExpression>();
        transfer = IrTransfer.Then(transfer, EvaluateArguments(call.Arguments, outTargets, indices, variableCount));
        if (includeOutWrites)
        {
            transfer = IrTransfer.Then(transfer, WriteOutTargets(outTargets, indices, variableCount));
        }

        return transfer;
    }

    private static IrTransfer OfCallOutWrites(IrCall call, IReadOnlyDictionary<IrVariable, int> indices,
        int variableCount)
    {
        var transfer = IrTransfer.Empty(variableCount);
        foreach (var argument in OrderByEvaluationIndex(call.Arguments))
        {
            if (argument.Parameter.RefKind == RefKind.Out)
            {
                transfer = IrTransfer.Then(transfer,
                    WriteTarget(argument.Value, isRefAssignment: false, indices, variableCount));
            }
        }

        return transfer;
    }

    private static IrTransfer OfRuntimeCall(IrRuntimeCall runtime, IReadOnlyDictionary<IrVariable, int> indices,
        int variableCount)
    {
        var transfer = IrTransfer.Empty(variableCount);
        var outTargets = ImmutableArray.CreateBuilder<IrExpression>();
        foreach (var argument in runtime.Arguments)
        {
            transfer = IrTransfer.Then(transfer,
                EvaluateByRefKind(argument.Value, argument.RefKind, outTargets, indices, variableCount));
        }

        return IrTransfer.Then(transfer, WriteOutTargets(outTargets, indices, variableCount));
    }

    private static IrTransfer OfStructCreation(IrStructCreation creation,
        IReadOnlyDictionary<IrVariable, int> indices, int variableCount)
    {
        var outTargets = ImmutableArray.CreateBuilder<IrExpression>();
        var transfer = EvaluateArguments(creation.Arguments, outTargets, indices, variableCount);
        transfer = IrTransfer.Then(transfer, WriteOutTargets(outTargets, indices, variableCount));
        foreach (var initializer in creation.Initializers)
        {
            transfer = IrTransfer.Then(transfer, OfAssignment(initializer, indices, variableCount));
        }

        return transfer;
    }

    private static IrTransfer OfArrayCreation(IrArrayCreation array, IReadOnlyDictionary<IrVariable, int> indices,
        int variableCount)
    {
        var transfer = OfValue(array.Length, indices, variableCount);
        foreach (var element in array.Elements)
        {
            transfer = IrTransfer.Then(transfer, OfValue(element, indices, variableCount));
        }

        return transfer;
    }

    private static IrTransfer EvaluateArguments(ImmutableArray<IrArgument> arguments,
        ImmutableArray<IrExpression>.Builder outTargets, IReadOnlyDictionary<IrVariable, int> indices,
        int variableCount)
    {
        var transfer = IrTransfer.Empty(variableCount);
        foreach (var argument in OrderByEvaluationIndex(arguments))
        {
            transfer = IrTransfer.Then(transfer,
                EvaluateByRefKind(argument.Value, argument.Parameter.RefKind, outTargets, indices, variableCount));
        }

        return transfer;
    }

    private static IrTransfer EvaluateByRefKind(IrExpression value, RefKind refKind,
        ImmutableArray<IrExpression>.Builder outTargets, IReadOnlyDictionary<IrVariable, int> indices,
        int variableCount)
    {
        if (refKind == RefKind.None)
        {
            return OfValue(value, indices, variableCount);
        }

        if (refKind == RefKind.Out)
        {
            outTargets.Add(value);
            return OfLocationPart(value, indices, variableCount);
        }

        return OfLocation(value, indices, variableCount);
    }

    private static IrTransfer WriteOutTargets(ImmutableArray<IrExpression>.Builder outTargets,
        IReadOnlyDictionary<IrVariable, int> indices, int variableCount)
    {
        var transfer = IrTransfer.Empty(variableCount);
        foreach (var target in outTargets)
        {
            transfer = IrTransfer.Then(transfer,
                WriteTarget(target, isRefAssignment: false, indices, variableCount));
        }

        return transfer;
    }

    private static IrTransfer OfLocationPart(IrExpression expression, IReadOnlyDictionary<IrVariable, int> indices,
        int variableCount) => expression switch
    {
        IrDiscard or IrVariableReference => IrTransfer.Empty(variableCount),
        IrFieldReference field => OfFieldLocation(field, readRoot: false, indices, variableCount),
        IrElementReference element => IrTransfer.Then(
            OfValue(element.Receiver, indices, variableCount),
            OfValue(element.Index, indices, variableCount)),
        IrPropertyReference property => OfPropertyReference(property, indices, variableCount),
        IrConditional { IsRef: true } conditional => IrTransfer.Either(
            OfValue(conditional.Condition, indices, variableCount),
            OfLocationPart(conditional.WhenTrue, indices, variableCount),
            OfLocationPart(conditional.WhenFalse, indices, variableCount)),
        _ => OfValue(expression, indices, variableCount)
    };

    private static IrTransfer OfFieldLocation(IrFieldReference field, bool readRoot,
        IReadOnlyDictionary<IrVariable, int> indices, int variableCount)
    {
        if (field.Field.IsStatic || field.Receiver is null)
        {
            return IrTransfer.Empty(variableCount);
        }

        if (!field.Receiver.Type.IsValueType || !IrMemberEffects.IsVariable(field.Receiver))
        {
            return OfValue(field.Receiver, indices, variableCount);
        }

        return readRoot
            ? OfLocation(field.Receiver, indices, variableCount)
            : OfLocationPart(field.Receiver, indices, variableCount);
    }

    private static IrTransfer ReadLocationRoot(IrExpression expression, IReadOnlyDictionary<IrVariable, int> indices,
        int variableCount) => expression switch
    {
        IrVariableReference reference => ReadVariable(reference.Variable, indices, variableCount),
        IrFieldReference field when !field.Field.IsStatic && field.Receiver is { } receiver &&
                                    receiver.Type.IsValueType && IrMemberEffects.IsVariable(receiver) =>
            ReadLocationRoot(receiver, indices, variableCount),
        IrConditional { IsRef: true } conditional => IrTransfer.Either(
            IrTransfer.Empty(variableCount),
            ReadLocationRoot(conditional.WhenTrue, indices, variableCount),
            ReadLocationRoot(conditional.WhenFalse, indices, variableCount)),
        _ => IrTransfer.Empty(variableCount)
    };

    private static IrTransfer WriteTarget(IrExpression target, bool isRefAssignment,
        IReadOnlyDictionary<IrVariable, int> indices, int variableCount) => target switch
    {
        IrVariableReference reference when isRefAssignment || reference.Variable.RefKind == RefKind.None =>
            WriteVariable(reference.Variable, indices, variableCount),
        IrVariableReference reference => ReadVariable(reference.Variable, indices, variableCount),
        IrFieldReference field when !field.Field.IsStatic && field.Receiver is { } receiver &&
                                    receiver.Type.IsValueType && IrMemberEffects.IsVariable(receiver) =>
            ReadLocationRoot(receiver, indices, variableCount),
        IrConditional { IsRef: true } conditional => IrTransfer.Either(
            IrTransfer.Empty(variableCount),
            WriteTarget(conditional.WhenTrue, isRefAssignment, indices, variableCount),
            WriteTarget(conditional.WhenFalse, isRefAssignment, indices, variableCount)),
        _ => IrTransfer.Empty(variableCount)
    };

    private static IrTransfer ReadVariable(IrVariable variable, IReadOnlyDictionary<IrVariable, int> indices,
        int variableCount) =>
        TryIndex(variable, indices, out var index)
            ? IrTransfer.Read(index, variableCount)
            : IrTransfer.Empty(variableCount);

    private static IrTransfer WriteVariable(IrVariable variable, IReadOnlyDictionary<IrVariable, int> indices,
        int variableCount) =>
        TryIndex(variable, indices, out var index)
            ? IrTransfer.Write(index, variableCount)
            : IrTransfer.Empty(variableCount);

    private static bool TryIndex(IrVariable variable, IReadOnlyDictionary<IrVariable, int> indices, out int index)
    {
        if (variable.Kind is not (IrVariableKind.Local or IrVariableKind.Temporary))
        {
            index = -1;
            return false;
        }

        if (!indices.TryGetValue(variable, out index))
        {
            throw new InvalidOperationException($"Tracked variable '{variable.Name}' was used without a declaration.");
        }

        return true;
    }

    private static IrArgument[] OrderByEvaluationIndex(ImmutableArray<IrArgument> arguments)
    {
        var ordered = new IrArgument[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            ordered[i] = arguments[i];
        }

        Array.Sort(ordered, static (a, b) => a.EvaluationIndex.CompareTo(b.EvaluationIndex));
        return ordered;
    }

    private static InvalidOperationException Unsupported(IrNode node) =>
        new($"Liveness transfer is not defined for IR node '{node.GetType().Name}'.");
}
