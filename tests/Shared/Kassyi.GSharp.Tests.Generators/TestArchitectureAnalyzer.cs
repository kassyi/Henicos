#nullable enable
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Tests.Generators;

/// <summary>
/// Enforces this repository's own test conventions. Repository-internal: it is not part of the shipped
/// <c>Kassyi.GSharp.Compiler</c> analyzer assembly, so it never runs in user projects (issue #460).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class TestArchitectureAnalyzer : DiagnosticAnalyzer
{
#pragma warning disable RS2008 // Repository-internal analyzer; not shipped, so analyzer release tracking does not apply.
    // Title and severity are carried over from the former diagnostics.yml entry (formerly in the GSH range); the
    // message names the attributes that count. IDs use the repository-internal HNT prefix.
    public static readonly DiagnosticDescriptor TestMethodMustHaveCategoryTrait = new(
        id: "HNT0002",
        title: "Test methods must have a Category trait",
        messageFormat: "Test method '{0}' has no category. Add [Category(TestCategory.X)], [TestTarget(ExecutionTarget.X)] or [Scenario(TestScenario.X)] to the method or its class.",
        category: "GSharpTestArchitecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RawTraitAttributeNotAllowed = new(
        id: "HNT0008",
        title: "Raw [Trait] attribute in a test assembly",
        messageFormat: "[Trait] on '{0}' is not allowed. Use [Category(TestCategory.X)], [TestTarget(ExecutionTarget.X)] or [Scenario(TestScenario.X)] instead.",
        category: "GSharpTestArchitecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor WindowsOnlyTestMustHavePlatformAttribute = new(
        id: "HNT0011",
        title: "Windows-only test must declare [Platform(TestPlatform.Windows)]",
        messageFormat: "Test method '{0}' skips or is restricted to Windows. Add [Platform(TestPlatform.Windows)] to the method or its class.",
        category: "GSharpTestArchitecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);
#pragma warning restore RS2008

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            TestMethodMustHaveCategoryTrait,
            RawTraitAttributeNotAllowed,
            WindowsOnlyTestMustHavePlatformAttribute);

    public override void Initialize(AnalysisContext context)
    {
        // tests/** is generated code in .editorconfig, so the analyzer has to opt in to it
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static compilationContext =>
        {
            var compilation = compilationContext.Compilation;
            if (!TestSymbols.IsTestAssembly(compilation) ||
                compilation.GetTypeByMetadataName(TestSymbols.FactAttributeMetadataName) is not { } factAttribute)
            {
                return;
            }

            var traitAttribute = compilation.GetTypeByMetadataName("Xunit.TraitAttribute");

            compilationContext.RegisterSyntaxNodeAction(
                context => AnalyzeClass(context, traitAttribute),
                SyntaxKind.ClassDeclaration
            );
            compilationContext.RegisterSyntaxNodeAction(
                context => AnalyzeMethod(context, factAttribute, traitAttribute),
                SyntaxKind.MethodDeclaration
            );

            var platformAnalysis = new PlatformSkipAnalysis(factAttribute, new KnownPlatformTypes(compilation));
            compilationContext.RegisterOperationBlockAction(platformAnalysis.AnalyzeOperationBlock);
            compilationContext.RegisterCompilationEndAction(platformAnalysis.ReportMissingPlatformAttributes);
        });
    }

    private static void AnalyzeClass(SyntaxNodeAnalysisContext context, INamedTypeSymbol? traitAttribute)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration, context.CancellationToken);
        if (classSymbol == null)
        {
            return;
        }

        ReportRawTraits(context, classSymbol, traitAttribute);
    }

    private static void AnalyzeMethod(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol factAttribute,
        INamedTypeSymbol? traitAttribute)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration, context.CancellationToken);
        if (methodSymbol == null)
        {
            return;
        }

        ReportRawTraits(context, methodSymbol, traitAttribute);

        if (!TestSymbols.IsTestMethod(methodSymbol, factAttribute))
        {
            return;
        }

        if (methodDeclaration.Parent is not ClassDeclarationSyntax classDeclaration)
        {
            return;
        }

        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration, context.CancellationToken);
        if (classSymbol == null)
        {
            return;
        }

        // Check for Category trait on method or class
        var hasCategoryTrait = HasCategoryTrait(methodSymbol) || HasCategoryTrait(classSymbol);
        if (!hasCategoryTrait)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                TestMethodMustHaveCategoryTrait,
                methodDeclaration.Identifier.GetLocation(),
                methodSymbol.Name));
        }
    }

    private static void ReportRawTraits(
        SyntaxNodeAnalysisContext context,
        ISymbol symbol,
        INamedTypeSymbol? traitAttribute)
    {
        if (traitAttribute == null)
        {
            return;
        }

        foreach (var attribute in symbol.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, traitAttribute))
            {
                continue;
            }

            var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                ?? (symbol.Locations.Length > 0 ? symbol.Locations[0] : Location.None);
            context.ReportDiagnostic(Diagnostic.Create(
                RawTraitAttributeNotAllowed,
                location,
                symbol.Name));
        }
    }

    private static bool IsAttribute(AttributeData attribute, string shortName)
    {
        var name = attribute.AttributeClass?.Name;
        return name == shortName || name == shortName + "Attribute";
    }

    private static bool HasCategoryTrait(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (IsAttribute(attribute, "Category") ||
                IsAttribute(attribute, "TestCategory") ||
                IsAttribute(attribute, "TestTarget") ||
                IsAttribute(attribute, "Scenario"))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class KnownPlatformTypes(Compilation compilation)
    {
        public INamedTypeSymbol? Skip { get; } = compilation.GetTypeByMetadataName("Xunit.Skip");

        public INamedTypeSymbol? SkipException { get; } = compilation.GetTypeByMetadataName("Xunit.SkipException");

        public INamedTypeSymbol? OperatingSystem { get; } = compilation.GetTypeByMetadataName("System.OperatingSystem");

        public INamedTypeSymbol? RuntimeInformation { get; } =
            compilation.GetTypeByMetadataName("System.Runtime.InteropServices.RuntimeInformation");

        public INamedTypeSymbol? OSPlatform { get; } =
            compilation.GetTypeByMetadataName("System.Runtime.InteropServices.OSPlatform");

        public INamedTypeSymbol? SupportedOSPlatformAttribute { get; } =
            compilation.GetTypeByMetadataName("System.Runtime.Versioning.SupportedOSPlatformAttribute");

        public INamedTypeSymbol? PlatformAttribute { get; } =
            compilation.GetTypeByMetadataName("Kassyi.GSharp.Tests.Infrastructure.PlatformAttribute");

        public INamedTypeSymbol? TestPlatform { get; } =
            compilation.GetTypeByMetadataName("Kassyi.GSharp.Tests.Infrastructure.TestPlatform");

        public object? WindowsEnumValue { get; } = ResolveWindowsEnumValue(
            compilation.GetTypeByMetadataName("Kassyi.GSharp.Tests.Infrastructure.TestPlatform"));

        private static object? ResolveWindowsEnumValue(INamedTypeSymbol? testPlatform)
        {
            if (testPlatform is null)
            {
                return null;
            }

            foreach (var member in testPlatform.GetMembers("Windows"))
            {
                if (member is IFieldSymbol { HasConstantValue: true } field)
                {
                    return field.ConstantValue;
                }
            }

            return null;
        }
    }

    private sealed record PlatformMethodFacts(bool SkipsOnNonWindows, ImmutableArray<IMethodSymbol> Callees);

    private sealed class PlatformSkipAnalysis(INamedTypeSymbol factAttribute, KnownPlatformTypes types)
    {
        private readonly ConcurrentDictionary<IMethodSymbol, PlatformMethodFacts> _facts =
            new(SymbolEqualityComparer.Default);

        private readonly ConcurrentDictionary<IMethodSymbol, ConcurrentBag<IMethodSymbol>> _implementations =
            new(SymbolEqualityComparer.Default);

        private readonly ConcurrentBag<IMethodSymbol> _testMethods = [];

        public void AnalyzeOperationBlock(OperationBlockAnalysisContext context)
        {
            if (context.OwningSymbol is not IMethodSymbol method)
            {
                return;
            }

            var isTestMethod = TestSymbols.IsTestMethod(method, factAttribute);
            var directWindowsSkip = HasSupportedOSPlatformWindows(method);
            var callees = ImmutableArray.CreateBuilder<IMethodSymbol>();

            foreach (var block in context.OperationBlocks)
            {
                foreach (var operation in block.DescendantsAndSelf())
                {
                    switch (operation)
                    {
                        case IInvocationOperation invocation:
                            AddCallee(callees, invocation.TargetMethod);
                            if (IsSkipMethod(invocation.TargetMethod, "IfNot") &&
                                GetConditionArgument(invocation) is { } ifNotCond &&
                                RequiresWindows(ifNotCond))
                            {
                                directWindowsSkip = true;
                            }
                            else if (IsSkipMethod(invocation.TargetMethod, "If") &&
                                GetConditionArgument(invocation) is { } ifCond &&
                                SkipsOnNonWindows(ifCond))
                            {
                                directWindowsSkip = true;
                            }

                            break;
                        case IMethodReferenceOperation reference:
                            AddCallee(callees, reference.Method);
                            break;
                        case IConditionalOperation conditional:
                            if ((SkipsOnNonWindows(conditional.Condition) &&
                                    conditional.WhenTrue is not null &&
                                    ContainsUnconditionalSkip(conditional.WhenTrue)) ||
                                (RequiresWindows(conditional.Condition) &&
                                    conditional.WhenFalse is not null &&
                                    ContainsUnconditionalSkip(conditional.WhenFalse)))
                            {
                                directWindowsSkip = true;
                            }

                            break;
                    }
                }
            }

            var normalized = Normalize(method);
            _facts[normalized] = new PlatformMethodFacts(directWindowsSkip, callees.ToImmutable());
            RegisterImplementation(normalized);
            if (isTestMethod)
            {
                _testMethods.Add(method);
            }
        }

        public void ReportMissingPlatformAttributes(CompilationAnalysisContext context)
        {
            var checkedSafe = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            foreach (var testMethod in _testMethods)
            {
                if (HasWindowsPlatformAttribute(testMethod) ||
                    HasWindowsPlatformAttribute(testMethod.ContainingType))
                {
                    continue;
                }

                var requiresWindows = HasSupportedOSPlatformWindowsOnEnclosingTypes(testMethod.ContainingType);
                if (!requiresWindows)
                {
                    var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
                    requiresWindows = ReachesWindowsSkip(Normalize(testMethod), visited, checkedSafe);
                    if (!requiresWindows)
                    {
                        checkedSafe.UnionWith(visited);
                    }
                }

                if (requiresWindows && testMethod.Locations.Length > 0)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        WindowsOnlyTestMustHavePlatformAttribute,
                        testMethod.Locations[0],
                        testMethod.Name));
                }
            }
        }

        private bool ReachesWindowsSkip(
            IMethodSymbol method,
            HashSet<IMethodSymbol> visited,
            HashSet<IMethodSymbol> checkedSafe)
        {
            if (checkedSafe.Contains(method) || !visited.Add(method))
            {
                return false;
            }

            if (_implementations.TryGetValue(method, out var implementations))
            {
                foreach (var implementation in implementations)
                {
                    if (ReachesWindowsSkip(implementation, visited, checkedSafe))
                    {
                        return true;
                    }
                }
            }

            if (!_facts.TryGetValue(method, out var facts))
            {
                return false;
            }

            if (facts.SkipsOnNonWindows)
            {
                return true;
            }

            foreach (var callee in facts.Callees)
            {
                if (ReachesWindowsSkip(callee, visited, checkedSafe))
                {
                    return true;
                }
            }

            return false;
        }

        private void RegisterImplementation(IMethodSymbol method)
        {
            if (method.OverriddenMethod is { } overridden)
            {
                AddImplementation(overridden, method);
            }

            foreach (var explicitImplementation in method.ExplicitInterfaceImplementations)
            {
                AddImplementation(explicitImplementation, method);
            }

            if (method.ContainingType is not { } containingType || method.MethodKind != MethodKind.Ordinary)
            {
                return;
            }

            foreach (var @interface in containingType.AllInterfaces)
            {
                foreach (var member in @interface.GetMembers(method.Name))
                {
                    if (member is IMethodSymbol interfaceMethod &&
                        SymbolEqualityComparer.Default.Equals(
                            containingType.FindImplementationForInterfaceMember(interfaceMethod),
                            method))
                    {
                        AddImplementation(interfaceMethod, method);
                    }
                }
            }
        }

        private void AddImplementation(IMethodSymbol member, IMethodSymbol implementation) =>
            _implementations.GetOrAdd(Normalize(member), static _ => []).Add(implementation);

        private bool RequiresWindows(IOperation operation)
        {
            operation = UnwrapBool(operation);
            switch (operation)
            {
                case IInvocationOperation invocation:
                    return IsWindowsPlatformCheck(invocation);
                case IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } unary:
                    return SkipsOnNonWindows(unary.Operand);
                case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.And } andOp:
                    return RequiresWindows(andOp.LeftOperand) || RequiresWindows(andOp.RightOperand);
                case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalOr or BinaryOperatorKind.Or } orOp:
                    return RequiresWindows(orOp.LeftOperand) && RequiresWindows(orOp.RightOperand);
                case IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals } eq:
                    return TryMatchBoolComparison(eq, out var innerEq, out var eqExpected) &&
                        (eqExpected ? RequiresWindows(innerEq) : SkipsOnNonWindows(innerEq));
                case IBinaryOperation { OperatorKind: BinaryOperatorKind.NotEquals } neq:
                    return TryMatchBoolComparison(neq, out var innerNeq, out var neqExpected) &&
                        (neqExpected ? SkipsOnNonWindows(innerNeq) : RequiresWindows(innerNeq));
                case IIsPatternOperation { Pattern: IConstantPatternOperation { Value: { } patVal } } isPat
                    when TryGetBoolConstant(patVal, out var patBool):
                    return patBool ? RequiresWindows(isPat.Value) : SkipsOnNonWindows(isPat.Value);
                default:
                    return false;
            }
        }

        private bool SkipsOnNonWindows(IOperation operation)
        {
            operation = UnwrapBool(operation);
            switch (operation)
            {
                case IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } unary:
                    return RequiresWindows(unary.Operand);
                case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalOr or BinaryOperatorKind.Or } orOp:
                    return SkipsOnNonWindows(orOp.LeftOperand) || SkipsOnNonWindows(orOp.RightOperand);
                case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.And } andOp:
                    return SkipsOnNonWindows(andOp.LeftOperand) && SkipsOnNonWindows(andOp.RightOperand);
                case IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals } eq:
                    return TryMatchBoolComparison(eq, out var innerEq, out var eqExpected) &&
                        (eqExpected ? SkipsOnNonWindows(innerEq) : RequiresWindows(innerEq));
                case IBinaryOperation { OperatorKind: BinaryOperatorKind.NotEquals } neq:
                    return TryMatchBoolComparison(neq, out var innerNeq, out var neqExpected) &&
                        (neqExpected ? RequiresWindows(innerNeq) : SkipsOnNonWindows(innerNeq));
                case IIsPatternOperation { Pattern: IConstantPatternOperation { Value: { } patVal } } isPat
                    when TryGetBoolConstant(patVal, out var patBool):
                    return patBool ? SkipsOnNonWindows(isPat.Value) : RequiresWindows(isPat.Value);
                default:
                    return false;
            }
        }

        private static IOperation UnwrapBool(IOperation operation)
        {
            while (true)
            {
                switch (operation)
                {
                    case IParenthesizedOperation paren:
                        operation = paren.Operand;
                        break;
                    case IConversionOperation conv when conv.IsImplicit &&
                        conv.Type?.SpecialType == SpecialType.System_Boolean &&
                        conv.Operand.Type?.SpecialType == SpecialType.System_Boolean:
                        operation = conv.Operand;
                        break;
                    default:
                        return operation;
                }
            }
        }

        private static bool TryGetBoolConstant(IOperation operation, out bool value)
        {
            if (UnwrapBool(operation).ConstantValue is { HasValue: true, Value: bool b })
            {
                value = b;
                return true;
            }

            value = false;
            return false;
        }

        private static bool TryMatchBoolComparison(IBinaryOperation binary, out IOperation inner, out bool comparedTo)
        {
            if (TryGetBoolConstant(binary.RightOperand, out comparedTo))
            {
                inner = binary.LeftOperand;
                return true;
            }

            if (TryGetBoolConstant(binary.LeftOperand, out comparedTo))
            {
                inner = binary.RightOperand;
                return true;
            }

            inner = binary;
            comparedTo = false;
            return false;
        }

        private bool IsWindowsPlatformCheck(IInvocationOperation invocation)
        {
            var method = invocation.TargetMethod;
            if (types.OperatingSystem is not null &&
                SymbolEqualityComparer.Default.Equals(method.ContainingType, types.OperatingSystem) &&
                (method.Name == "IsWindows" || method.Name == "IsWindowsVersionAtLeast"))
            {
                return true;
            }

            if (types.RuntimeInformation is not null &&
                types.OSPlatform is not null &&
                SymbolEqualityComparer.Default.Equals(method.ContainingType, types.RuntimeInformation) &&
                method.Name == "IsOSPlatform" &&
                invocation.Arguments.Length == 1 &&
                UnwrapBool(invocation.Arguments[0].Value) is IPropertyReferenceOperation propRef &&
                SymbolEqualityComparer.Default.Equals(propRef.Property.ContainingType, types.OSPlatform) &&
                propRef.Property.Name == "Windows")
            {
                return true;
            }

            return false;
        }

        private bool IsSkipMethod(IMethodSymbol method, string name) =>
            types.Skip is not null &&
            method.Name == name &&
            SymbolEqualityComparer.Default.Equals(method.ContainingType, types.Skip);

        private static IOperation? GetConditionArgument(IInvocationOperation invocation)
        {
            foreach (var arg in invocation.Arguments)
            {
                if (arg.Parameter?.Name == "condition")
                {
                    return arg.Value;
                }
            }

            return invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value : null;
        }

        private bool ContainsUnconditionalSkip(IOperation branch)
        {
            foreach (var op in DescendantsOutsideFunctionsAndConditionals(branch))
            {
                if (op is IThrowOperation thrown && IsSkipException(thrown))
                {
                    return true;
                }

                if (op is IInvocationOperation invocation &&
                    GetConditionArgument(invocation) is { } cond &&
                    TryGetBoolConstant(cond, out var constant))
                {
                    if ((IsSkipMethod(invocation.TargetMethod, "If") && constant) ||
                        (IsSkipMethod(invocation.TargetMethod, "IfNot") && !constant))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<IOperation> DescendantsOutsideFunctionsAndConditionals(IOperation root)
        {
            yield return root;
            foreach (var child in root.ChildOperations)
            {
                if (child is IAnonymousFunctionOperation or ILocalFunctionOperation or
                    IConditionalOperation or ICatchClauseOperation or ISwitchOperation)
                {
                    continue;
                }

                foreach (var descendant in DescendantsOutsideFunctionsAndConditionals(child))
                {
                    yield return descendant;
                }
            }
        }

        private bool IsSkipException(IThrowOperation thrown)
        {
            if (types.SkipException is null)
            {
                return false;
            }

            for (var type = (thrown.Exception as IConversionOperation)?.Operand.Type ?? thrown.Exception?.Type;
                type is not null;
                type = type.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(type, types.SkipException))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasSupportedOSPlatformWindows(ISymbol symbol)
        {
            if (types.SupportedOSPlatformAttribute is null)
            {
                return false;
            }

            foreach (var attribute in symbol.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, types.SupportedOSPlatformAttribute))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length == 1 &&
                    attribute.ConstructorArguments[0].Value is string platformName &&
                    platformName.StartsWith("windows", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasSupportedOSPlatformWindowsOnEnclosingTypes(INamedTypeSymbol? type)
        {
            for (; type is not null; type = type.ContainingType)
            {
                if (HasSupportedOSPlatformWindows(type))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasWindowsPlatformAttribute(ISymbol? symbol)
        {
            if (symbol is null || types.PlatformAttribute is null || types.TestPlatform is null || types.WindowsEnumValue is null)
            {
                return false;
            }

            foreach (var attribute in symbol.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, types.PlatformAttribute))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length == 1 &&
                    attribute.ConstructorArguments[0] is { Kind: TypedConstantKind.Enum, Type: { } argType, Value: { } val } &&
                    SymbolEqualityComparer.Default.Equals(argType, types.TestPlatform) &&
                    Equals(val, types.WindowsEnumValue))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddCallee(ImmutableArray<IMethodSymbol>.Builder callees, IMethodSymbol method)
        {
            if (method.MethodKind is MethodKind.LocalFunction or MethodKind.AnonymousFunction ||
                method.DeclaringSyntaxReferences.Length == 0)
            {
                return;
            }

            callees.Add(Normalize(method));
        }

        private static IMethodSymbol Normalize(IMethodSymbol method)
        {
            var definition = (method.ReducedFrom ?? method).OriginalDefinition;
            return definition.PartialImplementationPart ?? definition;
        }
    }
}
