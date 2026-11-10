using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir.Classifying;

/// <summary>
/// Collects what one IR expression reads and writes, for <see cref="IrEffects"/>. Members, properties and calls are in
/// <see cref="IrMemberEffects"/>.
/// </summary>
/// <remarks>
/// A location is returned as the <see cref="EffectRoot"/> it belongs to, or <see langword="null"/> for a discard, which
/// no one reads or writes.
/// </remarks>
internal sealed class IrEffectWalker(Compilation compilation)
{
    private VariableSet _reads;
    private VariableSet _writes;

    /// <summary>The compilation of the method.</summary>
    public Compilation Compilation => compilation;

    /// <summary>What was collected.</summary>
    public ExpressionEffects ToEffects() => new(_reads, _writes);

    /// <summary>Marks memory as read.</summary>
    public void ReadMemory() => _reads.Memory = true;

    /// <summary>Marks memory as written.</summary>
    public void WriteMemory() => _writes.Memory = true;

    /// <summary>Adds a read of the location <paramref name="root"/>.</summary>
    public void AddRead(EffectRoot? root) => Add(ref _reads, root);

    /// <summary>Adds a write of the location <paramref name="root"/>.</summary>
    public void AddWrite(EffectRoot? root) => Add(ref _writes, root);

    /// <summary>Whether a call of <paramref name="method"/> (or none) may write memory.</summary>
    public bool MayWriteMemory(IMethodSymbol? method) =>
        method != null && DeviceFunctionEffects.Classify(method, compilation) == DeviceFunctionEffect.Memory;

    /// <summary>Adds the effects of evaluating <paramref name="expression"/> for its value.</summary>
    public void AddValue(IrExpression expression)
    {
        if (expression.ConstantValue.HasValue)
        {
            return;
        }

        switch (expression)
        {
            case IrConstant or IrDefaultValue or IrDiscard or IrInitializedObject:
                return;
            case IrVariableReference reference:
                AddRead(RootOf(reference.Variable));
                return;
            case IrFieldReference or IrElementReference:
                AddRead(AddLocation(expression));
                return;
            case IrPropertyReference property:
                IrMemberEffects.AddPropertyValue(this, property);
                return;
            case IrCall call:
                IrMemberEffects.AddCall(this, call);
                return;
            case IrConversion conversion:
                AddConversion(conversion);
                return;
            default:
                AddComputation(expression);
                return;
        }
    }

    /// <summary>
    /// Adds the reads that find the location of <paramref name="expression"/> (indices, receivers) and returns the
    /// variable it belongs to, without reading or writing the location itself.
    /// </summary>
    public EffectRoot? AddLocation(IrExpression expression)
    {
        switch (expression)
        {
            case IrDiscard:
                return null;
            case IrVariableReference reference:
                return RootOf(reference.Variable);
            case IrFieldReference field:
                return IrMemberEffects.AddFieldLocation(this, field);
            case IrElementReference element:
                // An array element; the IR has no other element reference
                AddValue(element.Receiver);
                AddValue(element.Index);
                return new EffectRoot(null);
            case IrPropertyReference property:
                return IrMemberEffects.AddPropertyLocation(this, property);
            case IrConditional { IsRef: true } conditional:
                AddValue(conditional.Condition);
                var whenTrue = AddLocation(conditional.WhenTrue);
                var whenFalse = AddLocation(conditional.WhenFalse);
                return Combine(whenTrue, whenFalse);
            default:
                AddValue(expression);
                return new EffectRoot(null);
        }
    }

    // Operators, assignments, creations: everything that is not a reference, a call or a conversion
    private void AddComputation(IrExpression expression)
    {
        switch (expression)
        {
            case IrUnary unary:
                AddValue(unary.Operand);
                AddOperatorWrites(unary.Operator.Method);
                return;
            case IrBinary binary:
                AddValue(binary.Left);
                AddValue(binary.Right);
                AddOperatorWrites(binary.Operator.Method);
                return;
            case IrLogical logical:
                AddValue(logical.Left);
                AddValue(logical.Right);
                return;
            case IrConditional conditional:
                AddValue(conditional.Condition);
                AddValue(conditional.WhenTrue);
                AddValue(conditional.WhenFalse);
                return;
            case IrNullTest test:
                AddValue(test.Operand);
                return;
            case IrCoalesce coalesce:
                AddValue(coalesce.Value);
                AddValue(coalesce.WhenNull);
                return;
            case IrAddressOf address:
                AddLocation(address.Operand);
                return;
            default:
                AddWrite(expression);
                return;
        }
    }

