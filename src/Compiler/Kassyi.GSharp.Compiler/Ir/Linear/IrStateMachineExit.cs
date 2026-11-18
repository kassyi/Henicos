namespace Kassyi.GSharp.Compiler.Ir.Linear;

/// <summary>
/// The end of a state-machine body: <c>__gsharp_state = -1; return;</c> for a kernel or <c>return;</c> for a
/// device function.
/// </summary>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrStateMachineExit(IrSource source) : IrStatement(source);
