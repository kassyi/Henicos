namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Emits CUDA C++ struct definitions, operator overloads, and handles InlineArray structures.</summary>
/// <remarks><para>Implements: <b>GEN-33</b></para></remarks>
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

        if (!context.UsedStructs.Add(namedStruct))
        {
            return;
        }

        foreach (var member in namedStruct.GetMembers())
        {
            if (member is IFieldSymbol { IsStatic: false } field)
            {
                RegisterUsedStructIfApplicable(field.Type, ref context);
            }
        }
    }

    /// <summary>
    /// Topologically sorts referenced structs so that dependent nested structs are declared first in C++.
    /// </summary>
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

            foreach (var member in s.GetMembers())
            {
                if (member is not IFieldSymbol { IsStatic: false } field)
                {
                    continue;
                }

                var fieldType = field.Type is IArrayTypeSymbol arrayType ? arrayType.ElementType : field.Type;
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

        var size = context.Dialect.GetApproximateSize(typeSymbol);
        if (size >= 32)
        {
            context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.LargeStructByValueWarning,
                p.GetLocation(), p.Identifier.Text, size));
        }
    }

    /// <summary>
    /// Emits the C++ struct definition for the specified named struct symbol, handling InlineArray structures and operator overloads.
    /// </summary>
    public static void WriteStructDefinition(INamedTypeSymbol structSym, ref TranspileContext context)
    {
        int? packValue = null;
        var structLayoutAttr = structSym.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute");
        if (structLayoutAttr != null)
        {
            var packArg = structLayoutAttr.NamedArguments.FirstOrDefault(kvp => kvp.Key == "Pack").Value;
            if (packArg is { IsNull: false, Value: int p and > 0 })
            {
                packValue = p;
            }
        }

        if (packValue.HasValue)
        {
            context.Writer.AppendLine($"#pragma pack(push, {packValue.Value})");
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
            context.Writer.AppendLine("#pragma pack(pop)");
        }
    }

    private static void WriteInlineArrayDefinition(INamedTypeSymbol structSym, AttributeData inlineArrayAttr,
        ref TranspileContext context)
    {
        var length = (int)inlineArrayAttr.ConstructorArguments[0].Value!;
        var firstField = GetFirstInstanceField(structSym);
        if (firstField == null)
        {
            return;
        }

        WriteStructHeader(ref context, structSym);
        using (context.Writer.Scope())
        {
            context.AppendType(firstField.Type);
            context.Writer.Append($" {firstField.Name}");
            using (context.Writer.Brackets())
            {
                context.Writer.Append(length);
            }

            context.Writer.AppendSemiColonLine();

            WriteInlineArrayIndexer(ref context, firstField, isConst: false);
            WriteInlineArrayIndexer(ref context, firstField, isConst: true);
            WriteInlineArrayIterators(ref context, firstField, length);
        }

        WriteStructFooter(ref context);
    }

    private static void WriteInlineArrayIterators(ref TranspileContext context, IFieldSymbol firstField, int length)
    {
        context.Writer.Append("__device__ inline ");
        context.AppendType(firstField.Type);
        context.Writer.AppendLine($"* begin() {{ return {firstField.Name}; }}");

        context.Writer.Append("__device__ inline ");
        context.AppendType(firstField.Type);
        context.Writer.AppendLine($"* end() {{ return {firstField.Name} + {length}; }}");

        context.Writer.Append("__device__ inline const ");
        context.AppendType(firstField.Type);
        context.Writer.AppendLine($"* begin() const {{ return {firstField.Name}; }}");

        context.Writer.Append("__device__ inline const ");
        context.AppendType(firstField.Type);
        context.Writer.AppendLine($"* end() const {{ return {firstField.Name} + {length}; }}");

        context.Writer.AppendLine($"__device__ inline int size() const {{ return {length}; }}");
    }

    private static void WriteInlineArrayIndexer(ref TranspileContext context, IFieldSymbol firstField, bool isConst)
    {
        context.Writer.Append(isConst ? "__device__ inline const " : "__device__ inline ");
        context.AppendType(firstField.Type);
        context.Writer.Append("& operator[]");

        using (context.Writer.Parentheses())
        {
            context.Writer.Append("int index");
        }

        if (isConst)
        {
            context.Writer.AppendLine(" const");
        }

        using (context.Writer.Scope())
        {
            context.Writer.Append($"return {firstField.Name}");
            using (context.Writer.Brackets())
            {
                context.Writer.Append("index");
            }

            context.Writer.AppendSemiColonLine();
        }
    }

    private static void WriteStandardStructDefinition(INamedTypeSymbol structSym, ref TranspileContext context)
    {
        WriteStructHeader(ref context, structSym);

        using (context.Writer.Scope())
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
            context.Writer.Append("template <");
            for (var i = 0; i < structSym.TypeParameters.Length; i++)
            {
                if (i > 0)
                {
                    context.Writer.Append(", ");
                }

                context.Writer.Append("typename ").Append(structSym.TypeParameters[i].Name);
            }

            context.Writer.AppendLine(">");
        }
    }

    private static void WriteStructHeader(ref TranspileContext context, INamedTypeSymbol structSym)
    {
        var guardName = $"__GSHARP_STRUCT_{structSym.ToSafeCppIdentifier()}__";

        context.Writer.Append("#ifndef ").AppendLine(guardName)
            .Append("#define ").AppendLine(guardName);

        WriteTemplatePrefix(ref context, structSym);

        context.Writer.Append("struct ").AppendFullyQualifiedCppName(structSym).AppendLine();
    }

    private static void WriteStructFooter(ref TranspileContext context)
    {
        context.Writer
            .AppendSemiColonLine()
            .AppendLine("#endif")
            .AppendLine();
    }

    private static void WriteImplicitConversionOperator(INamedTypeSymbol structSym, ref TranspileContext context,
        IMethodSymbol conv)
    {
        var firstField = GetFirstInstanceField(structSym);

        if (SymbolEqualityComparer.Default.Equals(conv.ReturnType, structSym))
        {
            // Implicit constructor from param type
            context.Writer.Append("__device__ inline ").AppendFullyQualifiedCppName(structSym).AppendLine("() {}");

            context.Writer.Append("__device__ inline ").AppendFullyQualifiedCppName(structSym);
            using (context.Writer.Parentheses())
            {
                context.AppendType(conv.Parameters[0].Type);
                context.Writer.Append(" v");
            }

            context.Writer.AppendSpace();
            using (context.Writer.Scope())
            {
                if (firstField != null)
                {
                    context.Writer.AppendLine($"this->{firstField.Name} = v;");
                }
            }
        }
        else
        {
            // Implicit conversion operator to return type
            context.Writer.Append("__device__ inline operator ");
            context.AppendType(conv.ReturnType);
            context.Writer.Append("() const ");
            using (context.Writer.Scope())
            {
                if (firstField == null)
                {
                    return;
                }

                context.Writer.Append($"return this->{firstField.Name}");
                context.Writer.AppendSemiColonLine();
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
                    context.AppendType(ptrType.PointedAtType);
                    context.Writer.Append($" {field.Name}");
                    using (context.Writer.Brackets())
                    {
                        context.Visitor.Visit(sizeExp);
                    }

                    context.Writer.AppendSemiColonLine();
                }
                else
                {
                    context.AppendType(ptrType.PointedAtType);
                    context.Writer.AppendLine($"* {field.Name}; // ERROR: fixed size not found");
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
        if (memberSymbol.HasAttribute(KnownAttributes.GpuConst))
        {
            context.Writer.Append("const ");
        }

        context.AppendType(type);
        context.Writer.AppendLine($" {name};");
    }
}
