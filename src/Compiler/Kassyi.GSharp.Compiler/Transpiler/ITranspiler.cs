namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Contract for AST node transpilation handlers.</summary>
internal interface ITranspiler<in TNode> where TNode : SyntaxNode
{
    void Transpile(TNode node, ref TranspileContext context);
}
