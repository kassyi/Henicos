namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Emits CUDA C++ struct definitions, operator overloads, and handles InlineArray structures.</summary>
internal static class StructTranspiler
{
    /// <summary>
    /// Recursively registers referenced struct symbols and their field types to ensure necessary forward definitions are emitted.
    /// </summary>
    public static void RegisterUsedStructIfApplicable(ITypeSymbol? typeSymbol, ref TranspileContext context)
    {
        if (!TryGetUserStruct(typeSymbol, out var namedStruct))
        {
            return;
        }

        if (!context.Query.UsedStructs.Add(namedStruct))
        {
            return;
        }

        foreach (var member in StructAbiMembers.Get(namedStruct))
        {
            RegisterUsedStructIfApplicable(member.Type, ref context);
        }
    }

    /// <summary>
    /// Topologically sorts referenced structs in the current context so that dependent nested structs are declared first in C++.
    /// </summary>
    public static ImmutableArray<INamedTypeSymbol> GetTopologicallySortedStructs(ref TranspileContext context)
        => GetTopologicallySortedStructs(context.Query.UsedStructs);

    /// <summary>
    /// Topologically sorts referenced structs so that dependent nested structs are declared first in C++.
    /// </summary>
    public static ImmutableArray<INamedTypeSymbol> GetTopologicallySortedStructs(
        IEnumerable<INamedTypeSymbol> usedStructs)
    {
        // [WHY] CUDA C++ requires full struct definitions in dependency order before value-type usage as fields.
        var result = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        var structSet = usedStructs as ISet<INamedTypeSymbol> ??
                        new HashSet<INamedTypeSymbol>(usedStructs, SymbolEqualityComparer.Default);
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var visiting = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var s in structSet)
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

            foreach (var member in StructAbiMembers.Get(s))
            {
                if (TryGetUserStruct(member.Type, out var dep) && structSet.Contains(dep))
                {
                    visit(dep);
                }
            }

