namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Emits CUDA C++ struct definitions, operator overloads, and handles InlineArray structures.</summary>
internal static class StructTranspiler
{
    public static void RegisterUsedStructIfApplicable(ITypeSymbol? typeSymbol, ref TranspileContext context)
    {
        var structType = typeSymbol is IArrayTypeSymbol arrayType ? arrayType.ElementType : typeSymbol;
        if (structType is not INamedTypeSymbol { TypeKind: TypeKind.Struct } namedStruct ||
            namedStruct.IsGpuBigIntRef() ||
            namedStruct.ContainingNamespace.IsSystemOrNumerics())
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

    /// <summary>Topologically sorts referenced structs so that dependent nested structs are declared first in C++.</summary>
    public static IEnumerable<INamedTypeSymbol> GetTopologicallySortedStructs(ref TranspileContext context)
    {
        // [WHY] CUDA C++ requires full struct definitions in dependency order before value-type usage as fields.
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
                    namedStruct.ContainingNamespace.IsSystemOrNumerics())
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
            typeSymbol.ContainingNamespace.IsSystemOrNumerics())
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
        var length = (int)inlineArrayAttr.ConstructorArguments[0].Value!;
        var firstField = structSym.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => !f.IsStatic);
        if (firstField == null)
        {
            return;
        }

        context.Writer.Append("#ifndef __GSHARP_STRUCT_");
        context.Writer.AppendFullyQualifiedCppName(structSym);
        context.Writer.AppendLine("__");

        context.Writer.Append("#define __GSHARP_STRUCT_");
        context.Writer.AppendFullyQualifiedCppName(structSym);
        context.Writer.AppendLine("__");

        context.Writer.Append("struct ");
        context.Writer.AppendFullyQualifiedCppName(structSym);
        context.Writer.AppendLine();
        
        using (context.Writer.Scope())
        {
            context.Writer.AppendType(firstField.Type);
            context.Writer.Append(" ");
            context.Writer.Append(firstField.Name);
            using (context.Writer.Brackets())
            {
                context.Writer.Append($"{length}");
            }

            context.Writer.AppendLine(";");

            context.Writer.Append("__device__ inline ");
            context.Writer.AppendType(firstField.Type);
            context.Writer.Append("& operator");
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

            context.Writer.Append("__device__ inline const ");
            context.Writer.AppendType(firstField.Type);
            context.Writer.Append("& operator");
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
        context.Writer.Append("#ifndef __GSHARP_STRUCT_");
        context.Writer.AppendFullyQualifiedCppName(structSym);
        context.Writer.AppendLine("__");

        context.Writer.Append("#define __GSHARP_STRUCT_");
        context.Writer.AppendFullyQualifiedCppName(structSym);
        context.Writer.AppendLine("__");

        context.Writer.Append("struct ");
        context.Writer.AppendFullyQualifiedCppName(structSym);
        context.Writer.AppendLine();

        using (context.Writer.Scope())
        {
            var members = structSym.GetMembers().Where(m => !m.IsStatic && !m.IsImplicitlyDeclared);
            foreach (var member in members)
            {
                WriteStructMember(ref context, member);
            }

            var conversions = structSym.GetMembers().OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Conversion && m.Name == "op_Implicit");
            foreach (var conv in conversions)
            {
                WriteImplicitConversionOperator(structSym, ref context, conv);
            }
        }

        context.Writer.AppendSemiColon();
        context.Writer.AppendLine();
        context.Writer.AppendLine("#endif");
        context.Writer.AppendLine();
    }

    private static void WriteImplicitConversionOperator(INamedTypeSymbol structSym, ref TranspileContext context, IMethodSymbol conv)
    {
        var firstField = structSym.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => !f.IsStatic);

        if (SymbolEqualityComparer.Default.Equals(conv.ReturnType, structSym))
        {
            // Implicit constructor from param type
            context.Writer.Append("__device__ inline ");
            context.Writer.AppendFullyQualifiedCppName(structSym);
            using (context.Writer.Parentheses()) { }
            context.Writer.AppendSpace();
            using (context.Writer.Scope())
            {
            }

            context.Writer.Append("__device__ inline ");
            context.Writer.AppendFullyQualifiedCppName(structSym);
            using (context.Writer.Parentheses())
            {
                context.Writer.AppendType(conv.Parameters[0].Type);
                context.Writer.AppendSpace();
                context.Writer.Append("v");
            }
            context.Writer.AppendSpace();
            using (context.Writer.Scope())
            {
                if (firstField != null)
                {
                    context.Writer.Append("this->");
                    context.Writer.Append(firstField.Name);
                    context.Writer.AppendLine(" = v;");
                }
            }
        }
        else
        {
            // Implicit conversion operator to return type
            context.Writer.Append("__device__ inline operator ");
            context.Writer.AppendType(conv.ReturnType);
            context.Writer.Append("() const ");
            using (context.Writer.Scope())
            {
                if (firstField != null)
                {
                    context.Writer.Append("return this->");
                    context.Writer.Append(firstField.Name);
                    context.Writer.AppendLine(";");
                }
            }
        }
    }

    private static void WriteStructMember(ref TranspileContext context, ISymbol member)
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
                    context.Writer.AppendType(ptrType.PointedAtType);
                    context.Writer.AppendSpace();
                    context.Writer.Append(field.Name);
                    using (context.Writer.Brackets())
                    {
                        context.Visitor.Visit(sizeExp);
                    }

                    context.Writer.AppendSemiColon();
                    context.Writer.AppendLine();
                }
                else
                {
                    context.Writer.AppendType(ptrType.PointedAtType);
                    context.Writer.AppendSpace();
                    context.Writer.Append(field.Name);
                    context.Writer.AppendLine("; // ERROR: fixed size not found");
                }

                break;
            }
            case IFieldSymbol field:
                WriteMemberDeclaration(field.Type, field.Name, field, ref context);
                break;
            case IPropertySymbol prop:
                WriteMemberDeclaration(prop.Type, prop.Name, prop, ref context);
                break;
        }
    }

    private static void WriteMemberDeclaration(ITypeSymbol type, string name, ISymbol memberSymbol,
        ref TranspileContext context)
    {
        if (memberSymbol.GetAttributes().Any(a => KnownAttributes.IsGpuConst(a.AttributeClass?.Name)))
        {
            context.Writer.AppendKeyword("const");
            context.Writer.AppendSpace();
        }

        context.Writer.AppendType(type);
        context.Writer.AppendSpace();
        context.Writer.Append(name);
        context.Writer.AppendSemiColon();
        context.Writer.AppendLine();
    }
}
