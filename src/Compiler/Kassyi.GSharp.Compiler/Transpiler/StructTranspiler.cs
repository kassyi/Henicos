namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Emits CUDA C++ struct definitions, operator overloads, and handles InlineArray structures.</summary>
internal static class StructTranspiler
{
    /// <summary>
    /// Recursively registers referenced struct symbols and their field types to ensure necessary forward definitions are emitted.
    /// </summary>
    public static void RegisterUsedStructIfApplicable(ITypeSymbol? typeSymbol, ref TranspileContext context)
    {
        var structType = typeSymbol is IArrayTypeSymbol arrayType ? arrayType.ElementType : typeSymbol;
        if (structType is not INamedTypeSymbol { TypeKind: TypeKind.Struct } namedStruct ||
            namedStruct.ContainingNamespace.IsSystemOrNumerics())
        {
            return;
        }

        if (namedStruct.Name == TypeConstants.SharedMemoryType && namedStruct.ContainingNamespace.IsCoreNamespace())
        {
            return;
        }

        if (!context.Query.UsedStructs.Add(namedStruct))
        {
            return;
        }

        foreach (var dep in GetStructDependencies(namedStruct))
        {
            RegisterUsedStructIfApplicable(dep, ref context);
        }
    }

    /// <summary>
    /// Topologically sorts referenced structs so that dependent nested structs are declared first in C++.
    /// </summary>
    public static ImmutableArray<INamedTypeSymbol> GetTopologicallySortedStructs(ref TranspileContext context)
    {
        // [WHY] CUDA C++ requires full struct definitions in dependency order before value-type usage as fields.
        var result = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var visiting = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var usedStructs = context.Query.UsedStructs;

        foreach (var s in usedStructs)
        {
            visit(s);
        }

        return result.ToImmutable();

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

            foreach (var dep in GetStructDependencies(s))
            {
                if (usedStructs.Contains(dep))
                {
                    visit(dep);
                }
            }

            visiting.Remove(s);
            visited.Add(s);
            result.Add(s);
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetStructDependencies(INamedTypeSymbol structSymbol)
    {
        foreach (var member in structSymbol.GetMembers())
        {
            if (member is not IFieldSymbol { IsStatic: false } field)
            {
                continue;
            }

            var fieldType = field.Type is IArrayTypeSymbol arrayType ? arrayType.ElementType : field.Type;
            if (fieldType is INamedTypeSymbol { TypeKind: TypeKind.Struct } namedStruct &&
                !namedStruct.ContainingNamespace.IsSystemOrNumerics())
            {
                yield return namedStruct;
            }
        }
    }

    /// <summary>
    /// Checks whether a struct passed by value exceeds the 32-byte threshold and reports a performance warning diagnostic if applicable.
    /// </summary>
    public static void CheckLargeStructByValue(ParameterSyntax p, ITypeSymbol? typeSymbol, bool isPointer,
        ref TranspileContext context)
    {
        if (isPointer || typeSymbol is not { TypeKind: TypeKind.Struct } ||
            typeSymbol.ContainingNamespace.IsSystemOrNumerics())
        {
            return;
        }

        var size = context.Emitter.Dialect.GetApproximateSize(typeSymbol);
        if (size >= 32)
        {
            context.Reporter.Report(GSharpDiagnostics.LargeStructByValueWarning, p, p.Identifier.Text, size);
        }
    }

    /// <summary>
    /// Emits the C++ struct definition for the specified named struct symbol, handling InlineArray structures and operator overloads.
    /// </summary>
    public static void WriteStructDefinition(INamedTypeSymbol structSym, ref TranspileContext context)
    {
        int? packValue = null;
        AttributeData? structLayoutAttr = null;
        foreach (var a in structSym.GetAttributes())
        {
            if (!a.IsStructLayoutAttribute())
            {
                continue;
            }

            structLayoutAttr = a;
            break;
        }

        if (structLayoutAttr != null)
        {
            TypedConstant? packArgValue = null;
            foreach (var kvp in structLayoutAttr.NamedArguments)
            {
                if (kvp.Key != "Pack")
                {
                    continue;
                }

                packArgValue = kvp.Value;
                break;
            }

            if (packArgValue is { IsNull: false, Value: int p and > 0 })
            {
                packValue = p;
            }
        }

        if (packValue.HasValue)
        {
            context.Emitter.Writer.AppendLine($"#pragma pack(push, {packValue.Value})");
        }

        var inlineArrayAttr = structSym.GetAttribute(KnownAttributes.InlineArray);
        if (inlineArrayAttr != null)
        {
            WriteInlineArrayDefinition(structSym, inlineArrayAttr, ref context);
        }
        else
        {
            WriteStandardStructDefinition(structSym, ref context);
        }

        if (packValue.HasValue)
        {
            context.Emitter.Writer.AppendLine("#pragma pack(pop)");
        }
    }

    private static void WriteInlineArrayDefinition(INamedTypeSymbol structSym, AttributeData inlineArrayAttr,
        ref TranspileContext context)
    {
        var length = (int)(inlineArrayAttr.ConstructorArguments[0].Value ?? 0);
        var firstField = GetFirstInstanceField(structSym);
        if (firstField == null)
        {
            return;
        }

        WriteStructHeader(ref context, structSym);
        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Dialect.AppendType(ref context, firstField.Type);
            context.Emitter.Writer.Append($" {firstField.Name}");
            using (context.Emitter.Writer.Brackets())
            {
                context.Emitter.Writer.Append(length);
            }

            context.Emitter.Writer.AppendSemiColonLine();

            WriteInlineArrayIndexer(ref context, firstField, isConst: false);
            WriteInlineArrayIndexer(ref context, firstField, isConst: true);
            WriteInlineArrayIterators(ref context, firstField, length);
        }

        WriteStructFooter(ref context);
    }

