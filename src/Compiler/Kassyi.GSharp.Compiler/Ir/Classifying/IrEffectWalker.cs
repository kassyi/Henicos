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

    /// <summary>
    /// Adds a call of user code (accessors, an operator, a conversion; <see langword="null"/> for none): it may read
    /// anything, and writes memory when <see cref="DeviceFunctionEffects"/> says one of them does.
    /// </summary>
    public void AddUserCode(IMethodSymbol? first, IMethodSymbol? second = null)
    {
        ReadMemory();
        if (DeviceFunctionEffects.WritesMemory(first, compilation) ||
            DeviceFunctionEffects.WritesMemory(second, compilation))
        {
            WriteMemory();
        }
    }

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
            AddUserCode(method);
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
        AddSetterCall(assignment.Target);
    }

    /// <summary>
    /// Adds the call of the setter when <paramref name="target"/> is a computed property of a user struct: it may read
    /// anything, and writes what <see cref="DeviceFunctionEffects"/> says.
    /// </summary>
    public void AddSetterCall(IrExpression target)
    {
        if (target is not IrPropertyReference { Property: var property } ||
            !MemberEffects.IsUserAccessorProperty(property))
        {
            return;
        }

        AddUserCode(property.SetMethod);
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

        // The accessors an update of a computed property calls: the getter, then the setter
        if (target is IrPropertyReference { Property: var property } && MemberEffects.IsUserAccessorProperty(property))
        {
            AddUserCode(property.GetMethod, property.SetMethod);
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
        _reads.AddUnknown();
        _writes.AddUnknown();
    }

    /// <summary>
    /// Whether <paramref name="variable"/> is a local or by-value parameter that no other name reaches (its address is
    /// not taken, no <c>ref</c> expression names it, no lambda captures it), or a temporary of a pass.
    /// </summary>
    public static bool IsUnaliased(IrVariable variable, Compilation compilation) => variable.Kind switch
    {
        IrVariableKind.Temporary => true,
        IrVariableKind.Local or IrVariableKind.Parameter when variable.RefKind == RefKind.None &&
                                                              variable.Symbol is { } symbol =>
            !AliasedVariables.Contains(symbol, compilation),
        _ => false
    };

    // A non-ref variable that no other name reaches as itself (its symbol, or the temporary), anything else (this, a
    // ref local, a ref / out / in parameter) as memory
    private EffectRoot RootOf(IrVariable variable) =>
        variable.RefKind == RefKind.None && IsUnaliased(variable, compilation)
            ? new EffectRoot((object?)variable.Symbol ?? variable)
            : new EffectRoot(null);

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
        if (root is { } location)
        {
            set.AddRoot(location);
        }
    }
}
