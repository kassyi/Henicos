#nullable enable
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Tests.Generators;

/// <summary>
/// Reports, inside the G# compiler, a type or attribute matched by its name alone. A user type that shares the name
/// (<c>Gpu</c>, <c>Warp</c>, <c>Span</c>, an attribute class) would match as well. Repository-internal, like
/// <see cref="TestArchitectureAnalyzer"/>; it runs only in a compilation that declares
/// <c>Kassyi.GSharp.Compiler.TypeConstants</c>.
/// </summary>
/// <remarks>
/// The names come from the constants of <c>TypeConstants</c> whose names end in <c>Type</c>, and from the constants of
/// <c>KnownAttributes</c> (with and without the <c>Attribute</c> suffix). A match counts as checked when the same member
/// also looks at a namespace (<c>ContainingNamespace</c>, <c>IsCoreNamespace</c>, <c>IsSystem*</c>, <c>HasAttribute</c>
/// ...). Syntax-only checks compare token text, not a symbol name, so they are not reported. A check whose namespace is
/// guaranteed by a caller is suppressed with <c>#pragma warning disable HNT0003</c> and a comment that names the caller.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class TypeNameCheckAnalyzer : DiagnosticAnalyzer
{
    private const string CompilerNamespace = "Kassyi.GSharp.Compiler";
    private const string AttributeSuffix = "Attribute";

#pragma warning disable RS2008 // Repository-internal analyzer; not shipped, so analyzer release tracking does not apply.
    public static readonly DiagnosticDescriptor NameOnlyTypeCheck = new(
        id: "HNT0003",
        title: "Type matched by name without its namespace",
        messageFormat: "'{0}' matches the type by name only, so a user type named '{1}' matches as well. Check the namespace in the same member (IsCoreNamespace, IsSystemNamespace, ...) or use HasAttribute / IsAttribute.",
        category: "GSharpCompilerArchitecture",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
#pragma warning restore RS2008

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(NameOnlyTypeCheck);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static compilationContext =>
        {
            // The compiler's own assembly, not a compilation that merely references it
            var assembly = compilationContext.Compilation.Assembly;
            var typeConstants = assembly.GetTypeByMetadataName($"{CompilerNamespace}.TypeConstants");
            if (typeConstants is null)
            {
                return;
            }

            var knownAttributes = assembly.GetTypeByMetadataName($"{CompilerNamespace}.KnownAttributes");
            var analysis = new Analysis(CollectNames(typeConstants, knownAttributes), FindIsAttributeName(knownAttributes));

            compilationContext.RegisterOperationAction(analysis.AnalyzeBinary, OperationKind.Binary);
            compilationContext.RegisterOperationAction(analysis.AnalyzeIsPattern, OperationKind.IsPattern);
            compilationContext.RegisterOperationAction(analysis.AnalyzePropertySubpattern, OperationKind.PropertySubpattern);
            compilationContext.RegisterOperationAction(analysis.AnalyzeSwitchExpression, OperationKind.SwitchExpression);
            compilationContext.RegisterOperationAction(analysis.AnalyzeInvocation, OperationKind.Invocation);
        });
    }

    private static HashSet<string> CollectNames(INamedTypeSymbol typeConstants, INamedTypeSymbol? knownAttributes)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in typeConstants.GetMembers())
        {
            if (member is IFieldSymbol { HasConstantValue: true, ConstantValue: string value } field &&
                field.Name.EndsWith("Type", StringComparison.Ordinal))
            {
                names.Add(value);
            }
        }

        if (knownAttributes is null)
        {
            return names;
        }

        foreach (var member in knownAttributes.GetMembers())
        {
            if (member is IFieldSymbol { HasConstantValue: true, ConstantValue: string value, DeclaredAccessibility: Accessibility.Public })
            {
                names.Add(value);
                names.Add(value + AttributeSuffix);
            }
        }

        return names;
    }

    private static IMethodSymbol? FindIsAttributeName(INamedTypeSymbol? knownAttributes)
    {
        if (knownAttributes is null)
        {
            return null;
        }

        foreach (var member in knownAttributes.GetMembers("IsAttributeName"))
        {
            if (member is IMethodSymbol method)
            {
                return method;
            }
        }

        return null;
    }

    private sealed class Analysis(HashSet<string> names, IMethodSymbol? isAttributeName)
    {
        // x.Name == TypeConstants.GpuType, x.Name != "Warp"
        public void AnalyzeBinary(OperationAnalysisContext context)
        {
            var binary = (IBinaryOperation)context.Operation;
            if (binary.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals))
            {
                return;
            }

            if ((IsSymbolName(binary.LeftOperand) && TryGetName(binary.RightOperand, out var name)) ||
                (IsSymbolName(binary.RightOperand) && TryGetName(binary.LeftOperand, out name)))
            {
                ReportUnlessChecked(context, binary.Syntax, name);
            }
        }

        // x.Name is TypeConstants.SpanType or TypeConstants.ReadOnlySpanType
        public void AnalyzeIsPattern(OperationAnalysisContext context)
        {
            var isPattern = (IIsPatternOperation)context.Operation;
            if (IsSymbolName(isPattern.Value) && TryGetName(isPattern.Pattern, out var name))
            {
                ReportUnlessChecked(context, isPattern.Syntax, name);
            }
        }

        // x is { Name: TypeConstants.GpuType }, x is { ContainingType.Name: TypeConstants.GpuType }
        public void AnalyzePropertySubpattern(OperationAnalysisContext context)
        {
            var subpattern = (IPropertySubpatternOperation)context.Operation;
            if (IsSymbolName(subpattern.Member) && TryGetName(subpattern.Pattern, out var name))
            {
                ReportUnlessChecked(context, subpattern.Syntax, name);
            }
        }

        // x.Name switch { TypeConstants.WarpType => ... }
        public void AnalyzeSwitchExpression(OperationAnalysisContext context)
        {
            var switchExpression = (ISwitchExpressionOperation)context.Operation;
            if (!IsSymbolName(switchExpression.Value))
            {
                return;
            }

            foreach (var arm in switchExpression.Arms)
            {
                if (TryGetName(arm.Pattern, out var name))
                {
                    ReportUnlessChecked(context, switchExpression.Value.Syntax, name);
                    return;
                }
            }
        }

        // KnownAttributes.IsAttributeName(attribute.AttributeClass?.Name, KnownAttributes.GpuKernel)
        public void AnalyzeInvocation(OperationAnalysisContext context)
        {
            var invocation = (IInvocationOperation)context.Operation;
            if (isAttributeName is null ||
                !SymbolEqualityComparer.Default.Equals(invocation.TargetMethod, isAttributeName) ||
                invocation.Arguments.Length < 2 ||
                !IsSymbolName(invocation.Arguments[0].Value))
            {
                return;
            }

            var shortName = invocation.Arguments[1].Value.ConstantValue is { HasValue: true, Value: string value }
                ? value
                : invocation.Arguments[1].Syntax.ToString();
            ReportUnlessChecked(context, invocation.Syntax, shortName);
        }

        private bool TryGetName(IOperation operation, out string name)
        {
            switch (operation)
            {
                case IConstantPatternOperation constant:
                    return TryGetName(constant.Value, out name);
                case IBinaryPatternOperation binary:
                    return TryGetName(binary.LeftPattern, out name) || TryGetName(binary.RightPattern, out name);
                case INegatedPatternOperation negated:
                    return TryGetName(negated.Pattern, out name);
                default:
                    if (operation.ConstantValue is { HasValue: true, Value: string value } && names.Contains(value))
                    {
                        name = value;
                        return true;
                    }

                    name = string.Empty;
                    return false;
            }
        }

        private static void ReportUnlessChecked(OperationAnalysisContext context, SyntaxNode node, string name)
        {
            if (!ChecksNamespace(EnclosingMember(node)))
            {
                context.ReportDiagnostic(Diagnostic.Create(NameOnlyTypeCheck, node.GetLocation(), node.ToString(), name));
            }
        }
    }

    // ISymbol.Name read from a symbol that can be a type: ISymbol, ITypeSymbol, INamedTypeSymbol, ITypeParameterSymbol
    private static bool IsSymbolName(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        if (operation is IConditionalAccessOperation conditionalAccess)
        {
            operation = conditionalAccess.WhenNotNull;
        }

        return operation is IPropertyReferenceOperation { Property: { Name: "Name" } property, Instance.Type: { } receiver } &&
               IsRoslynSymbolInterface(property.ContainingType, "ISymbol") &&
               (IsRoslynSymbolInterface(receiver, "ISymbol") ||
                IsRoslynSymbolInterface(receiver, "ITypeSymbol") ||
                IsRoslynSymbolInterface(receiver, "INamedTypeSymbol") ||
                IsRoslynSymbolInterface(receiver, "ITypeParameterSymbol"));
    }

    private static bool IsRoslynSymbolInterface(ITypeSymbol type, string name) =>
        type is { Name: var typeName, ContainingNamespace: { Name: "CodeAnalysis", ContainingNamespace: { Name: "Microsoft", ContainingNamespace.IsGlobalNamespace: true } } } &&
        typeName == name;

    // The method, property, constructor or field that holds the check; local functions and lambdas belong to it.
    private static SyntaxNode EnclosingMember(SyntaxNode node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current is MemberDeclarationSyntax and not BaseTypeDeclarationSyntax and not BaseNamespaceDeclarationSyntax)
            {
                return current;
            }
        }

        return node;
    }

    private static bool ChecksNamespace(SyntaxNode member)
    {
        foreach (var node in member.DescendantNodes())
        {
            if (node is not IdentifierNameSyntax identifier)
            {
                continue;
            }

            var text = identifier.Identifier.ValueText;
            if (text is "ContainingNamespace" or "ContainingAssembly" or "HasAttribute" or "GetAttribute" or "IsAttribute" ||
                (text.StartsWith("Is", StringComparison.Ordinal) &&
                 (text.EndsWith("Namespace", StringComparison.Ordinal) || text.StartsWith("IsSystem", StringComparison.Ordinal))))
            {
                return true;
            }
        }

        return false;
    }
}
