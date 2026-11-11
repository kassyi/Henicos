namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Emits user-defined struct conversion operators (<c>implicit operator</c> and <c>explicit operator</c>) as
/// value-returning device functions (<c>__gsharp_fn_...__op_Implicit</c> / <c>__op_Explicit</c>).
/// </summary>
internal static class StructConversionEmitter
{
    private static readonly ConditionalWeakTable<Compilation, StrongBox<bool>> s_hasConversionsCache = new();

    /// <summary>
    /// Whether <paramref name="compilation"/> declares or imports any emitted struct conversion operators.
    /// Used by <see cref="Helpers.UserDefinedConversionHelper.TryAppendImplicitConversion"/> to bypass per-expression
    /// semantic model queries in compilations without conversions.
    /// </summary>
    public static bool HasEmittedStructConversions(Compilation compilation) =>
        s_hasConversionsCache.GetValue(compilation, static comp =>
        {
            if (comp.ContainsSymbolsWithName(WellKnownMemberNames.ImplicitConversionName, SymbolFilter.Member) ||
                comp.ContainsSymbolsWithName(WellKnownMemberNames.ExplicitConversionName, SymbolFilter.Member))
            {
                return new StrongBox<bool>(true);
            }

            return new StrongBox<bool>(DeviceExportTable.HasAnyConversion(comp));
        }).Value;

    /// <summary>
    /// Whether <paramref name="conversion"/> is a user-defined struct conversion operator (<c>implicit operator</c> or
    /// <c>explicit operator</c>) emitted as a value-returning device function (<c>__gsharp_fn_...__op_Implicit</c> /
    /// <c>__op_Explicit</c>). A <c>ref struct</c> (such as <c>GpuBigIntRef</c>) keeps its inline C++ member conversion
    /// operators for <c>[GpuOperatorMacro]</c> buffer forwarding.
    /// </summary>
    public static bool IsEmittedStructConversion(IMethodSymbol? conversion, Compilation compilation) =>
        conversion is { MethodKind: MethodKind.Conversion, IsImplicitlyDeclared: false } &&
        !conversion.HasAttribute(KnownAttributes.GpuOperatorMacro) &&
        conversion.ContainingType is { IsRefLikeType: false } containingType &&
        GlobalCppNames.IsEmittedUserType(containingType) &&
        (TryGetConversionDeclaration(conversion, out _) ||
         (DeviceExportTable.IsOutside(conversion, compilation) && DeviceExportTable.TryFind(conversion, out _)));

    public static bool TryGetConversionDeclaration(IMethodSymbol conversion,
        [NotNullWhen(true)] out ConversionOperatorDeclarationSyntax? declaration)
    {
        if (conversion.TryGetDeclaringSyntax<ConversionOperatorDeclarationSyntax>(out declaration))
        {
            return declaration.Body != null || declaration.ExpressionBody != null;
        }

        declaration = null;
        return false;
    }

    /// <summary>
    /// Whether <paramref name="conversion"/> is a conversion operator of a <c>ref struct</c> with a body in
    /// <paramref name="compilation"/>. It is emitted as a device function like the conversion of any other struct, and
    /// the C++ conversion members of the struct (a converting constructor, a conversion operator) call it
    /// (<see cref="WriteStructConversionOperators"/>). A conversion from another assembly has no body here and keeps
    /// the members that read or write the first field (<c>GpuBigIntRef</c>).
    /// </summary>
    public static bool HasRefStructConversionBody(IMethodSymbol conversion, Compilation compilation) =>
        conversion is { MethodKind: MethodKind.Conversion, IsImplicitlyDeclared: false } &&
        !conversion.HasAttribute(KnownAttributes.GpuOperatorMacro) &&
        conversion.ContainingType is { IsRefLikeType: true } &&
        TryGetConversionDeclaration(conversion, out var declaration) &&
        compilation.ContainsSyntaxTree(declaration.SyntaxTree);

