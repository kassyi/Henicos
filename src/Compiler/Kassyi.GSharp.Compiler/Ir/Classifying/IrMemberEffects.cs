using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Ir.Classifying;

/// <summary>
/// The effects of fields, properties, calls and struct creations for <see cref="IrEffectWalker"/>, with the predicates
/// of <see cref="MemberEffects"/> and <see cref="CallEffects"/>.
/// </summary>
internal static class IrMemberEffects
{
    /// <summary>
    /// The location of a field: a static field is memory; a field of a struct variable belongs to the variable; a field
    /// of <see langword="this"/> or of a reference is memory, found without reading memory.
    /// </summary>
    public static EffectRoot? AddFieldLocation(IrEffectWalker walker, IrFieldReference field)
    {
        if (field.Field.IsStatic || field.Receiver is not { } receiver || IsThis(receiver))
        {
            return new EffectRoot(null);
        }

        if (!receiver.Type.IsValueType)
        {
            walker.AddValue(receiver);
            return new EffectRoot(null);
        }

        if (IsVariable(receiver))
        {
            return walker.AddLocation(receiver);
        }

        // A field of a struct value (a call result, a conditional, a creation) is part of a temporary: evaluating the
        // value is all it reads
        walker.AddValue(receiver);
        return null;
    }

    /// <summary>Whether <paramref name="expression"/> names storage rather than computing a temporary value.</summary>
    private static bool IsVariable(IrExpression expression) => expression switch
    {
        IrVariableReference or IrElementReference => true,
        IrFieldReference { Receiver: null } => true,
        IrFieldReference { Receiver: { } receiver } => !receiver.Type.IsValueType || IsVariable(receiver),
        IrPropertyReference property => property.Property.RefKind != RefKind.None,
        IrCall call => call.Method.RefKind != RefKind.None,
        IrConditional conditional => conditional.IsRef,
        _ => false
    };

    /// <summary>
    /// The location of a property (an assignment target or an indexer passed by reference): a property of a struct
    /// variable belongs to the variable, an element of a span or of anything else is memory.
    /// </summary>
    public static EffectRoot? AddPropertyLocation(IrEffectWalker walker, IrPropertyReference property)
    {
        var receiver = property.Receiver;
        var ownsLocation = receiver is not null && !property.Property.IsStatic && !IsThis(receiver) &&
                           receiver.Type.IsValueType &&
                           (!property.Property.IsIndexer ||
                            receiver.Type is { IsRefLikeType: false } and not IPointerTypeSymbol);
        EffectRoot? root = new EffectRoot(null);
        if (ownsLocation && receiver is not null)
        {
            root = walker.AddLocation(receiver);
        }
        else if (receiver is not null)
        {
            walker.AddValue(receiver);
        }

        AddArgumentValues(walker, property.Arguments);
        return root;
    }

    /// <summary>The effects of reading a property or an indexer.</summary>
    public static void AddPropertyValue(IrEffectWalker walker, IrPropertyReference reference)
    {
        var property = reference.Property;
        if (reference.Receiver is not { } receiver || property.IsStatic)
        {
            // Memory unless it is an intrinsic (ThreadIndex.X)
            if (!MemberEffects.IsIntrinsicProperty(property))
            {
                walker.ReadMemory();
            }

            return;
        }

        var mutatesReceiver = MemberEffects.MayMutateValueTypeReceiver(property, walker.Compilation);
        if (mutatesReceiver)
        {
            var root = walker.AddLocation(receiver);
            walker.AddRead(root);
            walker.AddWrite(root);
        }
        else
        {
            walker.AddValue(receiver);
        }

        AddArgumentValues(walker, reference.Arguments);
        if (property.IsIndexer)
        {
            walker.ReadMemory();
            if (mutatesReceiver && walker.MayWriteMemory(property.GetMethod))
            {
                walker.WriteMemory();
            }

            return;
        }

        if (!MemberEffects.IsComputedUserProperty(property))
        {
            return;
        }

        // The getter of a struct may read anything, and writes what DeviceFunctionEffects says
        walker.ReadMemory();
        if (walker.MayWriteMemory(property.GetMethod))
        {
            walker.WriteMemory();
        }
    }

    /// <summary>
    /// The effects of a call: its receiver (a struct receiver is read and may be written), its arguments by value or by
    /// reference, and what the call itself reads and writes by its classification.
    /// </summary>
    public static void AddCall(IrEffectWalker walker, IrCall call)
    {
        var method = call.Method;
        EffectRoot? receiver = null;
        if (call.Receiver is { } instance)
        {
            if (method.ContainingType.IsValueType)
            {
                receiver = walker.AddLocation(instance);
                walker.AddRead(receiver);
            }
            else
            {
                walker.AddValue(instance);
            }
        }

        AddCallOf(walker, method, call.Arguments, receiver, call.Classification.Effect);
    }

    /// <summary>The effects of creating a struct: its constructor call and the values of its initializer.</summary>
    public static void AddStructCreation(IrEffectWalker walker, IrStructCreation creation)
    {
        if (creation.Type.IsSharedMemoryType())
        {
            walker.WriteMemory();
        }

        if (creation.Constructor is { } constructor)
        {
            AddCallOf(walker, constructor, creation.Arguments, receiver: null,
                DeviceFunctionEffects.Classify(constructor, walker.Compilation));
        }
        else
        {
            AddArgumentValues(walker, creation.Arguments);
        }

        // The members that the initializer assigns belong to the new value; only their values are evaluated
        foreach (var initializer in creation.Initializers)
        {
            walker.AddValue(initializer.Value);
        }
    }

    // The arguments, by value or by reference, then the call itself: memory unless it is pure math over scalars, and
    // the writes of its effect (its ref / out arguments and receiver, or memory)
    private static void AddCallOf(IrEffectWalker walker, IMethodSymbol method, ImmutableArray<IrArgument> arguments,
        EffectRoot? receiver, DeviceFunctionEffect effect)
    {
        var references = ImmutableArray.CreateBuilder<EffectRoot?>();
        foreach (var argument in arguments)
        {
            var refKind = argument.Parameter.RefKind;
            if (refKind == RefKind.None)
            {
                walker.AddValue(argument.Value);
                continue;
            }

            var root = walker.AddLocation(argument.Value);

            // The callee reads through a ref / in / ref readonly parameter, as part of this call
            if (refKind != RefKind.Out)
            {
                walker.AddRead(root);
            }

            if (refKind is RefKind.Ref or RefKind.Out)
            {
                references.Add(root);
            }
        }

        if (!CallEffects.IsScalarMath(method))
        {
            walker.ReadMemory();
        }

        if (effect == DeviceFunctionEffect.None)
        {
            return;
        }

        foreach (var root in references)
        {
            walker.AddWrite(root);
        }

        walker.AddWrite(receiver);
        if (effect == DeviceFunctionEffect.Memory)
        {
            walker.WriteMemory();
        }
    }

    private static void AddArgumentValues(IrEffectWalker walker, ImmutableArray<IrArgument> arguments)
    {
        foreach (var argument in arguments)
        {
            walker.AddValue(argument.Value);
        }
    }

    private static bool IsThis(IrExpression expression) =>
        expression is IrVariableReference { Variable.Kind: IrVariableKind.This };
}
