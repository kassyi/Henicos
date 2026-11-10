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
    /// of <see langword="this"/> or of a reference is memory, found without reading memory; a field of a struct
    /// temporary is part of the temporary.
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

        // A call result, a conditional, a creation: evaluating the value is all it reads
        walker.AddValue(receiver);
        return null;
    }

    /// <summary>
    /// The location of a property (an assignment target or an indexer passed by reference): a property of a struct
    /// variable belongs to the variable, an element of a span or of anything else is memory.
    /// </summary>
    public static EffectRoot? AddPropertyLocation(IrEffectWalker walker, IrPropertyReference property)
    {
        EffectRoot? root = new EffectRoot(null);
        if (property.Receiver is { } receiver && !IsThis(receiver))
        {
            if (OwnsLocation(property, receiver))
            {
                root = walker.AddLocation(receiver);
            }
            else
            {
                walker.AddValue(receiver);
            }
        }

        AddArgumentValues(walker, property.Arguments);
        return root;
    }

    /// <summary>The effects of reading a property or an indexer: its receiver, its arguments and its getter.</summary>
    public static void AddPropertyValue(IrEffectWalker walker, IrPropertyReference reference)
    {
        var property = reference.Property;
        if (reference.Receiver is not { } receiver || property.IsStatic)
        {
            AddStaticPropertyValue(walker, property);
            return;
        }

        var mutatesReceiver = MemberEffects.MayMutateValueTypeReceiver(property, walker.Compilation);
        if (mutatesReceiver)
        {
            var root = walker.AddLocation(receiver);
            walker.ReadRoot(root);
            walker.WriteRoot(root);
        }
        else
        {
            walker.AddValue(receiver);
        }

        AddArgumentValues(walker, reference.Arguments);
        if (property.IsIndexer)
        {
            AddIndexerGetter(walker, property, mutatesReceiver);
        }
        else
        {
            AddComputedGetter(walker, property);
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
                walker.ReadRoot(receiver);
            }
            else
            {
                walker.AddValue(instance);
            }
        }

        var references = AddArguments(walker, call.Arguments);
        AddCallItself(walker, method, references, receiver, call.Classification.Effect);
    }

    /// <summary>The effects of creating a struct: its constructor call and the values of its initializer.</summary>
    /// <remarks>Whether the struct is a <c>SharedMemory&lt;T&gt;</c> is asked from its type each time.</remarks>
    public static void AddStructCreation(IrEffectWalker walker, IrStructCreation creation)
    {
        if (creation.Type.IsSharedMemoryType())
        {
            walker.WriteMemory();
        }

        if (creation.Constructor is { } constructor)
        {
            var references = AddArguments(walker, creation.Arguments);
            AddCallItself(walker, constructor, references, receiver: null,
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

    // A static property, or one read through its type: memory unless it is an intrinsic (ThreadIndex.X)
    private static void AddStaticPropertyValue(IrEffectWalker walker, IPropertySymbol property)
    {
        if (!MemberEffects.IsIntrinsicProperty(property))
        {
            walker.ReadMemory();
        }
    }

    // An indexer reads memory, and writes it when its getter may change the receiver and writes memory
    private static void AddIndexerGetter(IrEffectWalker walker, IPropertySymbol indexer, bool mutatesReceiver)
    {
        walker.ReadMemory();
        if (mutatesReceiver && walker.MayWriteMemory(indexer.GetMethod))
        {
            walker.WriteMemory();
        }
    }

    // The getter of a computed property of a struct may read anything, and writes what DeviceFunctionEffects says
    private static void AddComputedGetter(IrEffectWalker walker, IPropertySymbol property)
    {
        if (!MemberEffects.IsComputedUserProperty(property))
        {
            return;
        }

        walker.ReadMemory();
        if (walker.MayWriteMemory(property.GetMethod))
        {
            walker.WriteMemory();
        }
    }

    // Evaluates the arguments by value or by reference and returns the locations the callee may write through
    private static ImmutableArray<EffectRoot?>.Builder AddArguments(IrEffectWalker walker,
        ImmutableArray<IrArgument> arguments)
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
                walker.ReadRoot(root);
            }

            if (refKind is RefKind.Ref or RefKind.Out)
            {
                references.Add(root);
            }
        }

        return references;
    }

    // The call itself: memory unless it is pure math over scalars or a view (AsSpan, Slice), and the writes of its
    // effect (its ref / out arguments and receiver, or memory)
    private static void AddCallItself(IrEffectWalker walker, IMethodSymbol method,
        ImmutableArray<EffectRoot?>.Builder references, EffectRoot? receiver, DeviceFunctionEffect effect)
    {
        if (!CallEffects.IsScalarMath(method) && !SpanWriteClassifier.IsPureViewMethod(method))
        {
            walker.ReadMemory();
        }

        if (effect == DeviceFunctionEffect.None)
        {
            return;
        }

        foreach (var root in references)
        {
            walker.WriteRoot(root);
        }

        walker.WriteRoot(receiver);
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

    // Whether a property is a location inside its struct receiver: a variable receiver that is not this, and for an
    // indexer not a span or a pointer
    private static bool OwnsLocation(IrPropertyReference property, IrExpression receiver) =>
        !property.Property.IsStatic && !IsThis(receiver) && receiver.Type.IsValueType &&
        (!property.Property.IsIndexer || receiver.Type is { IsRefLikeType: false } and not IPointerTypeSymbol);

    // Whether expression names storage rather than computing a temporary value
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

    private static bool IsThis(IrExpression expression) =>
        expression is IrVariableReference { Variable.Kind: IrVariableKind.This };
}
