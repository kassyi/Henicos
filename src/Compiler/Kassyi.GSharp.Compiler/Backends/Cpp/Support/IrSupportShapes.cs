using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Backends.Cpp.Support;

/// <summary>
/// The shape of a node that no row of the writer answers: the form of its decision where it has one, so that corpus
/// counts show which forms are most common before a row is added for them.
/// </summary>
internal static class IrSupportShapes
{
    /// <summary>No shape: the node has no decision that tells its forms apart.</summary>
    public const string None = "-";

    /// <summary>
    /// The shape of <paramref name="node"/>: the form of a conversion, the target of a call, the form of a property,
    /// the check of an element; <see cref="None"/> for every other node. The node must be decided.
    /// </summary>
    public static string Describe(IrNode node) => node switch
    {
        IrConversion conversion => conversion.Decided.Form.ToString(),
        IrCall call => call.Decided.Target is { } target ? target.ToString() : "TargetNull",
        IrPropertyReference property => property.Decided.Form.ToString(),
        IrElementReference element => element.Decided.Check.ToString(),
        _ => None
    };
}