    private static void WriteInlineArrayIterators(ref TranspileContext context, IFieldSymbol firstField, int length)
    {
        context.Emitter.Writer.Append("__device__ inline ");
        context.Emitter.Dialect.AppendType(ref context, firstField.Type);
        context.Emitter.Writer.AppendLine($"* begin() {{ return {firstField.Name}; }}");

        context.Emitter.Writer.Append("__device__ inline ");
        context.Emitter.Dialect.AppendType(ref context, firstField.Type);
        context.Emitter.Writer.AppendLine($"* end() {{ return {firstField.Name} + {length}; }}");

        context.Emitter.Writer.Append("__device__ inline const ");
        context.Emitter.Dialect.AppendType(ref context, firstField.Type);
        context.Emitter.Writer.AppendLine($"* begin() const {{ return {firstField.Name}; }}");

        context.Emitter.Writer.Append("__device__ inline const ");
        context.Emitter.Dialect.AppendType(ref context, firstField.Type);
        context.Emitter.Writer.AppendLine($"* end() const {{ return {firstField.Name} + {length}; }}");

        context.Emitter.Writer.AppendLine($"__device__ inline int size() const {{ return {length}; }}");
    }

    private static void WriteInlineArrayIndexer(ref TranspileContext context, IFieldSymbol firstField, bool isConst)
    {
        context.Emitter.Writer.Append(isConst ? "__device__ inline const " : "__device__ inline ");
        context.Emitter.Dialect.AppendType(ref context, firstField.Type);
        context.Emitter.Writer.Append("& operator[]");

        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Writer.Append("int index");
        }

        if (isConst)
        {
            context.Emitter.Writer.AppendLine(" const");
        }

        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Writer.Append($"return {firstField.Name}");
            using (context.Emitter.Writer.Brackets())
            {
                context.Emitter.Writer.Append("index");
            }

