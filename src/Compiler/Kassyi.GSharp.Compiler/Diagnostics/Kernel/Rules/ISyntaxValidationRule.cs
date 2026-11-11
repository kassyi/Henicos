using Microsoft.CodeAnalysis;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

internal interface ISyntaxValidationRule<in TSyntax> : IValidationRule where TSyntax : SyntaxNode
{
    void Analyze(TSyntax node, ref ValidationContext context);
}
