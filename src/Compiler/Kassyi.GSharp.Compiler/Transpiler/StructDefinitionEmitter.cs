namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Emits the device definition of one struct inside its include guard: the struct with its members and member
/// function declarations, the layout <c>static_assert</c>s, and the functions written after it (constructors,
/// conversion and truth operators).
/// </summary>
internal static class StructDefinitionEmitter
{
    /// <summary>
    /// Writes the definition of an <c>[InlineArray]</c> struct: the element array, indexers and iterators.
    /// </summary>
    public static void WriteInlineArrayDefinition(INamedTypeSymbol structSym, AttributeData inlineArrayAttr,
        ref TranspileContext context)
    {
        var length = (int)(inlineArrayAttr.ConstructorArguments[0].Value ?? 0);
        if (!StructMemberEmitter.TryGetFirstMember(structSym, out var firstMember))
        {
            return;
        }

        var element = StructMemberEmitter.GetMemberName(firstMember.Symbol, ref context);
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

        WriteStructFooter(structSym, isInlineArray: true, ref context);
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

    /// <summary>
    /// Writes the definition of a struct other than an <c>[InlineArray]</c> one, followed by its constructors,
    /// conversion operators and truth operators.
    /// </summary>
    public static void WriteStandardStructDefinition(INamedTypeSymbol structSym, ref TranspileContext context)
    {
        WriteStructHeader(ref context, structSym);

        using (context.Emitter.Writer.Scope())
        {
            var layout = TypeLayoutCalculator.GetStructLayout(structSym, context.Emitter.Dialect);
            if (layout.Kind == TypeLayoutKind.Explicit)
            {
                StructMemberEmitter.WriteExplicitLayoutMembers(ref context, layout);
            }
            else
            {
                StructMemberEmitter.WriteSequentialMembers(structSym, layout, ref context);
            }

            WriteMemberFunctions(structSym, ref context);
        }

        context.Emitter.Writer.AppendLine(";");
        WriteStructStaticAsserts(structSym, isInlineArray: false, ref context);
        StructConstructorEmitter.WriteStructConstructors(structSym, ref context);
        StructConversionEmitter.WriteStructConversionOperators(structSym, ref context);
        StructTruthOperatorEmitter.WriteTruthOperators(structSym, ref context);
        context.Emitter.Writer
            .AppendLine("#endif")
            .AppendLine();
    }

    // The member functions inside the struct: the conversion members of a ref struct, the computed properties, and the
    // declarations of Dispose and of the sort comparison members
    private static void WriteMemberFunctions(INamedTypeSymbol structSym, ref TranspileContext context)
    {
        var hasDefaultConstructor = false;
        foreach (var member in structSym.GetMembers())
        {
            if (member is IMethodSymbol { MethodKind: MethodKind.Conversion } conversion)
            {
                if (structSym.IsRefLikeType)
                {
                    StructConversionEmitter.WriteRefStructConversionMember(structSym, conversion,
                        ref hasDefaultConstructor, ref context);
                }
            }
            else if (member is IPropertySymbol property && StructAbiMembers.IsComputedProperty(property))
            {
                StructPropertyEmitter.WriteComputedProperty(property, ref context);
            }
        }

        // [WHY] Declared in every definition, defined only by the methods that dispose the struct (UsingTranspiler):
        // the include guard keeps the first definition of the struct that any method writes.
        if (UsingTranspiler.FindDisposeMethod(structSym, context.Query.SemanticModel.Compilation) != null)
        {
            context.Emitter.Writer.AppendLine($"__device__ void {GSharpInternalNames.Using.Dispose}();");
        }

        // Declared the same way, defined only by the methods that sort with them (SortComparisonMembers)
        SortComparisonMembers.WriteDeclarations(structSym, ref context);
    }

    private static void WriteStructHeader(ref TranspileContext context, INamedTypeSymbol structSym)
    {
        var guardName = CppNameFormatter.GetIncludeGuardName(structSym);

        context.Emitter.Writer.Append("#ifndef ").AppendLine(guardName)
            .Append("#define ").AppendLine(guardName);

        context.Emitter.Writer.Append("struct ").AppendFullyQualifiedCppName(structSym).AppendLine();
    }

    private static void WriteStructStaticAsserts(INamedTypeSymbol structSym, bool isInlineArray,
        ref TranspileContext context)
    {
        var layout = TypeLayoutCalculator.GetStructLayout(structSym, context.Emitter.Dialect);
        var csType = GlobalCppNames.GetTypePath(structSym).ToDisplayString();
        context.Emitter.Writer.Append("static_assert(sizeof(")
            .AppendFullyQualifiedCppName(structSym)
            .Append($")=={layout.Device.Size},\"{csType}: size\");")
            .AppendLine();
        context.Emitter.Writer.Append("static_assert(alignof(")
            .AppendFullyQualifiedCppName(structSym)
            .Append($")=={layout.Device.Alignment},\"{csType}: alignment\");")
            .AppendLine();

        if (isInlineArray)
        {
            return;
        }

        foreach (var field in layout.DeviceFields)
        {
            var cppMember = StructMemberEmitter.GetMemberName(field.Member.Symbol, ref context);
            var csMember = field.Member.Name;
            context.Emitter.Writer.Append("static_assert(offsetof(")
                .AppendFullyQualifiedCppName(structSym)
                .Append($",{cppMember})=={field.Offset},\"{csType}.{csMember}: offset\");")
                .AppendLine();
        }
    }

    private static void WriteStructFooter(INamedTypeSymbol structSym, bool isInlineArray, ref TranspileContext context)
    {
        context.Emitter.Writer
            .AppendLine(";");
        WriteStructStaticAsserts(structSym, isInlineArray, ref context);
        context.Emitter.Writer
            .AppendLine("#endif")
            .AppendLine();
    }
}
