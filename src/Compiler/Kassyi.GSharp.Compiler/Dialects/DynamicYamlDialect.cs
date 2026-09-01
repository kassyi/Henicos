using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Compiler.Transpiler.Dialects;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Kassyi.GSharp.Compiler.Dialects;

public class DynamicYamlDialect : ITranspilerDialect
{
    private readonly DialectDefinition _def;
    private readonly DynamicYamlDialect? _baseDialect;

    public IEnumerable<string> RequiredHeaders => _def.RequiredHeaders.Concat(_baseDialect?.RequiredHeaders ?? Enumerable.Empty<string>()).Distinct();
    public string StructPrefix => !string.IsNullOrEmpty(_def.StructPrefix) ? _def.StructPrefix : (_baseDialect?.StructPrefix ?? string.Empty);
    public string MethodPrefix => !string.IsNullOrEmpty(_def.MethodPrefix) ? _def.MethodPrefix : (_baseDialect?.MethodPrefix ?? string.Empty);

    private DynamicYamlDialect(DialectDefinition def, DynamicYamlDialect? baseDialect)
    {
        _def = def;
        _baseDialect = baseDialect;
    }

    public static DynamicYamlDialect Load(string dialectName)
    {
        var definitions = new Dictionary<string, DialectDefinition>();
        var assembly = Assembly.GetExecutingAssembly();
        
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        foreach (var resourceName in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".yml") || n.EndsWith(".yaml")))
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) continue;
            using var reader = new StreamReader(stream);
            try
            {
                var def = deserializer.Deserialize<DialectDefinition>(reader.ReadToEnd());
                if (def != null && !string.IsNullOrEmpty(def.Name))
                {
                    definitions[def.Name] = def;
                }
            }
            catch
            {
                // Ignore parse errors for now
            }
        }

        return BuildDialect(dialectName, definitions);
    }

    private static DynamicYamlDialect BuildDialect(string name, Dictionary<string, DialectDefinition> definitions)
    {
        if (!definitions.TryGetValue(name, out var def))
        {
            throw new ArgumentException($"Dialect '{name}' not found in embedded YAML resources.");
        }

        DynamicYamlDialect? baseDialect = null;
        if (!string.IsNullOrEmpty(def.Inherits))
        {
            baseDialect = BuildDialect(def.Inherits, definitions);
        }

        return new DynamicYamlDialect(def, baseDialect);
    }

    public void AppendType(ref TranspileContext context, ITypeSymbol? typeSymbol)
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
        
        if (_def.Types != null && _def.Types.TryGetValue(name, out var mapped))
        {
            context.Writer.Append(mapped);
            return;
        }

        if (_baseDialect != null)
        {
            _baseDialect.AppendType(ref context, typeSymbol);
            return;
        }

        context.Writer.AppendFullyQualifiedCppName(typeSymbol);
        
        if (typeSymbol is INamedTypeSymbol { IsGenericType: true } namedType)
        {
            context.Writer.Append("<");
            for (int i = 0; i < namedType.TypeArguments.Length; i++)
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

    public bool TryMapIntrinsic(ISymbol symbol, out string mappedExpression)
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

        if (_def.Intrinsics != null && _def.Intrinsics.TryGetValue(name, out var mapped))
        {
            mappedExpression = mapped;
            return true;
        }

        if (_baseDialect != null)
        {
            return _baseDialect.TryMapIntrinsic(symbol, out mappedExpression);
        }

        mappedExpression = string.Empty;
        return false;
    }

    public (bool IsRefPointer, bool IsPointerType, bool HasRestrict) AppendParameter(ref TranspileContext context, ParameterSyntax p, ITypeSymbol? typeSymbol)
    {
        // Simple fallback
        bool isRefPointer = p.Modifiers.Any(SyntaxKind.RefKeyword) || p.Modifiers.Any(SyntaxKind.OutKeyword);
        bool isPointerType = typeSymbol is IPointerTypeSymbol || typeSymbol is IArrayTypeSymbol;
        
        AppendType(ref context, typeSymbol);
        if (isRefPointer)
        {
            context.Writer.Append("* __restrict__ ");
        }
        
        return (isRefPointer, isPointerType, isRefPointer);
    }

    public int GetApproximateSize(ITypeSymbol typeSymbol)
    {
        return 4; // Dummy fallback for now
    }

    public CSharpSyntaxRewriter? GetAstPreProcessor(SemanticModel semanticModel) => null;

    public CSharpSyntaxVisitor<bool> GetInterceptorVisitor(ref TranspileContext context)
    {
        return new DefaultInterceptorVisitor();
    }

    private class DefaultInterceptorVisitor : CSharpSyntaxVisitor<bool>
    {
        public override bool DefaultVisit(SyntaxNode node) => false;
    }
}