            visiting.Remove(s);
            visited.Add(s);
            result.Add(s);
        }
    }

    private static bool TryGetUserStruct(ITypeSymbol? typeSymbol, [NotNullWhen(true)] out INamedTypeSymbol? namedStruct)
    {
        // [WHY] Array-like wrappers (T[], Memory<T>, ImmutableArray<T>, ...) reach the device as GSharpArray<T>; only their element type can be a user struct.
        var structType = typeSymbol.TryGetArrayLikeElementType(out var elementType) ? elementType : typeSymbol;
        if (structType is INamedTypeSymbol candidate && GlobalCppNames.IsEmittedUserType(candidate))
        {
            namedStruct = candidate;
            return true;
        }

        namedStruct = null;
        return false;
    }

    /// <summary>
    /// Emits the C++ struct definition for the specified named struct symbol, handling InlineArray structures and operator overloads.
    /// </summary>
    public static void WriteStructDefinition(INamedTypeSymbol structSym, ref TranspileContext context)
    {
        var hasPack = structSym.TryGetStructLayoutPack(out var packValue);
        if (hasPack)
        {
            context.Emitter.Writer.AppendLine($"#pragma pack(push, {packValue})");
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

        if (hasPack)
        {
            context.Emitter.Writer.AppendLine("#pragma pack(pop)");
        }
    }

    private static void WriteInlineArrayDefinition(INamedTypeSymbol structSym, AttributeData inlineArrayAttr,
        ref TranspileContext context)
    {
        var length = (int)(inlineArrayAttr.ConstructorArguments[0].Value ?? 0);
        if (!TryGetFirstMember(structSym, out var firstMember))
        {
            return;
        }

        var element = GetMemberName(firstMember.Symbol, ref context);
        WriteStructHeader(ref context, structSym);
        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Dialect.AppendType(ref context, firstMember.Type);
            context.Emitter.Writer.Append($" {element}");
            using (context.Emitter.Writer.Brackets())
            {
                context.Emitter.Writer.Append(length);
            }

            context.Emitter.Writer.AppendLine(";");

            WriteInlineArrayIndexer(ref context, firstMember.Type, element, isConst: false);
            WriteInlineArrayIndexer(ref context, firstMember.Type, element, isConst: true);
            WriteInlineArrayIterators(ref context, firstMember.Type, element, length);
        }

        WriteStructFooter(ref context);
    }

    private static void WriteInlineArrayIterators(ref TranspileContext context, ITypeSymbol elementType, string element,
        int length)
    {
        WriteInlineArrayIteratorPair(ref context, elementType, element, length, isConst: false);
        WriteInlineArrayIteratorPair(ref context, elementType, element, length, isConst: true);
        context.Emitter.Writer.AppendLine($"__device__ inline int size() const {{ return {length}; }}");
    }

    private static void WriteInlineArrayIteratorPair(ref TranspileContext context, ITypeSymbol elementType,
        string element, int length, bool isConst)
    {
        var prefix = isConst ? "__device__ inline const " : "__device__ inline ";
        var suffix = isConst ? " const" : string.Empty;

        context.Emitter.Writer.Append(prefix);
        context.Emitter.Dialect.AppendType(ref context, elementType);
        context.Emitter.Writer.AppendLine($"* begin(){suffix} {{ return {element}; }}");

        context.Emitter.Writer.Append(prefix);
        context.Emitter.Dialect.AppendType(ref context, elementType);
        context.Emitter.Writer.AppendLine($"* end(){suffix} {{ return {element} + {length}; }}");
    }

    private static void WriteInlineArrayIndexer(ref TranspileContext context, ITypeSymbol elementType, string element,
        bool isConst)
    {
        context.Emitter.Writer.Append(isConst ? "__device__ inline const " : "__device__ inline ");
        context.Emitter.Dialect.AppendType(ref context, elementType);
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
            context.Emitter.Writer.Append($"return {element}");
            using (context.Emitter.Writer.Brackets())
            {
                context.Emitter.Writer.Append("index");
            }

            context.Emitter.Writer.AppendLine(";");
        }
    }

    private static void WriteStandardStructDefinition(INamedTypeSymbol structSym, ref TranspileContext context)
    {
        WriteStructHeader(ref context, structSym);

        using (context.Emitter.Writer.Scope())
        {
            foreach (var member in StructAbiMembers.Get(structSym))
            {
                WriteStructMember(ref context, member);
            }

            foreach (var member in structSym.GetMembers())
            {
                if (member is IMethodSymbol { MethodKind: MethodKind.Conversion, Name: "op_Implicit" } conv)
                {
                    WriteImplicitConversionOperator(structSym, ref context, conv);
                }
                else if (member is IPropertySymbol property && StructAbiMembers.IsComputedProperty(property))
                {
                    WriteComputedPropertyGetter(property, ref context);
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

                context.Emitter.Writer.Append("typename ").AppendFullyQualifiedCppName(structSym.TypeParameters[i]);
            }

            context.Emitter.Writer.AppendLine(">");
        }
    }

    private static void WriteStructHeader(ref TranspileContext context, INamedTypeSymbol structSym)
    {
        var guardName = CppNameFormatter.GetIncludeGuardName(structSym);

        context.Emitter.Writer.Append("#ifndef ").AppendLine(guardName)
            .Append("#define ").AppendLine(guardName);

        WriteTemplatePrefix(ref context, structSym);

        context.Emitter.Writer.Append("struct ").AppendFullyQualifiedCppName(structSym).AppendLine();
    }

    private static void WriteStructFooter(ref TranspileContext context)
    {
        context.Emitter.Writer
            .AppendLine(";")
            .AppendLine("#endif")
            .AppendLine();
    }

    private static void WriteImplicitConversionOperator(INamedTypeSymbol structSym, ref TranspileContext context,
        IMethodSymbol conv)
    {
        var hasFirstMember = TryGetFirstMember(structSym, out var firstMember);

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

            context.Emitter.Writer.Append(' ');
            using (context.Emitter.Writer.Scope())
            {
                if (hasFirstMember)
                {
                    context.Emitter.Writer.AppendLine($"this->{GetMemberName(firstMember.Symbol, ref context)} = v;");
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
                if (!hasFirstMember)
                {
                    return;
                }

                context.Emitter.Writer.Append($"return this->{GetMemberName(firstMember.Symbol, ref context)}");
                context.Emitter.Writer.AppendLine(";");
            }
        }
    }

    // __device__ inline <type> <property name>() const { <getter body> }
    // [WHY] The getter keeps the C++ name of the property (CppNameResolver), so the implicit `A` and `Sum` in a getter body
    // resolve to the members of the struct, and a read elsewhere only appends the call (ComputedPropertyGetterCall).
    private static void WriteComputedPropertyGetter(IPropertySymbol property, ref TranspileContext context)
    {
        // A struct from metadata or from another compilation has no getter body to bind here.
        var compilation = context.Query.SemanticModel.Compilation;
        if (!property.TryGetDeclaringSyntax<PropertyDeclarationSyntax>(out var declaration) ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree) ||
            !TryGetGetterBody(declaration, out var block, out var expression))
        {
            return;
        }

        context.Emitter.Writer.Append("__device__ inline ");
        context.Emitter.Dialect.AppendType(ref context, property.Type);
        context.Emitter.Writer.Append(' ')
            .AppendCppName(CppNameResolver.Resolve(property, property.Name, context.Emitter.Dialect))
            .Append("() const ");

        // [WHY] The struct may be declared in another file than the kernel, so its body is bound with a model of its own tree.
        var query = context.Query;
        var reporter = context.Reporter;
        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        context.Query = new Query(model, query.BclIntrinsicMap, query.UsedStructs);
        context.Reporter = new Reporter(reporter.ReportDiagnostic, model);

        try
        {
            LambdaTranspilerHelper.TranspileLambdaBody(ref context, block, expression);
        }
        finally
        {
            context.Query = query;
            context.Reporter = reporter;
        }
    }

    // The body of `=> expr`, `get => expr` or `get { ... }`
    private static bool TryGetGetterBody(PropertyDeclarationSyntax declaration, out BlockSyntax? block,
        out ExpressionSyntax? expression)
    {
        block = null;
        expression = declaration.ExpressionBody?.Expression;
        if (expression != null || declaration.AccessorList is not { } accessorList)
        {
            return expression != null;
        }

        var getter = accessorList.Accessors
            .AsStackEnumerable()
            .FirstOrDefault(static accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
        block = getter?.Body;
        expression = getter?.ExpressionBody?.Expression;
        return block != null || expression != null;
    }

    private static string GetMemberName(ISymbol member, ref TranspileContext context) =>
        CppNameResolver.Resolve(member, member.Name, context.Emitter.Dialect).ToString();

    private static bool TryGetFirstMember(INamedTypeSymbol structSym, out StructAbiMember firstMember)
    {
        foreach (var member in StructAbiMembers.Get(structSym))
        {
            firstMember = member;
            return true;
        }

        firstMember = default;
        return false;
    }

    private static void WriteStructMember(ref TranspileContext context, StructAbiMember member)
    {
        if (member is { Kind: StructAbiMemberKind.FixedBuffer, Symbol: IFieldSymbol field })
        {
            field.TryGetDeclaringSyntax<VariableDeclaratorSyntax>(out var syntax);

            ExpressionSyntax? sizeExp = null;
            if (syntax?.ArgumentList is { Arguments.Count: > 0 } argList)
            {
                sizeExp = argList.Arguments[0].Expression;
            }

            if (sizeExp != null)
            {
                context.Emitter.Dialect.AppendType(ref context, member.Type);
                context.Emitter.Writer.Append($" {GetMemberName(field, ref context)}");
                using (context.Emitter.Writer.Brackets())
                {
                    context.Visitor.Visit(sizeExp);
                }

                context.Emitter.Writer.AppendLine(";");
            }
            else
            {
                context.Emitter.Dialect.AppendType(ref context, member.Type);
                context.Emitter.Writer.AppendLine(
                    $"* {GetMemberName(field, ref context)}; // ERROR: fixed size not found");
            }

            return;
        }

        WriteMemberDeclaration(member.Type, GetMemberName(member.Symbol, ref context), member.Symbol, ref context);
    }

    private static void WriteMemberDeclaration(ITypeSymbol type, string name, ISymbol memberSymbol,
        ref TranspileContext context)
    {
        var isReadOnly = memberSymbol.HasAttribute(KnownAttributes.GpuReadOnly);
        var isArray = type.TryGetArrayLikeElementType(out var elementType);

        if (isReadOnly && !isArray)
        {
            context.Emitter.Writer.Append("const ");
        }

        // C# 'volatile' forbids caching/reordering of the field access; C++ 'volatile' gives the same guarantee to nvcc/cl.
        if (memberSymbol is IFieldSymbol { IsVolatile: true })
        {
            context.Emitter.Writer.Append("volatile ");
        }

        if (isArray)
        {
            context.Emitter.Writer.Append("GSharpArray<");
            if (isReadOnly)
            {
                context.Emitter.Writer.Append("const ");
            }

            context.Emitter.Dialect.AppendType(ref context, elementType);
            context.Emitter.Writer.Append(">");
        }
        else
        {
            context.Emitter.Dialect.AppendType(ref context, type);
        }

        context.Emitter.Writer.AppendLine($" {name};");
    }
}