    public static void WriteStructConversionOperators(INamedTypeSymbol structSym, ref TranspileContext context)
    {
        var compilation = context.Query.SemanticModel.Compilation;
        if (structSym.IsRefLikeType)
        {
            WriteRefStructConversions(structSym, ref context);
            return;
        }

        foreach (var member in structSym.GetMembers())
        {
            if (member is IMethodSymbol conversion &&
                IsEmittedStructConversion(conversion, compilation) &&
                TryGetConversionDeclaration(conversion, out var declaration) &&
                compilation.ContainsSyntaxTree(declaration.SyntaxTree))
            {
                WriteOperatorFunction(conversion, declaration, ref context);
            }
        }
    }

    /// <summary>
    /// Declares, inside the definition of the ref struct <paramref name="structSym"/>, the C++ member that carries
    /// <paramref name="conversion"/>: the declaration of a member defined after the struct for a conversion with a body
    /// here (<see cref="HasRefStructConversionBody"/>), or, for any other <c>implicit operator</c>, an inline member
    /// that reads or writes the first field. A converting constructor first declares the default constructor once
    /// (<paramref name="hasDefaultConstructor"/>).
    /// </summary>
    public static void WriteRefStructConversionMember(INamedTypeSymbol structSym, IMethodSymbol conversion,
        ref bool hasDefaultConstructor, ref TranspileContext context)
    {
        if (HasRefStructConversionBody(conversion, context.Query.SemanticModel.Compilation))
        {
            WriteRefStructConversionDeclaration(structSym, ref context, conversion, ref hasDefaultConstructor);
        }
        else if (conversion.Name == WellKnownMemberNames.ImplicitConversionName)
        {
            WriteImplicitConversionOperator(structSym, ref context, conversion, ref hasDefaultConstructor);
        }
    }

    // __device__ inline [explicit] S(<type> v); or __device__ inline [explicit] operator <type>() const;
    // The member of a ref struct conversion with a body here; WriteRefStructConversions defines it after the struct,
    // calling the function emitted from the body. The member stays because an operator macro passes a ref struct to a
    // parameter of the type it converts to, and back, through these C++ conversions.
    private static void WriteRefStructConversionDeclaration(INamedTypeSymbol structSym, ref TranspileContext context,
        IMethodSymbol conversion, ref bool hasDefaultConstructor)
    {
        var writer = context.Emitter.Writer;
        var explicitKeyword = conversion.Name == WellKnownMemberNames.ExplicitConversionName ? "explicit " : "";
        if (conversion.ReturnType.IsEqualTo(structSym))
        {
            WriteDefaultConstructorOnce(structSym, ref context, ref hasDefaultConstructor);
            writer.Append($"__device__ inline {explicitKeyword}").AppendFullyQualifiedCppName(structSym);
            using (writer.Parentheses())
            {
                context.Emitter.Dialect.AppendType(ref context, conversion.Parameters[0].Type);
                writer.Append(" v");
            }

            writer.AppendLine(";");
            return;
        }

        writer.Append($"__device__ inline {explicitKeyword}operator ");
        context.Emitter.Dialect.AppendType(ref context, conversion.ReturnType);
        writer.AppendLine("() const;");
    }

    // __device__ inline S(<type> v) { this-><first member> = v; }
    // or __device__ inline operator <type>() const { return this-><first member>; }
    private static void WriteImplicitConversionOperator(INamedTypeSymbol structSym, ref TranspileContext context,
        IMethodSymbol conv, ref bool hasDefaultConstructor)
    {
        var hasFirstMember = StructMemberEmitter.TryGetFirstMember(structSym, out var firstMember);
        if (conv.ReturnType.IsEqualTo(structSym))
        {
            // Implicit constructor from param type, after the default constructor that the struct declares once
            WriteDefaultConstructorOnce(structSym, ref context, ref hasDefaultConstructor);
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
                    var name = StructMemberEmitter.GetMemberName(firstMember, ref context);
                    context.Emitter.Writer.AppendLine($"this->{name} = v;");
                }
            }

