#nullable enable
using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// The known differences between the effects of the IR (<c>IrEffects</c>) and of the syntax walk
/// (<see cref="ExpressionEffects"/>), each accepted only when the node has its shape and the two results differ by
/// exactly the reads and writes it names.
/// </summary>
internal static class IrEffectDifferences
{
    private const string Less = "IR finds less";
    private const string More = "IR finds more";

    /// <summary>The reason of a difference at <paramref name="node"/>, or <see langword="null"/>.</summary>
    public static string? Reason(IrExpression node, string kind, ExpressionEffects old, ExpressionEffects ir,
        Compilation compilation)
    {
        if (node is IrConversion { IsImplicit: true, Operand.ConstantValue.HasValue: false } conversion)
        {
            // An implicit conversion has the span of its operand, which is not compared on its own
            node = conversion.Operand;
        }

        return kind switch
        {
            Less => LessReason(node, old, ir, compilation),
            More => MoreReason(node, old, ir),
            _ => null
        };
    }

    // The syntax walk counts more than the IR
    private static string? LessReason(IrExpression node, ExpressionEffects old, ExpressionEffects ir,
        Compilation compilation) => node switch
    {
        // The syntax walk takes a discard as a location in memory
        _ when HasDiscardOperand(node) && !WritesMemoryBelow(node, compilation) &&
               Differs(ir, old, writes: Memory()) =>
            IrClassificationEquivalence.KnownPrefix + "a discard is written by no one",
        // The syntax walk reads memory to find the location of this
        IrVariableReference { Variable.Kind: IrVariableKind.This } when Differs(ir, old, reads: Memory()) =>
            IrClassificationEquivalence.KnownPrefix + "the location of this reads nothing",
        // The syntax walk writes the variable of a by-value receiver of a reduced extension call
        IrCall { Method: { IsExtensionMethod: true, Parameters: [{ RefKind: RefKind.None }, ..] } } call
            when RootSymbol(call.Arguments[0].Value) is { } receiver && Differs(ir, old, writes: Variable(receiver)) =>
            IrClassificationEquivalence.KnownPrefix + "a by-value extension receiver is not written",
        // The syntax walk takes the member an object initializer assigns as a location in memory
        IrAssignment { Target: IrFieldReference { Receiver: IrInitializedObject } }
            when !WritesMemoryBelow(node, compilation) && Differs(ir, old, writes: Memory()) =>
            IrClassificationEquivalence.KnownPrefix + "an initializer writes the new value",
        // The syntax walk reads the location of a struct temporary (a call result, a creation) as memory
        IrFieldReference { Receiver: { Type.IsValueType: true } receiver }
            when !IsStorage(receiver) && Differs(ir, old, reads: Memory()) =>
            IrClassificationEquivalence.KnownPrefix + "a field of a struct temporary reads no memory",
        _ => null
    };

    // The IR counts more than the syntax walk
    private static string? MoreReason(IrExpression node, ExpressionEffects old, ExpressionEffects ir) => node switch
    {
        // An operator macro is taken as writing memory; its target is not looked at
        IrUnary { Operator.Method: not null } or IrBinary { Operator.Method: not null }
            or IrCompoundAssignment { Operator.Method: not null } or IrIncrement { OperatorMethod: not null }
            when Differs(old, ir, writes: Memory()) =>
            IrClassificationEquivalence.KnownPrefix + "an operator macro is taken as writing memory",
        // #1606: the syntax walk does not call the getter of a struct property used as a receiver
        IrFieldReference { Receiver: IrPropertyReference { Property.RefKind: RefKind.None } property }
            when Differs(old, ir, Memory(), GetterWrites(property)) =>
            IrClassificationEquivalence.KnownPrefix + "a struct property used as a receiver calls its getter (#1606)",
        // #1606: the syntax walk does not see that a getter read through the implicit this may change this
        IrPropertyReference
            {
                Receiver: IrVariableReference { Variable.Kind: IrVariableKind.This, Source.Synthesized: true }
            }
            when Differs(old, ir, writes: Memory()) =>
            IrClassificationEquivalence.KnownPrefix + "a getter read through the implicit this writes this (#1606)",
        _ => null
    };

    // narrow lies within wide, and wide within narrow plus exactly the given reads and writes
    private static bool Differs(ExpressionEffects narrow, ExpressionEffects wide, VariableSet reads = default,
        VariableSet writes = default) =>
        narrow.Reads.IsSubsetOf(wide.Reads) && narrow.Writes.IsSubsetOf(wide.Writes) &&
        wide.Reads.IsSubsetOf(narrow.Reads.Union(reads)) && wide.Writes.IsSubsetOf(narrow.Writes.Union(writes));

    private static VariableSet Memory() => new() { Memory = true };

    private static VariableSet Variable(ISymbol symbol)
    {
        var set = default(VariableSet);
        set.Add(symbol);
        return set;
    }

    // What the getter of a property used as a receiver writes: memory and the variable its receiver belongs to
    private static VariableSet GetterWrites(IrPropertyReference property)
    {
        var set = Memory();
        if (property.Receiver is { } receiver && RootSymbol(receiver) is { } root)
        {
            set.Add(root);
        }

        return set;
    }

    // The local or by-value parameter a location belongs to, through fields of structs
    private static ISymbol? RootSymbol(IrExpression expression) => expression switch
    {
        IrVariableReference { Variable: { RefKind: RefKind.None, Symbol: { } symbol } } => symbol,
        IrFieldReference { Receiver: { Type.IsValueType: true } receiver } => RootSymbol(receiver),
        IrConversion { IsImplicit: true } conversion => RootSymbol(conversion.Operand),
        _ => null
    };

    // A discard as the target of an assignment or as an argument
    private static bool HasDiscardOperand(IrExpression node) => node switch
    {
        IrAssignment { Target: IrDiscard } => true,
        IrCall call => call.Arguments.Any(static argument => argument.Value is IrDiscard),
        _ => false
    };

    // Whether a call, an operator macro or a creation below the node may write memory, so that a missing write of
    // memory cannot be put down to the discard or the initializer
    private static bool WritesMemoryBelow(IrNode node, Compilation compilation)
    {
        var writes = false;
        IrNodeChildren.Visit(node, child => writes |= child switch
        {
            IrCall call => call.Classification.Effect == DeviceFunctionEffect.Memory,
            IrUnary { Operator.Method: not null } or IrBinary { Operator.Method: not null } => true,
            IrStructCreation creation => creation.Type.IsSharedMemoryType() ||
                                         (creation.Constructor is { } constructor &&
                                          DeviceFunctionEffects.Classify(constructor, compilation) ==
                                          DeviceFunctionEffect.Memory),
            IrPropertyReference => true,
            _ => false
        });
        return writes;
    }

    private static bool IsStorage(IrExpression expression) => expression switch
    {
        IrVariableReference or IrElementReference => true,
        IrFieldReference { Receiver: { } receiver } => !receiver.Type.IsValueType || IsStorage(receiver),
        IrFieldReference => true,
        IrPropertyReference property => property.Property.RefKind != RefKind.None,
        IrCall call => call.Method.RefKind != RefKind.None,
        IrConditional conditional => conditional.IsRef,
        _ => false
    };
}
