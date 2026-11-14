namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.ReadOnlyArrays;

/// <summary>A write to a read-only array: where to report it, the array's name, and the form of the write.</summary>
internal readonly record struct ReadOnlyArrayWrite(SyntaxNode Node, string Name, string Reason);
