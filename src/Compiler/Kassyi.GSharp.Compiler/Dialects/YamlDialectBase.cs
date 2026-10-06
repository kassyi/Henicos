namespace Kassyi.GSharp.Compiler.Dialects;

public abstract class YamlDialectBase : ITranspilerDialect
{
    public virtual IEnumerable<string> RequiredHeaders => Array.Empty<string>();
    public virtual string StructPrefix => string.Empty;
    public virtual string MethodPrefix => string.Empty;
    public virtual bool IsCuda => GetType().Name.Contains("Cuda");

    public virtual DialectCapabilities Capabilities =>
        DialectCapabilities.AllGpuFeatures;

    public virtual void AppendType(ref TranspileContext context, ITypeSymbol? typeSymbol)
    {
        if (typeSymbol == null)
        {
            context.Emitter.Writer.Append("void");
            return;
        }

        if (typeSymbol is IArrayTypeSymbol array)
        {
            AppendType(ref context, array.ElementType);
            context.Emitter.Writer.Append("*");
            return;
        }

        if (IsCuda && typeSymbol.TypeKind == TypeKind.Delegate)
        {
            context.Emitter.Writer.Append("auto");
            return;
        }

        if (typeSymbol is IPointerTypeSymbol pointer)
        {
            AppendType(ref context, pointer.PointedAtType);
            context.Emitter.Writer.Append("*");
            return;
        }

        var format = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions &
            ~SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
        // [WHY] Dictionary key lookup for Yaml Dialects requires a string allocation.
        // I tolerate ToDisplayString here because it is not in the execution hot path.
#pragma warning disable RS0030 // Do not used banned APIs
        var name = typeSymbol.ToDisplayString(format).Replace("global::", "");
#pragma warning restore RS0030

        if (TryMapType(name, out var mappedType))
        {
            context.Emitter.Writer.Append(mappedType);
            return;
        }

        context.Emitter.Writer.AppendFullyQualifiedCppName(typeSymbol);

        if (typeSymbol is not INamedTypeSymbol { IsGenericType: true } namedType)
        {
            return;
        }

        context.Emitter.Writer.Append("<");
        for (var i = 0; i < namedType.TypeArguments.Length; i++)
        {
            AppendType(ref context, namedType.TypeArguments[i]);
            if (i < namedType.TypeArguments.Length - 1)
            {
                context.Emitter.Writer.Append(", ");
            }
        }

        context.Emitter.Writer.Append(">");
    }

    public virtual bool TryMapType(string fullyQualifiedName, out string mappedType)
    {
        mappedType = string.Empty;
        return false;
    }

    public virtual bool TryMapIntrinsic(ISymbol symbol, out string mappedExpression)
    {
        var intrinsicAttr = symbol.GetAttribute(KnownAttributes.CudaIntrinsic);
        if (intrinsicAttr is { ConstructorArguments.Length: > 0 })
        {
            mappedExpression = intrinsicAttr.ConstructorArguments[0].Value?.ToString() ?? symbol.Name;
            return true;
        }

        var patternAttr = symbol.ContainingType?.GetAttribute(KnownAttributes.CudaIntrinsicPattern);
        if (patternAttr is { ConstructorArguments.Length: > 0 })
        {
            var pattern = patternAttr.ConstructorArguments[0].Value?.ToString() ?? "{0}";
            mappedExpression = string.Format(pattern, symbol.Name.ToLowerInvariant());
            return true;
        }

        // [WHY] Dictionary key lookup for Yaml Dialects requires a string signature.
        // I tolerate ToDisplayString here because it is not in the execution hot path.
#pragma warning disable RS0030 // Do not used banned APIs
        var name = symbol.ToDisplayString(new SymbolDisplayFormat(
            memberOptions: SymbolDisplayMemberOptions.IncludeParameters |
                           SymbolDisplayMemberOptions.IncludeContainingType,
            parameterOptions: SymbolDisplayParameterOptions.IncludeType |
                              SymbolDisplayParameterOptions.IncludeParamsRefOut,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
        )).Replace("global::", "");
#pragma warning restore RS0030

        return TryMapIntrinsicCore(name, out mappedExpression);
    }

    protected virtual bool TryMapIntrinsicCore(string signature, out string mappedExpression)
    {
        mappedExpression = string.Empty;
        return false;
    }

    public virtual bool TryMapMemberAccess(ITypeSymbol typeSymbol, string memberName, out string? mappedName,
        out bool isMethodCall)
    {
        mappedName = null;
        isMethodCall = false;

        if (typeSymbol.IsSystemNumericsVector())
        {
            mappedName = memberName.ToLowerInvariant();
            return true;
        }

        if (memberName == "Length")
        {
            if (typeSymbol.HasAttribute(KnownAttributes.InlineArray))
            {
                mappedName = "size";
                isMethodCall = true;
                return true;
            }

            mappedName = "length";
            return true;
        }

        if (memberName != "Count")
        {
            return false;
        }

        mappedName = "length";
        return true;
    }

    public virtual (bool IsRefPointer, bool IsPointerType, bool HasRestrict) AppendParameter(
        ref TranspileContext context, ParameterSyntax p, ITypeSymbol? typeSymbol)
    {
        var isRefPointer = p.Modifiers.Any(SyntaxKind.RefKeyword) || p.Modifiers.Any(SyntaxKind.OutKeyword);
        var isPointerType = typeSymbol is IPointerTypeSymbol or IArrayTypeSymbol;

        if (typeSymbol is INamedTypeSymbol { IsGenericType: true, Name: TypeConstants.SharedMemoryType } namedType &&
            namedType.ContainingNamespace.IsCoreNamespace())
        {
            AppendType(ref context, namedType.TypeArguments[0]);
            context.Emitter.Writer.Append("*");
            isPointerType = true;
        }
        else
        {
            AppendType(ref context, typeSymbol);
        }

        if (isRefPointer)
        {
            context.Emitter.Writer.Append("* __restrict__ ");
        }

        return (isRefPointer, isPointerType, isRefPointer);
    }

    public virtual int GetApproximateSize(ITypeSymbol typeSymbol) => 4;

    public virtual CSharpSyntaxRewriter? GetAstPreProcessor(SemanticModel semanticModel) => null;

    public virtual CSharpSyntaxVisitor<bool> GetInterceptorVisitor(ref TranspileContext context) =>
        new DefaultInterceptorVisitor();

    private class DefaultInterceptorVisitor : CSharpSyntaxVisitor<bool>
    {
        public override bool DefaultVisit(SyntaxNode node) => false;
    }
}
