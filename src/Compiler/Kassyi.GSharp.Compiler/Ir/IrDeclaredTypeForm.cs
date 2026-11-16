namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>
/// How the source declares a variable's type: a fact about the source, not how a writer spells a declaration.
/// A declaration without an initializer names the type whatever this says
/// (<see cref="Statements.IrLocalDeclaration.DeducesType"/>).
/// </summary>
internal enum IrDeclaredTypeForm
{
    /// <summary>
    /// Written as <see langword="var"/> (also <c>ref var</c>, <c>scoped var</c>, <c>out var</c>, a <c>var</c> pattern),
    /// or a temporary of a pass.
    /// </summary>
    Deduced,

    /// <summary>
    /// Written with its type, or with no declaration in the source: a parameter, <see langword="this"/>, a local
    /// without declaring syntax.
    /// </summary>
    Named
}
