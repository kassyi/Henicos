namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>One callee reached from a body and the syntax of the operation that makes the call.</summary>
/// <param name="Callee">The called method, constructor, accessor, conversion, operator, or method-group target.</param>
/// <param name="Site">The syntax node of the operation that makes or references the call.</param>
internal readonly record struct CalleeSite(IMethodSymbol Callee, SyntaxNode Site)
{
    /// <summary>
    /// Whether <paramref name="site"/> is the target of an operator macro (<c>[GpuOperatorMacro]</c>, including
    /// <c>&amp;&amp;</c>/<c>||</c> over a user-defined <c>&amp;</c>/<c>|</c>), which the stack paths do not yet
    /// follow.
    /// </summary>
    // E4-2 removes this skip
    public static bool IsOperatorMacro(CalleeSite site, Compilation compilation) =>
        site.Site is BinaryExpressionSyntax or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax &&
        site.Callee.MethodKind == MethodKind.Ordinary &&
        OperatorMacroSyntax.GetMacroAttribute(
            OperatorMacroSyntax.GetOperator(
                (ExpressionSyntax)site.Site,
                compilation.GetCachedSemanticModel(site.Site.SyntaxTree)
            ).Method
        ) != null;
}
