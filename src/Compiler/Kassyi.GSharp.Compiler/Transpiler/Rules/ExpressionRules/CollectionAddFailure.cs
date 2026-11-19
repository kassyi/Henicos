namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Why an element of a collection initializer has no call of its <c>Add</c> (<see cref="CollectionAddPlan"/>).
/// </summary>
internal enum CollectionAddFailure
{
    /// <summary>The element is written as the call of its <c>Add</c>.</summary>
    None,

    /// <summary>
    /// A written call of the same <c>Add</c> would not be emitted either
    /// (<see cref="CallTargetEmitter.TryReplaceCall"/>): an instance method of a struct (#451), an extension method
    /// without <c>[GpuDeviceFunction]</c>.
    /// </summary>
    CallTargetReplaced,

    /// <summary>
    /// An <c>Add</c> that takes <c>params</c>, has optional parameters the element omits, or reaches a barrier, which
    /// has no call form outside a written call (GSH0003).
    /// </summary>
    Unsupported,
}
