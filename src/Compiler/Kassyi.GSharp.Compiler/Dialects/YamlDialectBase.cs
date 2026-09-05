
namespace Kassyi.GSharp.Compiler.Dialects;

public abstract class YamlDialectBase : ITranspilerDialect
{
    public virtual IEnumerable<string> RequiredHeaders => Enumerable.Empty<string>();
    public virtual string StructPrefix => string.Empty;
    public virtual string MethodPrefix => string.Empty;
    public virtual bool IsCuda => GetType().Name.Contains("Cuda");
    public virtual DialectCapabilities Capabilities => IsCuda ? DialectCapabilities.AllGpuFeatures : DialectCapabilities.None;

    public virtual void AppendType(ref TranspileContext context, ITypeSymbol? typeSymbol)
    {
        if (typeSymbol == null)
        {
            context.Writer.Append("void");
            return;
        }

        if (typeSymbol is IArrayTypeSymbol array)
        {
            AppendType(ref context, array.ElementType);
            context.Writer.Append("*");
            return;
        }

        if (typeSymbol is IPointerTypeSymbol pointer)
        {
            AppendType(ref context, pointer.PointedAtType);
            context.Writer.Append("*");
            return;
        }

        var format = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions & ~SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
        var name = typeSymbol.ToDisplayString(format).Replace("global::", "");
        
        if (TryMapType(name, out var mappedType))
        {
            context.Writer.Append(mappedType);
            return;
        }

        context.Writer.AppendFullyQualifiedCppName(typeSymbol);
        
        if (typeSymbol is INamedTypeSymbol { IsGenericType: true } namedType)
        {
            context.Writer.Append("<");
            for (var i = 0; i < namedType.TypeArguments.Length; i++)
            {
                AppendType(ref context, namedType.TypeArguments[i]);
                if (i < namedType.TypeArguments.Length - 1)
                {
                    context.Writer.Append(", ");
                }
            }
            context.Writer.Append(">");
        }
    }

    public virtual bool TryMapType(string fullyQualifiedName, out string mappedType)
    {
        mappedType = string.Empty;
        return false;
    }

    public virtual bool TryMapIntrinsic(ISymbol symbol, out string mappedExpression)
    {
        var intrinsicAttr = symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "CudaIntrinsicAttribute");
        if (intrinsicAttr is { ConstructorArguments.Length: > 0 })
        {
            mappedExpression = intrinsicAttr.ConstructorArguments[0].Value?.ToString() ?? symbol.Name;
            return true;
        }

        if (symbol.ContainingType != null)
        {
            var patternAttr = symbol.ContainingType.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "CudaIntrinsicPatternAttribute");
            if (patternAttr is { ConstructorArguments.Length: > 0 })
            {
                var pattern = patternAttr.ConstructorArguments[0].Value?.ToString() ?? "{0}";
                mappedExpression = string.Format(pattern, symbol.Name.ToLowerInvariant());
                return true;
            }
        }

        var name = symbol.ToDisplayString(new SymbolDisplayFormat(
            memberOptions: SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeContainingType,
            parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeParamsRefOut,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
        )).Replace("global::", "");

        return TryMapIntrinsicCore(name, out mappedExpression);
    }

    protected virtual bool TryMapIntrinsicCore(string signature, out string mappedExpression)
    {
        mappedExpression = string.Empty;
        return false;
    }

    public virtual (bool IsRefPointer, bool IsPointerType, bool HasRestrict) AppendParameter(ref TranspileContext context, ParameterSyntax p, ITypeSymbol? typeSymbol)
    {
        var isRefPointer = p.Modifiers.Any(SyntaxKind.RefKeyword) || p.Modifiers.Any(SyntaxKind.OutKeyword);
        var isPointerType = typeSymbol is IPointerTypeSymbol || typeSymbol is IArrayTypeSymbol;
        
        AppendType(ref context, typeSymbol);
        if (isRefPointer)
        {
            context.Writer.Append("* __restrict__ ");
        }
        
        return (isRefPointer, isPointerType, isRefPointer);
    }

    public virtual int GetApproximateSize(ITypeSymbol typeSymbol) => 4;

    public virtual CSharpSyntaxRewriter? GetAstPreProcessor(SemanticModel semanticModel) => null;

    public virtual CSharpSyntaxVisitor<bool> GetInterceptorVisitor(ref TranspileContext context)
    {
        return new DefaultInterceptorVisitor();
    }

    private class DefaultInterceptorVisitor : CSharpSyntaxVisitor<bool>
    {
        public override bool DefaultVisit(SyntaxNode node) => false;
    }
}
