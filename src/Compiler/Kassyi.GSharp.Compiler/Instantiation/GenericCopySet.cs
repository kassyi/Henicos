namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>The copies of one set of instances and the derived compilation that holds them.</summary>
/// <param name="Compilation">The compilation with every copy added next to its declaration.</param>
/// <param name="Copies">One per instance, in the order of the instances.</param>
internal sealed record GenericCopySet(Compilation Compilation, ImmutableArray<GenericCopy> Copies);
