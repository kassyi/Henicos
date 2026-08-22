using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.Generators.GSharp.Transpiler;

public class RoslynToCudaVisitor : CSharpSyntaxWalker
{
    private readonly SemanticModel _semanticModel;
    private SourceWriter _writer;
    private int _indentLevel = 0;
    private readonly HashSet<INamedTypeSymbol> _usedStructs = new(SymbolEqualityComparer.Default);

    public RoslynToCudaVisitor(SemanticModel semanticModel)
    {
        _semanticModel = semanticModel;
        _writer = new SourceWriter();
    }

    public string GenerateCudaSource(MethodDeclarationSyntax method)
    {
        var bodyWriter = new SourceWriter();
        var mainWriter = _writer;
        _writer = bodyWriter;

        _indentLevel = 0;

        _writer.Append("extern \"C\" __global__ void ");
        _writer.Append(method.Identifier.Text);
        _writer.Append("(");

        var parameters = method.ParameterList.Parameters;
        for (var i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            var typeSymbol = _semanticModel.GetTypeInfo(p.Type!).Type;

            // Check if it's a custom struct
            var structType = typeSymbol;
            if (structType is IArrayTypeSymbol arrayType)
                structType = arrayType.ElementType;
            if (structType != null && structType.TypeKind == TypeKind.Struct &&
                structType.ContainingNamespace.ToDisplayString() != "System")
            {
                _usedStructs.Add((INamedTypeSymbol)structType);
            }

            var cppType = MapType(typeSymbol);

            // In Cuda, arrays are pointers.
            if (typeSymbol != null && typeSymbol.TypeKind == TypeKind.Array)
            {
                cppType = MapType(((IArrayTypeSymbol)typeSymbol).ElementType) + "*";
            }

            // If it's a ref or out parameter, it's a pointer.
            if (p.Modifiers.Any(m => m.IsKind(SyntaxKind.RefKeyword) || m.IsKind(SyntaxKind.OutKeyword)))
            {
                cppType += "*";
            }

            _writer.Append($"{cppType} {p.Identifier.Text}");
            if (i < parameters.Count - 1)
            {
                _writer.Append(", ");
            }
        }

        _writer.AppendLine(") {");
        _indentLevel++;

        if (method.Body != null)
        {
            Visit(method.Body);
        }
        else if (method.ExpressionBody != null)
        {
            AppendIndent();
            // Assuming the expression body is something simple. A real transpile needs to handle returning or assigning.
            // For now, we only visit the expression.
            Visit(method.ExpressionBody.Expression);
            _writer.AppendLine(";");
        }

        _indentLevel--;
        _writer.AppendLine("}");

        _writer = mainWriter;

        // Write struct definitions
        foreach (var structSym in _usedStructs)
        {
            WriteStructDefinition(structSym);
        }

        var result = _writer.ToString() + bodyWriter.ToString();
        _writer.Dispose();
        bodyWriter.Dispose();
        return result;
    }

    private void WriteStructDefinition(INamedTypeSymbol structSym)
    {
        _writer.AppendLine($"struct {structSym.Name}");
        using (_writer.Scope())
        {
            foreach (var member in structSym.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
            {
                _writer.AppendLine($"{MapType(member.Type)} {member.Name};");
            }
        }

        _writer.AppendLine(";");
        _writer.AppendLine();
    }

    private string MapType(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol == null)
        {
            return "void";
        }

        return typeSymbol.SpecialType switch
        {
            SpecialType.System_Void => "void",
            SpecialType.System_Boolean => "bool",
            SpecialType.System_Byte => "unsigned char",
            SpecialType.System_SByte => "char",
            SpecialType.System_Int16 => "short",
            SpecialType.System_UInt16 => "unsigned short",
            SpecialType.System_Int32 => "int",
            SpecialType.System_UInt32 => "unsigned int",
            SpecialType.System_Int64 => "long long",
            SpecialType.System_UInt64 => "unsigned long long",
            SpecialType.System_Single => "float",
            SpecialType.System_Double => "double",
            _ => typeSymbol.Name
        };
    }

    private void AppendIndent() => _writer.Append(new string(' ', _indentLevel * 4));

    public override void VisitBlock(BlockSyntax node)
    {
        foreach (var statement in node.Statements)
        {
            AppendIndent();
            Visit(statement);
            _writer.AppendLine();
        }
    }

