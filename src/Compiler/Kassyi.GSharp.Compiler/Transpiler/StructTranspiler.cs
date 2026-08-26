namespace Kassyi.GSharp.Compiler.Transpiler;

internal static class StructTranspiler
{
    public static void RegisterUsedStructIfApplicable(ITypeSymbol? typeSymbol, ref TranspileContext context)
    {
        var structType = typeSymbol is IArrayTypeSymbol arrayType ? arrayType.ElementType : typeSymbol;
        if (structType is not INamedTypeSymbol { TypeKind: TypeKind.Struct } namedStruct ||
            namedStruct.ContainingNamespace.ToDisplayString() == "System")
        {
            return;
        }

        if (!context.UsedStructs.Add(namedStruct))
        {
            return;
        }

        foreach (var member in namedStruct.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
        {
            RegisterUsedStructIfApplicable(member.Type, ref context);
        }
    }

    public static IEnumerable<INamedTypeSymbol> GetTopologicallySortedStructs(ref TranspileContext context)
    {
        var result = new List<INamedTypeSymbol>();
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var visiting = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var usedStructs = context.UsedStructs;

        foreach (var s in usedStructs)
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

                if (usedStructs.Contains(namedStruct))
                {
                    visit(namedStruct);
                }
            }

            visiting.Remove(s);
            visited.Add(s);
            result.Add(s);
        }
    }

    public static void CheckLargeStructByValue(ParameterSyntax p, ITypeSymbol? typeSymbol, bool isPointer,
        ref TranspileContext context)
    {
        if (isPointer || typeSymbol is not { TypeKind: TypeKind.Struct } ||
            typeSymbol.ContainingNamespace.ToDisplayString() == TypeConstants.SystemNamespace)
        {
            return;
        }

        var size = CudaTypeMapper.GetApproximateSize(typeSymbol);
        if (size >= 32)
        {
            context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.LargeStructByValueWarning,
                p.GetLocation(), p.Identifier.Text, size));
        }
    }

    public static void WriteStructDefinition(INamedTypeSymbol structSym, ref TranspileContext context)
    {
        var inlineArrayAttr = structSym.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "InlineArrayAttribute");
        if (inlineArrayAttr != null)
        {
            WriteInlineArrayDefinition(structSym, inlineArrayAttr, ref context);
        }
        else
        {
            WriteStandardStructDefinition(structSym, ref context);
        }
    }

    private static void WriteInlineArrayDefinition(INamedTypeSymbol structSym, AttributeData inlineArrayAttr,
        ref TranspileContext context)
    {
        var structName = CudaTypeMapper.GetFullyQualifiedCppName(structSym);
        var length = (int)inlineArrayAttr.ConstructorArguments[0].Value!;
        var firstField = structSym.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => !f.IsStatic);
        if (firstField == null)
        {
            return;
        }

        var cppElementType = CudaTypeMapper.MapType(firstField.Type);
        context.Writer.AppendLine($"#ifndef __GSHARP_STRUCT_{structName}__");
        context.Writer.AppendLine($"#define __GSHARP_STRUCT_{structName}__");
        context.Writer.AppendLine($"struct {structName}");
        using (context.Writer.Scope())
        {
            context.Writer.Append($"{cppElementType} {firstField.Name}");
            using (context.Writer.Brackets())
            {
                context.Writer.Append($"{length}");
            }

            context.Writer.AppendLine(";");

            context.Writer.Append($"__device__ inline {cppElementType}& operator");
            context.Writer.AppendBrackets();

            using (context.Writer.Parentheses())
            {
                context.Writer.Append("int index");
            }

            using (context.Writer.Scope())
            {
                context.Writer.Append($"return {firstField.Name}");
                using (context.Writer.Brackets())
                {
                    context.Writer.Append("index");
                }

                context.Writer.AppendLine(";");
            }

            context.Writer.Append($"__device__ inline const {cppElementType}& operator");
            context.Writer.AppendBrackets();

            using (context.Writer.Parentheses())
            {
                context.Writer.Append("int index");
            }

            context.Writer.AppendLine(" const");
            using (context.Writer.Scope())
            {
                context.Writer.Append($"return {firstField.Name}");
                using (context.Writer.Brackets())
                {
                    context.Writer.Append("index");
                }

                context.Writer.AppendLine(";");
            }
        }

        context.Writer.AppendSemiColon();
        context.Writer.AppendLine();
        context.Writer.AppendLine("#endif");
        context.Writer.AppendLine();
    }

    private static void WriteStandardStructDefinition(INamedTypeSymbol structSym, ref TranspileContext context)
    {
        var structName = CudaTypeMapper.GetFullyQualifiedCppName(structSym);
        context.Writer.AppendLine($"#ifndef __GSHARP_STRUCT_{structName}__");
        context.Writer.AppendLine($"#define __GSHARP_STRUCT_{structName}__");
        context.Writer.AppendLine($"struct {structName}");
        using (context.Writer.Scope())
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
                            context.Writer.Append($"{CudaTypeMapper.MapType(ptrType.PointedAtType)} {field.Name}");
                            using (context.Writer.Brackets())
                            {
                                context.Visitor.Visit(sizeExp);
                            }

                            context.Writer.AppendLine(";");
                        }
                        else
                        {
                            context.Writer.AppendLine(
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
                        context.Writer.AppendLine($"{fieldConst}{CudaTypeMapper.MapType(field.Type)} {field.Name};");
                        break;
                    }
                    case IPropertySymbol prop:
                    {
                        var propConst =
                            prop.GetAttributes().Any(a => KnownAttributes.IsGpuConst(a.AttributeClass?.Name))
                                ? "const "
                                : "";
                        context.Writer.AppendLine($"{propConst}{CudaTypeMapper.MapType(prop.Type)} {prop.Name};");
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
                    context.Writer.Append($"__device__ inline {structName}() ");
                    using (context.Writer.Scope())
                    {
                    }

                    context.Writer.Append($"__device__ inline {structName}({paramType} v) ");
                    using (context.Writer.Scope())
                    {
                        var firstField = structSym.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => !f.IsStatic);
                        if (firstField != null)
                        {
                            context.Writer.AppendLine($"this->{firstField.Name} = v;");
                        }
                    }
                }
                else
                {
                    // Implicit conversion operator to return type
                    var returnType = CudaTypeMapper.MapType(conv.ReturnType);
                    context.Writer.Append($"__device__ inline operator {returnType}() const ");
                    using (context.Writer.Scope())
                    {
                        var firstField = structSym.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => !f.IsStatic);
                        if (firstField != null)
                        {
                            context.Writer.AppendLine($"return this->{firstField.Name};");
                        }
                    }
                }
            }
        }

        context.Writer.AppendSemiColon();
        context.Writer.AppendLine();
        context.Writer.AppendLine("#endif");
        context.Writer.AppendLine();
    }
}
