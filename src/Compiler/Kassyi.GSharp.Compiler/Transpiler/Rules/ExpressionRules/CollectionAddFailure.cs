namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Why an element of a collection initializer has no call of its <c>Add</c> (<see cref="CollectionAddPlan"/>).
/// </summary>
internal enum CollectionAddFailure
{
    /// <summary>The element is written as the call of its <c>Add</c>.</summary>
    None,

    /// <summary>
    /// A written call of the same <c>Add</c> would not be emitted either (<see cref="CallTargetEmitter"/>): an
    /// instance method of a struct (#451), an extension method without <c>[GpuDeviceFunction]</c>.
    /// </summary>
    CallTargetReplaced,

    /// <summary>A generic <c>Add</c>, which has no instantiation for an element (GSH0003).</summary>
    Generic,

    /// <summary>
    /// An <c>Add</c> that G# does not emit as a device function, such as one the dialect maps to a built-in (GSH0003).
    /// </summary>
    NotEmitted,

    /// <summary>An <c>Add</c> with a <c>params</c> parameter (GSH0003).</summary>
    Params,

    /// <summary>An <c>Add</c> with optional parameters that the element omits (GSH0003).</summary>
    OmittedArguments,

    /// <summary>
    /// An <c>Add</c> that reaches a barrier, called through the resume protocol that lowering writes only for a written
    /// call (GSH0003).
    /// </summary>
    Suspending,
}