    // Assignments, increments and creations, which write
    private void AddWrite(IrExpression expression)
    {
        switch (expression)
        {
            case IrAssignment assignment:
                AddAssignment(assignment);
                return;
            case IrCompoundAssignment compound:
                AddUpdate(compound.Target, compound.Value, compound.Operator.Method);
                return;
            case IrIncrement increment:
                AddUpdate(increment.Target, null, increment.OperatorMethod);
                return;
            case IrStructCreation creation:
                IrMemberEffects.AddStructCreation(this, creation);
                return;
            case IrArrayCreation array:
                AddValue(array.Length);
                foreach (var element in array.Elements)
                {
                    AddValue(element);
                }

                return;
            case IrSequence sequence:
                AddSequence(sequence);
                return;
            default:
                // An opaque expression may read and write anything
                AddUnknown();
                return;
        }
    }

    // A user-defined conversion reads what it may and writes what DeviceFunctionEffects says
    private void AddConversion(IrConversion conversion)
    {
        if (conversion.Conversion.Method is { } method)
        {
            ReadMemory();
            if (MayWriteMemory(method))
            {
                WriteMemory();
            }
        }

        AddValue(conversion.Operand);
    }

    // A user-defined unary or binary operator: its operands are evaluated by the node, and its body may write memory
    private void AddOperatorWrites(IMethodSymbol? method)
    {
        if (MayWriteMemory(method))
        {
            WriteMemory();
        }
    }

    private void AddAssignment(IrAssignment assignment)
    {
        var root = AddLocation(assignment.Target);
        if (assignment.IsRef)
        {
            // A ref assignment names its value's location, which the reference then reads through
            AddRead(AddLocation(assignment.Value));
        }
        else
        {
            AddValue(assignment.Value);
        }

        AddWrite(root);
        if (assignment.Target is IrPropertyReference { Property: var property } &&
            MemberEffects.IsComputedUserProperty(property) &&
            MayWriteMemory(property.SetMethod))
        {
            WriteMemory();
        }
    }

    // A compound assignment (with its value) or an increment (without): the target is read and written
    private void AddUpdate(IrExpression target, IrExpression? value, IMethodSymbol? operatorMethod)
    {
        var root = AddLocation(target);
        AddRead(root);
        if (value != null)
        {
            AddValue(value);
        }

        AddWrite(root);
        AddOperatorWrites(operatorMethod);
        if (target is not IrPropertyReference { Property: var property } ||
            !MemberEffects.IsComputedUserProperty(property))
        {
            return;
        }

        if (value == null)
        {
            // An increment of a computed property calls its getter, which may read anything
            ReadMemory();
            if (MayWriteMemory(property.GetMethod))
            {
                WriteMemory();
            }
        }

        if (MayWriteMemory(property.SetMethod))
        {
            WriteMemory();
        }
    }

    private void AddSequence(IrSequence sequence)
    {
        foreach (var statement in sequence.SideEffects)
        {
            switch (statement)
            {
                case IrExpressionStatement { Expression: var expression }:
                    AddValue(expression);
                    break;
                case IrLocalDeclaration { Initializer: { } initializer, Variable: var variable }:
                    AddValue(initializer);
                    AddWrite(RootOf(variable));
                    break;
                case IrLocalDeclaration:
                    break;
                default:
                    AddUnknown();
                    break;
            }
        }

        AddValue(sequence.Value);
    }

    private void AddUnknown()
    {
        _reads.Memory = _reads.AllLocals = true;
        _writes.Memory = _writes.AllLocals = true;
    }

    // A non-ref local or by-value parameter as itself unless it has another name, a temporary as itself, anything else
    // (this, a ref local, a ref / out / in parameter) as memory
    private EffectRoot RootOf(IrVariable variable) => variable switch
    {
        { RefKind: not RefKind.None } or { Kind: IrVariableKind.This } => new EffectRoot(null),
        { Kind: IrVariableKind.Temporary } => new EffectRoot(variable),
        { Symbol: { } symbol } => AliasedVariables.Contains(symbol, compilation)
            ? new EffectRoot(null)
            : new EffectRoot(symbol),
        _ => new EffectRoot(null)
    };

    private static EffectRoot? Combine(EffectRoot? whenTrue, EffectRoot? whenFalse) =>
        (whenTrue, whenFalse) switch
        {
            ({ } t, { } f) => EffectRoot.Combine(t, f),
            ({ } t, null) => t,
            (null, { } f) => f,
            _ => null
        };

    private static void Add(ref VariableSet set, EffectRoot? root)
    {
        if (root is not { } location)
        {
            return;
        }

        foreach (var variable in location.Variables)
        {
            set.Add(variable);
        }

        if (location.IsMemory)
        {
            set.Memory = true;
        }
    }
}
