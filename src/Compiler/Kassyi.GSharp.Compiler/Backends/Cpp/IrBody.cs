using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Bodies;
using Kassyi.GSharp.Compiler.Ir.Decisions;

namespace Kassyi.GSharp.Compiler.Backends.Cpp;

/// <summary>
/// The lowered IR body of a method for one C++ route: front-end result, route profile, decided body, sequenced body,
/// and decision context.
/// </summary>
internal sealed class IrBody(IrBodyFront front, CppBodyProfile profile, IrMethod decided, IrMethod sequenced,
    IrDecisionContext decisions)
{
    /// <summary>The shared front-end result.</summary>
    public IrBodyFront Front { get; } = front;

    /// <summary>The route profile this body was lowered for.</summary>
    public CppBodyProfile Profile { get; } = profile;

    /// <summary>The decided IR body.</summary>
    public IrMethod Decided { get; } = decided;

    /// <summary>
    /// The sequenced IR body read by the C++ writer; same instance as <see cref="Decided"/> when unchanged.
    /// </summary>
    public IrMethod Sequenced { get; } = sequenced;

    /// <summary>The decision context used to decide this body.</summary>
    public IrDecisionContext Decisions { get; } = decisions;
}
