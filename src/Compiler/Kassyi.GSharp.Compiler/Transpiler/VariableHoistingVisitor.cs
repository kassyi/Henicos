using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Compiler.Transpiler;

public struct HoistedVariable
{
    public ITypeSymbol Type;
    public string Name;
}

public class VariableHoistingVisitor : CSharpSyntaxWalker
{
    private readonly SemanticModel _semanticModel;
    public List<HoistedVariable> Variables { get; } = new();
    public int YieldCount { get; private set; } = 0;

    public VariableHoistingVisitor(SemanticModel semanticModel)
    {
        _semanticModel = semanticModel;
    }

    public override void VisitVariableDeclaration(VariableDeclarationSyntax node)
    {
        foreach (var variable in node.Variables)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(variable) as ILocalSymbol;
            if (symbol != null)
            {
                Variables.Add(new HoistedVariable { Type = symbol.Type, Name = variable.Identifier.Text });
            }
        }
        base.VisitVariableDeclaration(node);
    }

    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        var symbol = _semanticModel.GetDeclaredSymbol(node) as ILocalSymbol;
        if (symbol != null)
        {
            Variables.Add(new HoistedVariable { Type = symbol.Type, Name = node.Identifier.Text });
        }
        base.VisitForEachStatement(node);
    }
    
    public override void VisitYieldStatement(YieldStatementSyntax node)
    {
        if (node.IsKind(SyntaxKind.YieldReturnStatement))
        {
            YieldCount++;
        }
        base.VisitYieldStatement(node);
    }
}
