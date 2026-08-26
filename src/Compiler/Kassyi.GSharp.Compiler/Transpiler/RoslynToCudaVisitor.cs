namespace Kassyi.GSharp.Compiler.Transpiler;

public partial class RoslynToCudaVisitor(SemanticModel semanticModel, Action<Diagnostic>? reportDiagnostic = null)
    : CSharpSyntaxWalker
{
    internal TranspileContext _context;

    public readonly struct OverrideValue
    {
        public readonly string? StringValue;
        public readonly SyntaxNode? NodeValue;

        public OverrideValue(string stringValue)
        {
            StringValue = stringValue;
            NodeValue = null;
        }

        public OverrideValue(SyntaxNode nodeValue)
        {
            NodeValue = nodeValue;
            StringValue = null;
        }

        public void WriteTo(RoslynToCudaVisitor visitor)
        {
            if (NodeValue != null)
            {
                visitor.Visit(NodeValue);
            }
            else if (StringValue != null)
            {
                visitor._context.Writer.Append(StringValue);
            }
        }
    }

    public string GenerateCudaSource(MethodDeclarationSyntax method, bool isDevice = false)
    {
        var mainWriter = new SourceWriter();
        var bodyWriter = new SourceWriter();
        _context = new TranspileContext(
            bodyWriter,
            new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default),
            new Dictionary<string, OverrideValue>(),
            semanticModel,
            reportDiagnostic,
            this);

        WriteMethodSignature(method, isDevice);
        WriteMethodBody(method);

        _context.Writer = mainWriter;

        // Write struct definitions
        var sortedStructs = GetTopologicallySortedStructs();
        foreach (var structSym in sortedStructs)
        {
            WriteStructDefinition(structSym);
        }

        var result = mainWriter.ToString() + bodyWriter.ToString();
        mainWriter.Dispose();
        bodyWriter.Dispose();
        return result;
    }

    public string GenerateCudaSignature(MethodDeclarationSyntax method, bool isDevice = false)
    {
        var mainWriter = new SourceWriter();
        var sigWriter = new SourceWriter();
        _context = new TranspileContext(
            sigWriter,
            new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default),
            new Dictionary<string, OverrideValue>(),
            semanticModel,
            reportDiagnostic,
            this);
        WriteMethodSignature(method, isDevice);
        _context.Writer.Append(";");
        _context.Writer.AppendLine();

        _context.Writer = mainWriter;
        foreach (var structSym in GetTopologicallySortedStructs())
        {
            var structName = CudaTypeMapper.GetFullyQualifiedCppName(structSym);
            _context.Writer.AppendLine($"struct {structName};");
        }

        _context.Writer.Append(sigWriter.ToString());

        var result = mainWriter.ToString();
        mainWriter.Dispose();
        sigWriter.Dispose();
        return result;
    }

    private void WriteMethodSignature(MethodDeclarationSyntax method, bool isDevice)
    {
        var methodSymbol = _context.SemanticModel.GetDeclaredSymbol(method);

        if (isDevice)
        {
            WriteDeviceMethodPrefix(methodSymbol);
        }
        else
        {
            WriteKernelMethodPrefix(method, methodSymbol);
        }

        _context.Writer.AppendSpace();
        _context.Writer.Append(method.Identifier.Text);
        using (_context.Writer.Parentheses())
        {
            WriteParameters(method.ParameterList.Parameters);
        }
    }

    private void WriteDeviceMethodPrefix(IMethodSymbol? methodSymbol)
    {
        var hasAggressiveInlining = false;
        var hasNoInlining = false;

        if (methodSymbol != null)
        {
            foreach (var attr in methodSymbol.GetAttributes())
            {
                switch (attr.AttributeClass?.Name)
                {
                    case KnownAttributes.GpuInlineAttribute:

                        hasAggressiveInlining = true;
                        break;
                    case KnownAttributes.GpuNoInlineAttribute:

                        hasNoInlining = true;
                        break;
                }
            }
        }

        if (hasAggressiveInlining)
        {
            _context.Writer.AppendKeyword("__forceinline__");
        }
        else if (hasNoInlining)
        {
            _context.Writer.AppendKeyword("__noinline__");
        }
        else
        {
            _context.Writer.AppendKeyword("inline");
        }

        _context.Writer.AppendSpace();
        _context.Writer.AppendKeyword("__device__");
        _context.Writer.AppendSpace();

        var returnType = methodSymbol?.ReturnType;
        if (returnType != null && returnType.SpecialType != SpecialType.System_Void)
        {
            _context.Writer.Append(CudaTypeMapper.MapType(returnType));
        }
        else
        {
            _context.Writer.AppendKeyword("void");
        }
    }

    private void WriteKernelMethodPrefix(MethodDeclarationSyntax method, IMethodSymbol? methodSymbol)
    {
        var returnType = methodSymbol?.ReturnType;
        if (returnType != null && returnType.SpecialType != SpecialType.System_Void)
        {
            _context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.KernelReturnMustBeVoid,
                method.ReturnType.GetLocation(), method.Identifier.Text, returnType.ToDisplayString()));
        }

        _context.Writer.AppendKeyword("extern \"C\" __global__ void");
    }

    private void WriteMethodBody(MethodDeclarationSyntax method)
    {
        using (_context.Writer.Scope())
        {
            if (method.Body != null)
            {
                Visit(method.Body);
            }
            else if (method.ExpressionBody != null)
            {
                var methodSymbol = _context.SemanticModel.GetDeclaredSymbol(method);
                if (methodSymbol is { ReturnsVoid: false })
                {
                    _context.Writer.AppendKeyword("return");
                    _context.Writer.AppendSpace();
                }

                Visit(method.ExpressionBody.Expression);
                _context.Writer.AppendSemiColon();
                _context.Writer.AppendLine();
            }
        }
    }

    private void WriteParameters(SeparatedSyntaxList<ParameterSyntax> parameters)
    {
        var validParameters = new List<ParameterSyntax>(parameters.Count);
        foreach (var p in parameters)
        {
            if (!IsJitConstant(p))
            {
                validParameters.Add(p);
            }
        }

        for (var i = 0; i < validParameters.Count; i++)
        {
            var p = validParameters[i];
            var typeSymbol = _context.SemanticModel.GetTypeInfo(p.Type!).Type;

            RegisterUsedStructIfApplicable(typeSymbol);
            var (cppType, isPointer) = CudaTypeMapper.GetParameterCppType(p, typeSymbol);
            CheckLargeStructByValue(p, typeSymbol, isPointer);

            var paramSymbol = _context.SemanticModel.GetDeclaredSymbol(p);
            if (paramSymbol != null &&
                paramSymbol.GetAttributes().Any(a => KnownAttributes.IsGpuConst(a.AttributeClass?.Name)))
            {
                _context.Writer.AppendKeyword("const");
                _context.Writer.AppendSpace();
            }

            _context.Writer.Append(cppType);
            _context.Writer.AppendSpace();
            _context.Writer.Append(p.Identifier.Text);
            if (i < validParameters.Count - 1)
            {
                _context.Writer.AppendComma();
            }
        }
    }

    private bool IsJitConstant(ParameterSyntax p)
    {
        var paramSymbol = _context.SemanticModel.GetDeclaredSymbol(p);
        if (paramSymbol == null)
        {
            return false;
        }

        foreach (var attr in paramSymbol.GetAttributes())
        {
            if (KnownAttributes.IsJitConstant(attr.AttributeClass?.Name))
            {
                return true;
            }
        }

        return false;
    }

    private void RegisterUsedStructIfApplicable(ITypeSymbol? typeSymbol)
    {
        var structType = typeSymbol is IArrayTypeSymbol arrayType ? arrayType.ElementType : typeSymbol;
        if (structType is not INamedTypeSymbol { TypeKind: TypeKind.Struct } namedStruct ||
            namedStruct.ContainingNamespace.ToDisplayString() == "System")
        {
            return;
        }

        if (!_context.UsedStructs.Add(namedStruct))
        {
            return;
        }

        foreach (var member in namedStruct.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
        {
            RegisterUsedStructIfApplicable(member.Type);
        }
    }

    private IEnumerable<INamedTypeSymbol> GetTopologicallySortedStructs()
    {
        var result = new List<INamedTypeSymbol>();
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var visiting = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var s in _context.UsedStructs)
        {
            visit(s);
        }

        return result;

        void visit(INamedTypeSymbol s)
        {
            if (visited.Contains(s))
            {
                return;
            }

            if (!visiting.Add(s))
            {
                return;
            }

            foreach (var member in s.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
            {
                var fieldType = member.Type is IArrayTypeSymbol arrayType ? arrayType.ElementType : member.Type;
                if (fieldType is not INamedTypeSymbol { TypeKind: TypeKind.Struct } namedStruct ||
                    namedStruct.ContainingNamespace.ToDisplayString() == "System")
                {
                    continue;
                }

                if (_context.UsedStructs.Contains(namedStruct))
                {
                    visit(namedStruct);
                }
            }

            visiting.Remove(s);
            visited.Add(s);
            result.Add(s);
        }
    }

    private void CheckLargeStructByValue(ParameterSyntax p, ITypeSymbol? typeSymbol, bool isPointer)
    {
        if (isPointer || typeSymbol is not { TypeKind: TypeKind.Struct } ||
            typeSymbol.ContainingNamespace.ToDisplayString() == TypeConstants.SystemNamespace)
        {
            return;
        }

        var size = CudaTypeMapper.GetApproximateSize(typeSymbol);
        if (size >= 32)
        {
            _context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.LargeStructByValueWarning,
                p.GetLocation(), p.Identifier.Text, size));
        }
    }

    private void WriteStructDefinition(INamedTypeSymbol structSym)
    {
        var inlineArrayAttr = structSym.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "InlineArrayAttribute");
        if (inlineArrayAttr != null)
        {
            WriteInlineArrayDefinition(structSym, inlineArrayAttr);
        }
        else
        {
            WriteStandardStructDefinition(structSym);
        }
    }

    private void WriteInlineArrayDefinition(INamedTypeSymbol structSym, AttributeData inlineArrayAttr)
    {
        var structName = CudaTypeMapper.GetFullyQualifiedCppName(structSym);
        var length = (int)inlineArrayAttr.ConstructorArguments[0].Value!;
        var firstField = structSym.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => !f.IsStatic);
        if (firstField == null)
        {
            return;
        }

        var cppElementType = CudaTypeMapper.MapType(firstField.Type);
        _context.Writer.AppendLine($"#ifndef __GSHARP_STRUCT_{structName}__");
        _context.Writer.AppendLine($"#define __GSHARP_STRUCT_{structName}__");
        _context.Writer.AppendLine($"struct {structName}");
        using (_context.Writer.Scope())
        {
            _context.Writer.Append($"{cppElementType} {firstField.Name}");
            using (_context.Writer.Brackets())
            {
                _context.Writer.Append($"{length}");
            }

            _context.Writer.AppendLine(";");

            _context.Writer.Append($"__device__ inline {cppElementType}& operator");
            _context.Writer.AppendBrackets();

            using (_context.Writer.Parentheses())
            {
                _context.Writer.Append("int index");
            }

            using (_context.Writer.Scope())
            {
                _context.Writer.Append($"return {firstField.Name}");
                using (_context.Writer.Brackets())
                {
                    _context.Writer.Append("index");
                }

                _context.Writer.AppendLine(";");
            }

            _context.Writer.Append($"__device__ inline const {cppElementType}& operator");
            _context.Writer.AppendBrackets();

            using (_context.Writer.Parentheses())
            {
                _context.Writer.Append("int index");
            }

            _context.Writer.AppendLine(" const");
            using (_context.Writer.Scope())
            {
                _context.Writer.Append($"return {firstField.Name}");
                using (_context.Writer.Brackets())
                {
                    _context.Writer.Append("index");
                }

                _context.Writer.AppendLine(";");
            }
        }

        _context.Writer.AppendSemiColon();
        _context.Writer.AppendLine();
        _context.Writer.AppendLine("#endif");
        _context.Writer.AppendLine();
    }

    private void WriteStandardStructDefinition(INamedTypeSymbol structSym)
    {
        var structName = CudaTypeMapper.GetFullyQualifiedCppName(structSym);
        _context.Writer.AppendLine($"#ifndef __GSHARP_STRUCT_{structName}__");
        _context.Writer.AppendLine($"#define __GSHARP_STRUCT_{structName}__");
        _context.Writer.AppendLine($"struct {structName}");
        using (_context.Writer.Scope())
        {
            var members = structSym.GetMembers().Where(m => !m.IsStatic && !m.IsImplicitlyDeclared);
            foreach (var member in members)
            {
                switch (member)
                {
                    case IFieldSymbol { IsFixedSizeBuffer: true, Type: IPointerTypeSymbol ptrType } field:
                    {
                        var syntax =
                            field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as VariableDeclaratorSyntax;
                        var sizeExp = syntax?.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
                        if (sizeExp != null)
                        {
                            _context.Writer.Append($"{CudaTypeMapper.MapType(ptrType.PointedAtType)} {field.Name}");
                            using (_context.Writer.Brackets())
                            {
                                Visit(sizeExp);
                            }

                            _context.Writer.AppendLine(";");
                        }
                        else
                        {
                            _context.Writer.AppendLine(
                                $"{CudaTypeMapper.MapType(ptrType.PointedAtType)} {field.Name}; // ERROR: fixed size not found");
                        }

                        break;
                    }
                    case IFieldSymbol field:
                    {
                        var fieldConst =
                            field.GetAttributes().Any(a => KnownAttributes.IsGpuConst(a.AttributeClass?.Name))
                                ? "const "
                                : "";
                        _context.Writer.AppendLine($"{fieldConst}{CudaTypeMapper.MapType(field.Type)} {field.Name};");
                        break;
                    }
                    case IPropertySymbol prop:
                    {
                        var propConst =
                            prop.GetAttributes().Any(a => KnownAttributes.IsGpuConst(a.AttributeClass?.Name))
                                ? "const "
                                : "";
                        _context.Writer.AppendLine($"{propConst}{CudaTypeMapper.MapType(prop.Type)} {prop.Name};");
                        break;
                    }
                }
            }

            var conversions = structSym.GetMembers().OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Conversion && m.Name == "op_Implicit");
            foreach (var conv in conversions)
            {
                if (SymbolEqualityComparer.Default.Equals(conv.ReturnType, structSym))
                {
                    // Implicit constructor from param type
                    var paramType = CudaTypeMapper.MapType(conv.Parameters[0].Type);
                    _context.Writer.Append($"__device__ inline {structName}() ");
                    using (_context.Writer.Scope())
                    {
                    }

                    _context.Writer.Append($"__device__ inline {structName}({paramType} v) ");
                    using (_context.Writer.Scope())
                    {
                        var firstField = structSym.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => !f.IsStatic);
                        if (firstField != null)
                        {
                            _context.Writer.AppendLine($"this->{firstField.Name} = v;");
                        }
                    }
                }
                else
                {
                    // Implicit conversion operator to return type
                    var returnType = CudaTypeMapper.MapType(conv.ReturnType);
                    _context.Writer.Append($"__device__ inline operator {returnType}() const ");
                    using (_context.Writer.Scope())
                    {
                        var firstField = structSym.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => !f.IsStatic);
                        if (firstField != null)
                        {
                            _context.Writer.AppendLine($"return this->{firstField.Name};");
                        }
                    }
                }
            }
        }

        _context.Writer.AppendSemiColon();
        _context.Writer.AppendLine();
        _context.Writer.AppendLine("#endif");
        _context.Writer.AppendLine();
    }

    public void VisitStatementOrBlock(StatementSyntax statement)
    {
        if (statement is BlockSyntax block)
        {
            using (_context.Writer.Scope())
            {
                foreach (var stmt in block.Statements)
                {
                    Visit(stmt);
                    _context.Writer.AppendLine();
                }
            }
        }
        else
        {
            // Auto-wrap single statements in braces to support C# bracket omission safely in C++
            using (_context.Writer.Scope())
            {
                Visit(statement);
                if (!statement.IsKind(SyntaxKind.Block))
                {
                    _context.Writer.AppendLine();
                }
            }
        }
    }

    public override void VisitPredefinedType(PredefinedTypeSyntax node)
    {
        if (node.Keyword.IsKind(SyntaxKind.StringKeyword))
        {
            _context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.StringNotSupported,
                node.GetLocation()));
        }

        base.VisitPredefinedType(node);
    }

    public override void DefaultVisit(SyntaxNode node)
    {
        _context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.UnsupportedSyntax, node.GetLocation(),
            node.Kind().ToString()));
        base.DefaultVisit(node);
    }
}


