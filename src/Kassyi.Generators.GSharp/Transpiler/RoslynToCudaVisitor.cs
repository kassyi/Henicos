using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.Generators.GSharp.Diagnostics;

namespace Kassyi.Generators.GSharp.Transpiler;

public class RoslynToCudaVisitor(SemanticModel semanticModel, Action<Diagnostic>? reportDiagnostic = null)
    : CSharpSyntaxWalker
{
    private SourceWriter _writer = new();
    private int _indentLevel;
    private readonly HashSet<INamedTypeSymbol> _usedStructs = new(SymbolEqualityComparer.Default);
    private bool _hasEmittedDynamicSmemBase;

    private static readonly Dictionary<string, Func<string, string>> s_apiMappings = new()
    {
        { "Math", name => name.ToLowerInvariant() },
        { "ThreadIndex", name => $"threadIdx.{name.ToLowerInvariant()}" },
        { "BlockIndex", name => $"blockIdx.{name.ToLowerInvariant()}" },
        { "BlockDimension", name => $"blockDim.{name.ToLowerInvariant()}" },
        { "GridDimension", name => $"gridDim.{name.ToLowerInvariant()}" },
        { "Gpu", name => name == "SyncThreads" ? "__syncthreads" : (name == "AtomicAdd" ? "atomicAdd" : name) }
    };

    public string GenerateCudaSource(MethodDeclarationSyntax method, bool isDevice = false)
    {
        var bodyWriter = new SourceWriter();
        var mainWriter = _writer;
        _writer = bodyWriter;

        _indentLevel = 0;
        _hasEmittedDynamicSmemBase = false;

        if (isDevice)
        {
            _writer.AppendKeyword("__device__");
            _writer.AppendSpace();
            var returnType = semanticModel.GetDeclaredSymbol(method)?.ReturnType;
            if (returnType != null && returnType.SpecialType != SpecialType.System_Void)
            {
                _writer.Append(MapType(returnType));
            }
            else
            {
                _writer.AppendKeyword("void");
            }
        }
        else
        {
            var returnType = semanticModel.GetDeclaredSymbol(method)?.ReturnType;
            if (returnType != null && returnType.SpecialType != SpecialType.System_Void)
            {
                reportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.KernelReturnMustBeVoid,
                    method.ReturnType.GetLocation(), method.Identifier.Text, returnType.ToDisplayString()));
            }

            _writer.AppendKeyword("extern \"C\" __global__ void");
        }

        _writer.AppendSpace();
        _writer.Append(method.Identifier.Text);
        using (_writer.Parentheses())
        {
            WriteParameters(method.ParameterList.Parameters);
        }

        using (_writer.Scope())
        {
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
                _writer.AppendSemiColon();
            }

            _indentLevel--;
        }

        _writer = mainWriter;

        // Write struct definitions
        foreach (var structSym in _usedStructs)
        {
            WriteStructDefinition(structSym);
        }

        var result = _writer + bodyWriter.ToString();
        _writer.Dispose();
        bodyWriter.Dispose();
        return result;
    }

    private void WriteParameters(SeparatedSyntaxList<ParameterSyntax> parameters)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            var typeSymbol = semanticModel.GetTypeInfo(p.Type!).Type;

            // Check if it's a custom struct
            var structType = typeSymbol;
            if (structType is IArrayTypeSymbol arrayType)
            {
                structType = arrayType.ElementType;
            }

            if (structType is { TypeKind: TypeKind.Struct } &&
                structType.ContainingNamespace.ToDisplayString() != "System")
            {
                _usedStructs.Add((INamedTypeSymbol)structType);
            }

            var cppType = MapType(typeSymbol);

            // In Cuda, arrays are pointers.
            if (typeSymbol is { TypeKind: TypeKind.Array })
            {
                cppType = MapType(((IArrayTypeSymbol)typeSymbol).ElementType) + "*";
            }

            // If it's a ref or out parameter, it's a pointer.
            var isPointer = false;
            if (p.Modifiers.Any(m => m.IsKind(SyntaxKind.RefKeyword) || m.IsKind(SyntaxKind.OutKeyword)))
            {
                cppType += "*";
                isPointer = true;
            }

            if (!isPointer && typeSymbol is { TypeKind: TypeKind.Struct } &&
                typeSymbol.ContainingNamespace.ToDisplayString() != "System")
            {
                var size = GetApproximateSize(typeSymbol);
                if (size >= 32)
                {
                    reportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.LargeStructByValueWarning,
                        p.GetLocation(), p.Identifier.Text, size));
                }
            }

            _writer.Append(cppType);
            _writer.AppendSpace();
            _writer.Append(p.Identifier.Text);
            if (i < parameters.Count - 1)
            {
                _writer.AppendComma();
            }
        }
    }

    private int GetApproximateSize(ITypeSymbol typeSymbol)
    {
        if (typeSymbol.SpecialType != SpecialType.None)
        {
            return typeSymbol.SpecialType switch
            {
                SpecialType.System_Boolean => 1,
                SpecialType.System_Byte => 1,
                SpecialType.System_SByte => 1,
                SpecialType.System_Int16 => 2,
                SpecialType.System_UInt16 => 2,
                SpecialType.System_Int32 => 4,
                SpecialType.System_UInt32 => 4,
                SpecialType.System_Single => 4,
                SpecialType.System_Int64 => 8,
                SpecialType.System_UInt64 => 8,
                SpecialType.System_Double => 8,
                _ => 4
            };
        }

        if (typeSymbol is not INamedTypeSymbol named || named.TypeKind != TypeKind.Struct)
        {
            return 8; // arrays/pointers
        }

        var size = 0;
        foreach (var field in named.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
        {
            size += GetApproximateSize(field.Type);
        }

        return size == 0 ? 1 : size;
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

        _writer.AppendSemiColon();
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
        _writer.AppendSemiColon();
    }

    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        Visit(node.Left);
        _writer.AppendOperator(node.OperatorToken.Text);
        Visit(node.Right);
    }

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        using (_writer.Parentheses())
        {
            Visit(node.Left);
            _writer.AppendOperator(node.OperatorToken.Text);
            Visit(node.Right);
        }
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node) => _writer.Append(node.Identifier.Text);

    public override void VisitLiteralExpression(LiteralExpressionSyntax node) => _writer.Append(node.Token.Text);

    public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
    {
        Visit(node.Expression);
        using (_writer.Brackets())
        {
            for (var i = 0; i < node.ArgumentList.Arguments.Count; i++)
            {
                Visit(node.ArgumentList.Arguments[i].Expression);
                if (i < node.ArgumentList.Arguments.Count - 1)
                {
                    _writer.AppendComma();
                }
            }
        }
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
        _writer.AppendKeyword("if");
        using (_writer.Parentheses())
        {
            Visit(node.Condition);
        }

        _writer.AppendLine();

        VisitStatementOrBlock(node.Statement);

        if (node.Else != null)
        {
            AppendIndent();
            _writer.AppendKeyword("else");
            _writer.AppendLine();
            VisitStatementOrBlock(node.Else.Statement);
        }
    }

    public override void VisitForStatement(ForStatementSyntax node)
    {
        _writer.AppendKeyword("for");
        using (_writer.Parentheses())
        {
            if (node.Declaration != null)
            {
                var type = MapType(semanticModel.GetTypeInfo(node.Declaration.Type).Type);
                _writer.Append(type);
                _writer.AppendSpace();
                for (var i = 0; i < node.Declaration.Variables.Count; i++)
                {
                    var v = node.Declaration.Variables[i];
                    _writer.Append(v.Identifier.Text);
                    if (v.Initializer != null)
                    {
                        _writer.AppendOperator("=");
                        Visit(v.Initializer.Value);
                    }

                    if (i < node.Declaration.Variables.Count - 1)
                    {
                        _writer.AppendComma();
                    }
                }
            }
            else
            {
                foreach (var init in node.Initializers)
                {
                    Visit(init);
                    _writer.AppendComma(); // Simplistic, might need trailing comma trim
                }
            }

            _writer.AppendSemiColon();

            if (node.Condition != null)
            {
                Visit(node.Condition);
            }

            _writer.AppendSemiColon();

            for (var i = 0; i < node.Incrementors.Count; i++)
            {
                Visit(node.Incrementors[i]);
                if (i < node.Incrementors.Count - 1)
                {
                    _writer.AppendComma();
                }
            }
        }

        _writer.AppendLine();
        VisitStatementOrBlock(node.Statement);
    }

    public override void VisitWhileStatement(WhileStatementSyntax node)
    {
        _writer.AppendKeyword("while");
        using (_writer.Parentheses())
        {
            Visit(node.Condition);
        }

        _writer.AppendLine();
        VisitStatementOrBlock(node.Statement);
    }

    public override void VisitDoStatement(DoStatementSyntax node)
    {
        _writer.AppendKeyword("do");
        _writer.AppendLine();
        VisitStatementOrBlock(node.Statement);
        AppendIndent();
        _writer.AppendKeyword("while");
        using (_writer.Parentheses())
        {
            Visit(node.Condition);
        }

        _writer.Append(";");
        _writer.AppendLine();
    }

    public override void VisitSwitchStatement(SwitchStatementSyntax node)
    {
        _writer.AppendKeyword("switch");
        using (_writer.Parentheses())
        {
            Visit(node.Expression);
        }

        _writer.AppendLine();
        AppendIndent();
        using (_writer.Scope())
        {
            _indentLevel++;
            foreach (var section in node.Sections)
            {
                VisitSwitchSection(section);
            }

            _indentLevel--;
            AppendIndent();
        }
    }

    public override void VisitSwitchSection(SwitchSectionSyntax node)
    {
        foreach (var label in node.Labels)
        {
            AppendIndent();
            Visit(label);
            _writer.AppendLine();
        }

        _indentLevel++;
        foreach (var stmt in node.Statements)
        {
            AppendIndent();
            Visit(stmt);
            _writer.AppendLine();
        }

        _indentLevel--;
    }

    public override void VisitCaseSwitchLabel(CaseSwitchLabelSyntax node)
    {
        _writer.AppendKeyword("case");
        _writer.AppendSpace();
        Visit(node.Value);
        _writer.Append(":");
    }

    public override void VisitDefaultSwitchLabel(DefaultSwitchLabelSyntax node) => _writer.Append("default:");

    public override void VisitBreakStatement(BreakStatementSyntax node)
    {
        _writer.AppendKeyword("break");
        _writer.Append(";");
    }

    public override void VisitReturnStatement(ReturnStatementSyntax node)
    {
        _writer.AppendKeyword("return");
        if (node.Expression != null)
        {
            _writer.AppendSpace();
            Visit(node.Expression);
        }

        _writer.AppendSemiColon();
    }

    public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        var typeSymbol = semanticModel.GetTypeInfo(node.Declaration.Type).Type;
        if (typeSymbol?.Name == "SharedMemory" &&
            typeSymbol.ContainingNamespace.ToDisplayString() == "Kassyi.GSharp.Core")
        {
            ProcessSharedMemoryDeclaration(node, (INamedTypeSymbol)typeSymbol);
            return;
        }

        VisitVariableDeclaration(node.Declaration);
        _writer.AppendSemiColon();
    }

    private void ProcessSharedMemoryDeclaration(LocalDeclarationStatementSyntax node, INamedTypeSymbol typeSymbol)
    {
        var typeArg = typeSymbol.TypeArguments[0];
        var cppElementType = MapType(typeArg);

        for (var i = 0; i < node.Declaration.Variables.Count; i++)
        {
            var v = node.Declaration.Variables[i];
            var sizeArg = (v.Initializer?.Value as ObjectCreationExpressionSyntax)?.ArgumentList?.Arguments
                .FirstOrDefault()?.Expression;
            if (sizeArg == null && v.Initializer?.Value is ImplicitObjectCreationExpressionSyntax implCreation)
            {
                sizeArg = implCreation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
            }

            if (sizeArg == null)
            {
                continue;
            }

            var constantValue = semanticModel.GetConstantValue(sizeArg);
            if (constantValue.HasValue)
            {
                WriteStaticSharedMemory(v, sizeArg, constantValue.Value!, cppElementType, typeArg, node, i);
            }
            else
            {
                WriteDynamicSharedMemory(v, sizeArg, cppElementType, node, i);
            }
        }
    }

    private void WriteStaticSharedMemory(VariableDeclaratorSyntax v, ExpressionSyntax sizeArg, object constantValue, string cppElementType, ITypeSymbol typeArg, LocalDeclarationStatementSyntax node, int i)
    {
        var elemSize = GetApproximateSize(typeArg);
        var byteSize = (int)constantValue * elemSize;
        if (byteSize > 32768)
        {
            reportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.ExcessiveSharedMemoryWarning,
                sizeArg.GetLocation(), byteSize, 32768));
        }

        _writer.AppendKeyword("__shared__");
        _writer.AppendSpace();
        _writer.Append(cppElementType);
        _writer.AppendSpace();
        _writer.Append(v.Identifier.Text);
        using (_writer.Brackets())
        {
            _writer.Append(constantValue.ToString());
        }

        _writer.Append(";");
        if (i < node.Declaration.Variables.Count - 1)
        {
            _writer.AppendLine();
            AppendIndent();
        }
    }

    private void WriteDynamicSharedMemory(VariableDeclaratorSyntax v, ExpressionSyntax sizeArg, string cppElementType, LocalDeclarationStatementSyntax node, int i)
    {
        if (!_hasEmittedDynamicSmemBase)
        {
            _writer.AppendLine("extern __shared__ char __gsharp_dynamic_smem[];");
            AppendIndent();
            _writer.AppendLine("size_t __smem_offset = 0;");
            AppendIndent();
            _hasEmittedDynamicSmemBase = true;
        }

        _writer.Append(
            $"__smem_offset = (__smem_offset + alignof({cppElementType}) - 1) & ~(alignof({cppElementType}) - 1);");
        _writer.AppendLine();
        AppendIndent();
        _writer.Append(
            $"{cppElementType}* {v.Identifier.Text} = ({cppElementType}*)(__gsharp_dynamic_smem + __smem_offset);");
        _writer.AppendLine();
        AppendIndent();
        _writer.Append("__smem_offset += ");
        using (_writer.Parentheses())
        {
            Visit(sizeArg);
        }

        _writer.Append($" * sizeof({cppElementType});");

        if (i < node.Declaration.Variables.Count - 1)
        {
            _writer.AppendLine();
            AppendIndent();
        }
    }

    public override void VisitVariableDeclaration(VariableDeclarationSyntax node)
    {
        var typeSymbol = semanticModel.GetTypeInfo(node.Type).Type;
        if (typeSymbol is { TypeKind: TypeKind.Struct } &&
            typeSymbol.ContainingNamespace.ToDisplayString() != "System")
        {
            _usedStructs.Add((INamedTypeSymbol)typeSymbol);
        }

        var cppType = MapType(typeSymbol);

        for (var i = 0; i < node.Variables.Count; i++)
        {
            var v = node.Variables[i];

            _writer.Append(cppType);
            _writer.AppendSpace();
            _writer.Append(v.Identifier.Text);

            if (v.Initializer != null)
            {
                _writer.AppendOperator("=");
                Visit(v.Initializer.Value);
            }

            if (i < node.Variables.Count - 1)
            {
                _writer.AppendComma();
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
        var symbol = semanticModel.GetSymbolInfo(node).Symbol;
        if (symbol is { ContainingType: not null })
        {
            var typeName = symbol.ContainingType.Name;
            if (s_apiMappings.TryGetValue(typeName, out var mapper))
            {
                _writer.Append(mapper(node.Name.Identifier.Text));
                return;
            }
        }

        Visit(node.Expression);
        _writer.Append(".");
        _writer.Append(node.Name.Identifier.Text);
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        Visit(node.Expression);
        using (_writer.Parentheses())
        {
            for (var i = 0; i < node.ArgumentList.Arguments.Count; i++)
            {
                Visit(node.ArgumentList.Arguments[i]);
                if (i < node.ArgumentList.Arguments.Count - 1)
                {
                    _writer.AppendComma();
                }
            }
        }
    }

    public override void VisitArgument(ArgumentSyntax node)
    {
        if (node.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || node.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
        {
            _writer.Append("&");
        }

        Visit(node.Expression);
    }

    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        var typeSymbol = semanticModel.GetTypeInfo(node).Type;
        if (typeSymbol is { Name: "SharedMemory" } &&
            typeSymbol.ContainingNamespace.ToDisplayString() == "Kassyi.GSharp.Core")
        {
            base.VisitObjectCreationExpression(node);
            return;
        }

        reportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.ObjectCreationNotSupported, node.GetLocation()));
        base.VisitObjectCreationExpression(node);
    }

    public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
    {
        var typeSymbol = semanticModel.GetTypeInfo(node).Type;
        if (typeSymbol is { Name: "SharedMemory" } &&
            typeSymbol.ContainingNamespace.ToDisplayString() == "Kassyi.GSharp.Core")
        {
            base.VisitImplicitObjectCreationExpression(node);
            return;
        }

        reportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.ObjectCreationNotSupported, node.GetLocation()));
        base.VisitImplicitObjectCreationExpression(node);
    }

    public override void VisitPredefinedType(PredefinedTypeSyntax node)
    {
        if (node.Keyword.IsKind(SyntaxKind.StringKeyword))
        {
            reportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.StringNotSupported, node.GetLocation()));
        }

        base.VisitPredefinedType(node);
    }
}