            context.Emitter.Writer.AppendSemiColonLine();
        }
    }

    private static void WriteStandardStructDefinition(INamedTypeSymbol structSym, ref TranspileContext context)
    {
        WriteStructHeader(ref context, structSym);

        using (context.Emitter.Writer.Scope())
        {
            foreach (var member in structSym.GetMembers())
            {
                if (member is { IsStatic: false, IsImplicitlyDeclared: false })
                {
                    WriteStructMember(ref context, member);
                }
            }

            foreach (var member in structSym.GetMembers())
            {
                if (member is IMethodSymbol { MethodKind: MethodKind.Conversion, Name: "op_Implicit" } conv)
                {
                    WriteImplicitConversionOperator(structSym, ref context, conv);
                }
            }
        }

        WriteStructFooter(ref context);
    }

    public static void WriteTemplatePrefix(ref TranspileContext context, INamedTypeSymbol structSym)
    {
        if (structSym.TypeParameters.Length > 0)
        {
            context.Emitter.Writer.Append("template <");
            for (var i = 0; i < structSym.TypeParameters.Length; i++)
            {
                if (i > 0)
                {
                    context.Emitter.Writer.Append(", ");
                }

                context.Emitter.Writer.Append("typename ").Append(structSym.TypeParameters[i].Name);
            }

            context.Emitter.Writer.AppendLine(">");
        }
    }

    private static void WriteStructHeader(ref TranspileContext context, INamedTypeSymbol structSym)
    {
        var guardName = $"__GSHARP_STRUCT_{structSym.ToSafeCppIdentifier()}__";

        context.Emitter.Writer.Append("#ifndef ").AppendLine(guardName)
            .Append("#define ").AppendLine(guardName);

        WriteTemplatePrefix(ref context, structSym);

        context.Emitter.Writer.Append("struct ").AppendFullyQualifiedCppName(structSym).AppendLine();
    }

    private static void WriteStructFooter(ref TranspileContext context)
    {
        context.Emitter.Writer
            .AppendSemiColonLine()
            .AppendLine("#endif")
            .AppendLine();
    }

    private static void WriteImplicitConversionOperator(INamedTypeSymbol structSym, ref TranspileContext context,
        IMethodSymbol conv)
    {
        var firstField = GetFirstInstanceField(structSym);

        if (conv.ReturnType.IsEqualTo(structSym))
        {
            // Implicit constructor from param type
            context.Emitter.Writer.Append("__device__ inline ").AppendFullyQualifiedCppName(structSym)
                .AppendLine("() {}");

            context.Emitter.Writer.Append("__device__ inline ").AppendFullyQualifiedCppName(structSym);
            using (context.Emitter.Writer.Parentheses())
            {
                context.Emitter.Dialect.AppendType(ref context, conv.Parameters[0].Type);
                context.Emitter.Writer.Append(" v");
            }

            context.Emitter.Writer.AppendSpace();
            using (context.Emitter.Writer.Scope())
            {
                if (firstField != null)
                {
                    context.Emitter.Writer.AppendLine($"this->{firstField.Name} = v;");
                }
            }
        }
        else
        {
            // Implicit conversion operator to return type
            context.Emitter.Writer.Append("__device__ inline operator ");
            context.Emitter.Dialect.AppendType(ref context, conv.ReturnType);
            context.Emitter.Writer.Append("() const ");
            using (context.Emitter.Writer.Scope())
            {
                if (firstField == null)
                {
                    return;
                }

                context.Emitter.Writer.Append($"return this->{firstField.Name}");
                context.Emitter.Writer.AppendSemiColonLine();
            }
        }
    }

    private static IFieldSymbol? GetFirstInstanceField(INamedTypeSymbol structSym)
    {
        foreach (var member in structSym.GetMembers())
        {
            if (member is IFieldSymbol { IsStatic: false } field)
            {
                return field;
            }
        }

        return null;
    }

    private static void WriteStructMember(ref TranspileContext context, ISymbol member)
    {
        switch (member)
        {
            case IFieldSymbol { IsFixedSizeBuffer: true, Type: IPointerTypeSymbol ptrType } field:
            {
                VariableDeclaratorSyntax? syntax = null;
                var syntaxRefs = field.DeclaringSyntaxReferences;
                if (!syntaxRefs.IsEmpty)
                {
                    syntax = syntaxRefs[0].GetSyntax() as VariableDeclaratorSyntax;
                }

                ExpressionSyntax? sizeExp = null;
                if (syntax?.ArgumentList is { Arguments.Count: > 0 } argList)
                {
                    sizeExp = argList.Arguments[0].Expression;
                }

                if (sizeExp != null)
                {
                    context.Emitter.Dialect.AppendType(ref context, ptrType.PointedAtType);
                    context.Emitter.Writer.Append($" {field.Name}");
                    using (context.Emitter.Writer.Brackets())
                    {
                        context.Visitor.Visit(sizeExp);
                    }

                    context.Emitter.Writer.AppendSemiColonLine();
                }
                else
                {
                    context.Emitter.Dialect.AppendType(ref context, ptrType.PointedAtType);
                    context.Emitter.Writer.AppendLine($"* {field.Name}; // ERROR: fixed size not found");
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
        if (memberSymbol.HasAttribute(KnownAttributes.GpuReadOnly))
        {
            context.Emitter.Writer.Append("const ");
        }

        context.Emitter.Dialect.AppendType(ref context, type);
        context.Emitter.Writer.AppendLine($" {name};");
    }
}
