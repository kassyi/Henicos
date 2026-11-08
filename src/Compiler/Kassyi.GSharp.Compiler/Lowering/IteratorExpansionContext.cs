namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>The inputs that every iterator expansion of one method shares (<see cref="IteratorInliner"/>).</summary>
/// <param name="SemanticModel">The model of the consumer's syntax tree, which binds the iterator body too.</param>
/// <param name="Dialect">The dialect whose C++ names the backing arguments of an iterator local use.</param>
/// <param name="Reporter">Reports an iterator that cannot be expanded.</param>
internal readonly record struct IteratorExpansionContext(
    SemanticModel SemanticModel,
    ITranspilerDialect Dialect,
    Reporter Reporter);
