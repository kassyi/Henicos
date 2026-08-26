namespace Kassyi.GSharp.Compiler.Transpiler;

internal interface ITranspiler<in TNode> where TNode : SyntaxNode
{
    void Transpile(TNode node, ref TranspileContext context);
}
