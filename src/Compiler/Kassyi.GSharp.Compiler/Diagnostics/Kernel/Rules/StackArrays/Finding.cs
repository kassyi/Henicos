using Microsoft.CodeAnalysis;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.StackArrays;

/// <summary>A use that lets a storage outlive its scope: where to report it, the storage, and the use.</summary>
internal readonly record struct Finding(SyntaxNode Node, string Name, string Reason);
