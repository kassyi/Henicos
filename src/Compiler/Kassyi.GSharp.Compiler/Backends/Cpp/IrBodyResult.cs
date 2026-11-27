using Kassyi.GSharp.Compiler.Ir.Bodies;

namespace Kassyi.GSharp.Compiler.Backends.Cpp;

/// <summary>
/// The outcome of lowering an <see cref="IrBodyFront"/> for a C++ route: either an eligible <see cref="IrBody"/> or
/// the first reason the body is ineligible.
/// </summary>
internal sealed class IrBodyResult(IrBodyRoute route, IrBody? body, IrBodyIneligibility reason, string detail,
    ImmutableArray<IrSupportKey> unsupported)
{
    /// <summary>The route this result was produced for.</summary>
    public IrBodyRoute Route { get; } = route;

    /// <summary>The lowered IR body when <see cref="Reason"/> is <see cref="IrBodyIneligibility.None"/>.</summary>
    public IrBody? Body { get; } = (body is null) == (reason != IrBodyIneligibility.None)
        ? body
        : throw new ArgumentException("Body presence must match whether the reason is None.");

    /// <summary>
    /// Why the body did not take the IR route, or <see cref="IrBodyIneligibility.None"/> when eligible.
    /// </summary>
    public IrBodyIneligibility Reason { get; } = reason;

    /// <summary>The detail of <see cref="Reason"/>, or empty when the reason has none.</summary>
    public string Detail { get; } = detail;

    /// <summary>
    /// Every node key the route cannot spell in pre-order when <see cref="Reason"/> is
    /// <see cref="IrBodyIneligibility.UnsupportedForm"/>.
    /// </summary>
    public ImmutableArray<IrSupportKey> Unsupported { get; } = unsupported;

    /// <summary>Creates an eligible result for <paramref name="route"/> holding <paramref name="body"/>.</summary>
    public static IrBodyResult Eligible(IrBodyRoute route, IrBody body) =>
        new(route, body, IrBodyIneligibility.None, "", ImmutableArray<IrSupportKey>.Empty);

    /// <summary>Creates an ineligible result for <paramref name="route"/> with no unsupported keys.</summary>
    public static IrBodyResult Ineligible(IrBodyRoute route, IrBodyIneligibility reason, string detail) =>
        new(route, null, reason, detail, ImmutableArray<IrSupportKey>.Empty);
}
