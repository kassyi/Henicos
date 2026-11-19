namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>Managed execution routing and caller-info flags for an <see cref="Expressions.IrCall"/> (#1561).</summary>
/// <param name="RoutedMethod">The routed method symbol for managed execution, or <see langword="null"/>.</param>
/// <param name="AddsCallerInfo">Whether managed emission appends caller-info arguments.</param>
internal readonly record struct IrManagedCall(IMethodSymbol? RoutedMethod, bool AddsCallerInfo);
