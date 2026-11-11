namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>A statement.</summary>
/// <param name="source">Where the node comes from.</param>
internal abstract class IrStatement(IrSource source) : IrNode(source);
