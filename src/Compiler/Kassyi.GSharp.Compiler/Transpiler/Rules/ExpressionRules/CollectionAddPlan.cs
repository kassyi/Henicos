namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// What an element of a collection initializer writes (<see cref="CollectionInitializerElements.Plan"/>): the call of
/// the <c>Add</c> that C# binds for it (<see cref="Call"/>), or the reason it has none (<see cref="Failed"/>).
/// </summary>
internal readonly struct CollectionAddPlan
{
    private CollectionAddPlan(CollectionAddFailure failure, IMethodSymbol add, CallTargetPlan target,
        bool receiverByReference, CollectionAddArgument[] arguments)
    {
        Failure = failure;
        Add = add;
        Target = target;
        ReceiverByReference = receiverByReference;
        Arguments = arguments;
    }

    /// <summary>Why the element has no call; <see cref="CollectionAddFailure.None"/> when it is written.</summary>
    public CollectionAddFailure Failure { get; }

    /// <summary>The <c>Add</c> C# binds, an extension method in its reduced form.</summary>
    public IMethodSymbol Add { get; }

    /// <summary>
    /// The decision of <see cref="CallTargetClassifier"/> for the call, made on the element of the user's source and
    /// reported there.
    /// </summary>
    public CallTargetPlan Target { get; }

    /// <summary>The method that is called: the extension method in its static form.</summary>
    public IMethodSymbol Method => Add.ReducedFrom ?? Add;

    /// <summary>Whether the collection is passed with <c>&amp;</c> (a <c>this ref</c> receiver).</summary>
    public bool ReceiverByReference { get; }

    /// <summary>The arguments in parameter order, after the receiver; empty for a failure.</summary>
    public CollectionAddArgument[] Arguments { get; }

    /// <summary>An element that is written as the call of <paramref name="add"/>.</summary>
    public static CollectionAddPlan Call(IMethodSymbol add, CallTargetPlan target, bool receiverByReference,
        CollectionAddArgument[] arguments) =>
        new(CollectionAddFailure.None, add, target, receiverByReference, arguments);

    /// <summary>An element whose <paramref name="add"/> cannot be called, for <paramref name="failure"/>.</summary>
    public static CollectionAddPlan Failed(CollectionAddFailure failure, IMethodSymbol add, CallTargetPlan target) =>
        new(failure, add, target, receiverByReference: false, []);
}
