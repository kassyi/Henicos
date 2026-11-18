namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>Where a failing check reports: the node the check is at, the kind of the trap and the text.</summary>
/// <param name="Source">The source node where the check is located.</param>
/// <param name="TrapKind">The kind of the trap, which tells the site apart from another trap at the same place.</param>
/// <param name="Message">The text of the error message.</param>
internal readonly record struct IrErrorSite(IrSource Source, string TrapKind, string Message);
