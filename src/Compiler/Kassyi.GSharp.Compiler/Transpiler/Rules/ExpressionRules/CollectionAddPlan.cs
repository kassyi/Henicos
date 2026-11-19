namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// What an element of a collection initializer writes (<see cref="CollectionInitializerElements.Plan"/>): the call of
/// the <c>Add</c> that C# binds for it, or the reason it has none.
/// </summary>
/// <param name="Failure">
/// Why the element has no call; <see cref="CollectionAddFailure.None"/> when it is written.
/// </param>
/// <param name="Add">The <c>Add</c> C# binds, an extension method in its reduced form.</param>
/// <param name="Target">
/// The decision of <see cref="CallTargetClassifier"/> for the call, made on the element of the user's source and
/// reported there.
/// </param>
/// <param name="Method">The method that is called: the extension method in its static form.</param>
/// <param name="ReceiverByReference">
/// Whether the collection is passed with <c>&amp;</c> (a <c>this ref</c> receiver).
/// </param>
/// <param name="Arguments">The arguments in parameter order, after the receiver; empty for a failure.</param>
internal readonly record struct CollectionAddPlan(
    CollectionAddFailure Failure,
    IMethodSymbol Add,
    CallTargetPlan Target,
    IMethodSymbol Method,
    bool ReceiverByReference,
    CollectionAddArgument[] Arguments);