            return;
        }

        // Implicit conversion operator to return type
        context.Emitter.Writer.Append("__device__ inline operator ");
        context.Emitter.Dialect.AppendType(ref context, conv.ReturnType);
        context.Emitter.Writer.Append("() const ");
        using (context.Emitter.Writer.Scope())
        {
            if (hasFirstMember)
            {
                var name = StructMemberEmitter.GetMemberName(firstMember, ref context);
                context.Emitter.Writer.Append($"return this->{name}");
                context.Emitter.Writer.AppendLine(";");
            }
        }
    }

    // __device__ inline S() {}
    // A constructor of its own takes away the implicit default constructor that S{} needs
    private static void WriteDefaultConstructorOnce(INamedTypeSymbol structSym, ref TranspileContext context,
        ref bool hasDefaultConstructor)
    {
        if (hasDefaultConstructor)
        {
            return;
        }

        context.Emitter.Writer.Append("__device__ inline ").AppendFullyQualifiedCppName(structSym).AppendLine("() {}");
        hasDefaultConstructor = true;
    }

    // Each conversion with a body as a function, then the members that WriteRefStructConversionMember declares in the
    // struct:
    // __device__ inline S::S(<type> v) { *this = <function>(v); }
    // __device__ inline S::operator <type>() const { return <function>(*this); }
    private static void WriteRefStructConversions(INamedTypeSymbol structSym, ref TranspileContext context)
    {
        var compilation = context.Query.SemanticModel.Compilation;
        foreach (var member in structSym.GetMembers())
        {
            if (member is not IMethodSymbol conversion ||
                !HasRefStructConversionBody(conversion, compilation) ||
                !TryGetConversionDeclaration(conversion, out var declaration))
            {
                continue;
            }

            WriteOperatorFunction(conversion, declaration, ref context);

            var writer = context.Emitter.Writer;
            var function = GlobalCppNames.GetFunctionName(conversion);
            writer.Append("__device__ inline ").AppendFullyQualifiedCppName(structSym).Append("::");
            if (conversion.ReturnType.IsEqualTo(structSym))
            {
                writer.AppendFullyQualifiedCppName(structSym);
                using (writer.Parentheses())
                {
                    context.Emitter.Dialect.AppendType(ref context, conversion.Parameters[0].Type);
                    writer.Append(" v");
                }

                writer.AppendLine($" {{ *this = {function}(v); }}");
            }
            else
            {
                writer.Append("operator ");
                context.Emitter.Dialect.AppendType(ref context, conversion.ReturnType);
                writer.AppendLine($"() const {{ return {function}(*this); }}");
            }
        }
    }

    /// <summary>
    /// Writes the user-defined operator <paramref name="method"/> of a struct (a conversion, or the
    /// <c>operator true</c> or <c>operator false</c> of <see cref="StructTruthOperatorEmitter"/>) as a value-returning
    /// device function named by <see cref="GlobalCppNames.GetFunctionName"/>, with the body of
    /// <paramref name="declaration"/>.
    /// </summary>
    public static void WriteOperatorFunction(IMethodSymbol method, BaseMethodDeclarationSyntax declaration,
        ref TranspileContext context)
    {
        var saved = SemanticModelSwitch.Enter(ref context, declaration.SyntaxTree);
        var previousCtorType = context.Emitter.StructConstructorType;
        var previousCallableDepth = context.Emitter.CallableDepth;
        context.Emitter.StructConstructorType = null;
        context.Emitter.CallableDepth = 0;

        try
        {
            context.Emitter.Writer.Append("__device__ inline ");
            context.Emitter.Dialect.AppendType(ref context, method.ReturnType);
            context.Emitter.Writer.Append(' ').Append(GlobalCppNames.GetFunctionName(method));
            context.Visitor.Visit(declaration.ParameterList);
            context.Emitter.Writer.Append(' ');

            LambdaTranspilerHelper.TranspileLambdaBody(
                ref context,
                declaration.Body,
                declaration.ExpressionBody?.Expression,
                isVoidExpression: false);
        }
        finally
        {
            context.Emitter.StructConstructorType = previousCtorType;
            context.Emitter.CallableDepth = previousCallableDepth;
            saved.Restore(ref context);
        }
    }
}
