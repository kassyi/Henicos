using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir.Classifying;

/// <summary>
/// Collects what one IR expression reads and writes, for <see cref="IrEffects"/>. Members, properties and calls are in
/// <see cref="IrMemberEffects"/>.
/// </summary>
/// <remarks>
/// A location is returned as the <see cref="EffectRoot"/> it belongs to, or <see langword="null"/> for a discard or a
/// part of a temporary, which no one else reads or writes. Every expression node has its own case in
/// <see cref="AddValue"/>; a node kind without one is an internal error, so a new kind cannot silently fall into "may
/// read and write anything".
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
    public void ReadRoot(EffectRoot? root) => Add(ref _reads, root);

    /// <summary>Adds a write of the location <paramref name="root"/>.</summary>
    public void WriteRoot(EffectRoot? root) => Add(ref _writes, root);

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
                ReadRoot(RootOf(reference.Variable));
                return;
            case IrFieldReference or IrElementReference:
                ReadRoot(AddLocation(expression));
                return;
            case IrPropertyReference property:
                IrMemberEffects.AddPropertyValue(this, property);
                return;
            case IrCall call:
                IrMemberEffects.AddCall(this, call);
                return;
            case IrStructCreation creation:
                IrMemberEffects.AddStructCreation(this, creation);
                return;
            case IrConversion conversion:
                AddConversion(conversion);
                return;
            case IrUnary unary:
                AddOperands(unary.Operator.Method, unary.Operand, null);
                return;
            case IrBinary binary:
                AddOperands(binary.Operator.Method, binary.Left, binary.Right);
                return;
            case IrLogical logical:
                AddOperands(null, logical.Left, logical.Right);
                return;
            case IrConditional conditional:
                AddConditional(conditional);
                return;
            case IrNullTest test:
                AddValue(test.Operand);
                return;
            case IrCoalesce coalesce:
                AddOperands(null, coalesce.Value, coalesce.WhenNull);
                return;
            case IrAddressOf address:
                AddLocation(address.Operand);
                return;
            case IrAssignment assignment:
                AddAssignment(assignment);
                return;
            case IrCompoundAssignment compound:
                AddUpdate(compound.Target, compound.Value, compound.Operator.Method);
                return;
            case IrIncrement increment:
                AddUpdate(increment.Target, null, increment.OperatorMethod);
                return;
            case IrArrayCreation array:
                AddArrayCreation(array);
                return;
            case IrSequence sequence:
                AddSequence(sequence);
                return;
            case IrOpaqueExpression:
                AddUnknown();
                return;
            default:
                throw new InvalidOperationException(
                    $"The effects of an IR node of kind {expression.GetType().Name} are not defined.");
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

    // The operands of an operator, then the operator itself: an operator macro (the only user-defined operator G#
    // accepts; a plain one is GSH0060) is taken as writing memory, since what runs is its target, not its body
    private void AddOperands(IMethodSymbol? userOperator, IrExpression first, IrExpression? second)
    {
        AddValue(first);
        if (second != null)
        {
            AddValue(second);
        }

        if (userOperator != null)
        {
            WriteMemory();
        }
    }

    private void AddConditional(IrConditional conditional)
    {
        AddValue(conditional.Condition);
        AddValue(conditional.WhenTrue);
        AddValue(conditional.WhenFalse);
    }

    // A user-defined conversion reads what it may and writes what DeviceFunctionEffects says. One without a body in
    // the compilation applied to a constant (null to Span<T>) is a view of the constant and reads nothing
    private void AddConversion(IrConversion conversion)
    {
        if (conversion.Conversion.Method is { } method &&
            !(conversion.Operand.ConstantValue.HasValue && method.DeclaringSyntaxReferences.Length == 0))
        {
            ReadMemory();
            if (MayWriteMemory(method))
            {
                WriteMemory();
            }
        }

        AddValue(conversion.Operand);
    }

    private void AddAssignment(IrAssignment assignment)
    {
        var root = AddLocation(assignment.Target);
        if (assignment.IsRef)
        {
            // A ref assignment names its value's location, which the reference then reads through
            ReadRoot(AddLocation(assignment.Value));
        }
        else
        {
            AddValue(assignment.Value);
        }

        WriteRoot(root);
        if (assignment.Target is IrPropertyReference { Property: var property } &&
            MemberEffects.IsUserAccessorProperty(property) &&
            MayWriteMemory(property.SetMethod))
        {
            WriteMemory();
        }
    }

    // A compound assignment (with its value) or an increment (without): the target is read and written
    private void AddUpdate(IrExpression target, IrExpression? value, IMethodSymbol? userOperator)
    {
        var root = AddLocation(target);
        ReadRoot(root);
        if (value != null)
        {
            AddValue(value);
        }

        WriteRoot(root);
        if (userOperator != null)
        {
            WriteMemory();
        }

        if (target is IrPropertyReference { Property: var property } && MemberEffects.IsUserAccessorProperty(property))
        {
            AddComputedPropertyUpdate(property, isIncrement: value == null);
        }
    }

    // The accessors an update of a computed property calls: the getter of an increment, and the setter
    private void AddComputedPropertyUpdate(IPropertySymbol property, bool isIncrement)
    {
        if (isIncrement)
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

    private void AddArrayCreation(IrArrayCreation array)
    {
        AddValue(array.Length);
        foreach (var element in array.Elements)
        {
            AddValue(element);
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
                    WriteRoot(RootOf(variable));
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