    public override void VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        Visit(node.Expression);
        _writer.Append(";");
    }

    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        Visit(node.Left);
        _writer.Append($" {node.OperatorToken.Text} ");
        Visit(node.Right);
    }

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        _writer.Append("(");
        Visit(node.Left);
        _writer.Append($" {node.OperatorToken.Text} ");
        Visit(node.Right);
        _writer.Append(")");
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node) => _writer.Append(node.Identifier.Text);

    public override void VisitLiteralExpression(LiteralExpressionSyntax node) => _writer.Append(node.Token.Text);

    public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
    {
        Visit(node.Expression);
        _writer.Append("[");
        foreach (var arg in node.ArgumentList.Arguments)
        {
            Visit(arg.Expression);
        }

        _writer.Append("]");
    }

    public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
    {
        // For pointers: *a
        if (node.IsKind(SyntaxKind.PointerIndirectionExpression))
        {
            _writer.Append("*");
            Visit(node.Operand);
        }
        else
        {
            _writer.Append(node.OperatorToken.Text);
            Visit(node.Operand);
        }
    }

    public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
    {
        Visit(node.Operand);
        _writer.Append(node.OperatorToken.Text);
    }

    public override void VisitIfStatement(IfStatementSyntax node)
    {
        _writer.Append("if (");
        Visit(node.Condition);
        _writer.AppendLine(")");

        VisitStatementOrBlock(node.Statement);

        if (node.Else != null)
        {
            AppendIndent();
            _writer.AppendLine("else");
            VisitStatementOrBlock(node.Else.Statement);
        }
    }

    public override void VisitForStatement(ForStatementSyntax node)
    {
        _writer.Append("for (");
        if (node.Declaration != null)
        {
            // E.g., int i = 0
            var type = MapType(_semanticModel.GetTypeInfo(node.Declaration.Type).Type);
            _writer.Append(type + " ");
            for (var i = 0; i < node.Declaration.Variables.Count; i++)
            {
                var v = node.Declaration.Variables[i];
                _writer.Append(v.Identifier.Text);
                if (v.Initializer != null)
                {
                    _writer.Append(" = ");
                    Visit(v.Initializer.Value);
                }

                if (i < node.Declaration.Variables.Count - 1)
                {
                    _writer.Append(", ");
                }
            }
        }
        else
        {
            foreach (var init in node.Initializers)
            {
                Visit(init);
                _writer.Append(", "); // Simplistic, might need trailing comma trim
            }
        }

        _writer.Append("; ");

        if (node.Condition != null)
        {
            Visit(node.Condition);
        }

        _writer.Append("; ");

        for (var i = 0; i < node.Incrementors.Count; i++)
        {
            Visit(node.Incrementors[i]);
            if (i < node.Incrementors.Count - 1)
            {
                _writer.Append(", ");
            }
        }

        _writer.AppendLine(")");
        VisitStatementOrBlock(node.Statement);
    }

    public override void VisitWhileStatement(WhileStatementSyntax node)
    {
        _writer.Append("while (");
        Visit(node.Condition);
        _writer.AppendLine(")");
        VisitStatementOrBlock(node.Statement);
    }

    public override void VisitReturnStatement(ReturnStatementSyntax node)
    {
        _writer.Append("return");
        if (node.Expression != null)
        {
            _writer.Append(" ");
            Visit(node.Expression);
        }

        _writer.Append(";");
    }

    public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        VisitVariableDeclaration(node.Declaration);
        _writer.Append(";");
    }

    public override void VisitVariableDeclaration(VariableDeclarationSyntax node)
    {
        var typeSymbol = _semanticModel.GetTypeInfo(node.Type).Type;
        if (typeSymbol != null && typeSymbol.TypeKind == TypeKind.Struct &&
            typeSymbol.ContainingNamespace.ToDisplayString() != "System")
        {
            _usedStructs.Add((INamedTypeSymbol)typeSymbol);
        }

        var cppType = MapType(typeSymbol);

        for (var i = 0; i < node.Variables.Count; i++)
        {
            var v = node.Variables[i];

            _writer.Append($"{cppType} {v.Identifier.Text}");

            if (v.Initializer != null)
            {
                _writer.Append(" = ");
                Visit(v.Initializer.Value);
            }

            if (i < node.Variables.Count - 1)
            {
                _writer.Append(", ");
            }
        }
    }

    private void VisitStatementOrBlock(StatementSyntax statement)
    {
        if (statement is BlockSyntax block)
        {
            AppendIndent();
            using (_writer.Scope())
            {
                _indentLevel++;
                foreach (var stmt in block.Statements)
                {
                    AppendIndent();
                    Visit(stmt);
                    _writer.AppendLine();
                }

                _indentLevel--;
                AppendIndent();
            }
        }
        else
        {
            _indentLevel++;
            AppendIndent();
            Visit(statement);
            _writer.AppendLine();
            _indentLevel--;
        }
    }

    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
        if (symbol != null && symbol.ContainingType != null)
        {
            var typeName = symbol.ContainingType.Name;

            // Map Math
            if (typeName == "Math")
            {
                _writer.Append(node.Name.Identifier.Text.ToLowerInvariant());
                return;
            }

            // Map CUDA Intrinsics
            if (typeName == "ThreadIdx")
            {
                _writer.Append($"threadIdx.{node.Name.Identifier.Text.ToLowerInvariant()}");
                return;
            }

            if (typeName == "BlockIdx")
            {
                _writer.Append($"blockIdx.{node.Name.Identifier.Text.ToLowerInvariant()}");
                return;
            }

            if (typeName == "BlockDim")
            {
                _writer.Append($"blockDim.{node.Name.Identifier.Text.ToLowerInvariant()}");
                return;
            }

            if (typeName == "GridDim")
            {
                _writer.Append($"gridDim.{node.Name.Identifier.Text.ToLowerInvariant()}");
                return;
            }

            // Map Gpu Intrinsics
            if (typeName == "Gpu")
            {
                if (node.Name.Identifier.Text == "SyncThreads")
                {
                    _writer.Append("__syncthreads");
                    return;
                }

                if (node.Name.Identifier.Text == "AtomicAdd")
                {
                    _writer.Append("atomicAdd");
                    return;
                }
            }
        }

        Visit(node.Expression);
        _writer.Append(".");
        _writer.Append(node.Name.Identifier.Text);
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        Visit(node.Expression);
        _writer.Append("(");
        for (int i = 0; i < node.ArgumentList.Arguments.Count; i++)
        {
            Visit(node.ArgumentList.Arguments[i]);
            if (i < node.ArgumentList.Arguments.Count - 1)
                _writer.Append(", ");
        }

        _writer.Append(")");
    }

    public override void VisitArgument(ArgumentSyntax node)
    {
        if (node.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || node.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
        {
            _writer.Append("&");
        }

        Visit(node.Expression);
    }
}
