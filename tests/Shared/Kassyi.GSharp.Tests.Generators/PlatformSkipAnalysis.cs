#nullable enable
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Tests.Generators;

/// <summary>
/// HNT0011: finds the tests that lose their meaning on a Linux worker (they skip, or return before they check
/// anything) and reports those that do not carry <c>[Platform(TestPlatform.Windows)]</c> on the method or on the
/// concrete class that runs them.
/// </summary>
/// <remarks>
/// Each method body is reduced at its operation block to facts: whether it skips on Linux, the source methods it
/// calls, the skip sites whose condition still depends on a bool helper, and the value a one-line bool helper returns
/// on Linux. The compilation end action follows the calls from each test and from the constructors of its class.
/// Conditions are evaluated in three-valued logic for a Linux host; a form the analysis cannot decide is not reported.
/// </remarks>
internal sealed class PlatformSkipAnalysis(INamedTypeSymbol factAttribute, KnownPlatformTypes types)
{
    private const int MaxLocalDepth = 4;

    private readonly ConcurrentDictionary<IMethodSymbol, PlatformMethodFacts> _facts =
        new(SymbolEqualityComparer.Default);

    private readonly ConcurrentDictionary<IMethodSymbol, ConcurrentBag<IMethodSymbol>> _implementations =
        new(SymbolEqualityComparer.Default);

    private readonly ConcurrentBag<IMethodSymbol> _testMethods = [];

    private readonly ConcurrentBag<INamedTypeSymbol> _derivedClasses = [];

    public void CollectDerivedClass(SymbolAnalysisContext context)
    {
        // Only a concrete class whose base is declared in source can inherit tests from a source base
        if (context.Symbol is INamedTypeSymbol
            {
                TypeKind: TypeKind.Class,
                IsAbstract: false,
                BaseType: { SpecialType: not SpecialType.System_Object } baseType,
            } type &&
            baseType.DeclaringSyntaxReferences.Length > 0)
        {
            _derivedClasses.Add(type);
        }
    }

    public void AnalyzeOperationBlock(OperationBlockAnalysisContext context)
    {
        var method = context.OwningSymbol switch
        {
            IMethodSymbol owner => owner,
            IPropertySymbol { GetMethod: { } getter } => getter,
            _ => null,
        };
        if (method is null || context.OperationBlocks.Length == 0)
        {
            return;
        }

        var isTestMethod = TestSymbols.IsTestMethod(method, factAttribute);

        // Source generator output (*.g.cs) holds no platform skip; only a generated test is followed
        if (!isTestMethod && IsGeneratorOutput(context.OperationBlocks[0]))
        {
            return;
        }

        // A bare return ends the test only in the test body or a constructor; in a helper it only ends the helper
        var endsWithReturn = isTestMethod ||
            method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor;
        var builder = new FactsBuilder(context.OperationBlocks, HasSupportedOSPlatformWindows(method));
        foreach (var block in context.OperationBlocks)
        {
            foreach (var operation in block.DescendantsAndSelf())
            {
                switch (operation)
                {
                    case IInvocationOperation invocation:
                        AddCallee(builder.Callees, invocation.TargetMethod);
                        if ((IsSkipMethod(invocation.TargetMethod, "If") ||
                                IsSkipMethod(invocation.TargetMethod, "IfNot")) &&
                            GetConditionArgument(invocation) is { } condition)
                        {
                            builder.AddSite(
                                BuildCondition(condition, builder, 0),
                                skipsWhen: invocation.TargetMethod.Name == "If");
                        }

                        break;
                    case IMethodReferenceOperation reference:
                        AddCallee(builder.Callees, reference.Method);
                        break;
                    case IConditionalOperation conditional:
                        AnalyzeConditional(conditional, builder, endsWithReturn);
                        break;
                }
            }
        }

        var returnsOnLinux = method.ReturnType.SpecialType == SpecialType.System_Boolean
            ? ReturnValueOnLinux(context.OperationBlocks, builder)
            : LinuxValue.Unknown;
        var normalized = Normalize(method);
        if (builder.IsEmpty && returnsOnLinux == LinuxValue.Unknown)
        {
            // Nothing in the body can reach a skip: keep no facts, so the end action holds only what tests reach
            if (isTestMethod)
            {
                _testMethods.Add(method);
            }

            return;
        }

        _facts[normalized] = builder.ToFacts(returnsOnLinux);
        RegisterImplementation(normalized);
        if (isTestMethod)
        {
            _testMethods.Add(method);
        }
    }

    public void ReportMissingPlatformAttributes(CompilationAnalysisContext context)
    {
        var checkedSafe = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var classSkips = new Dictionary<INamedTypeSymbol, bool>(SymbolEqualityComparer.Default);
        foreach (var testMethod in _testMethods)
        {
            if (HasWindowsPlatformAttribute(testMethod) || testMethod.Locations.Length == 0)
            {
                continue;
            }

            var declaringType = testMethod.ContainingType;
            var bodySkips = HasSupportedOSPlatformWindowsOnEnclosingTypes(declaringType) ||
                MethodReachesSkip(testMethod, checkedSafe);

            // xUnit gives an inherited test the traits of the concrete class that runs it, never those of the base
            if (!declaringType.IsAbstract)
            {
                if (!HasWindowsPlatformAttribute(declaringType) &&
                    (bodySkips || ClassSkips(declaringType, classSkips, checkedSafe)))
                {
                    Report(context, testMethod.Locations[0], testMethod);
                }

                continue;
            }

            foreach (var derived in _derivedClasses)
            {
                if (derived.Locations.Length > 0 &&
                    InheritsFrom(derived, declaringType) &&
                    !HasWindowsPlatformAttribute(derived) &&
                    (bodySkips || ClassSkips(derived, classSkips, checkedSafe)))
                {
                    Report(context, derived.Locations[0], testMethod);
                }
            }
        }
    }

    private static void Report(CompilationAnalysisContext context, Location location, IMethodSymbol testMethod) =>
        context.ReportDiagnostic(Diagnostic.Create(
            TestArchitectureAnalyzer.WindowsOnlyTestMustHavePlatformAttribute,
            location,
            testMethod.Name));

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseDefinition)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseDefinition.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
    }

    // A skip in a constructor of the class or of a source base class skips every test the class runs
    private bool ClassSkips(
        INamedTypeSymbol type,
        Dictionary<INamedTypeSymbol, bool> cache,
        HashSet<IMethodSymbol> checkedSafe)
    {
        if (cache.TryGetValue(type, out var skips))
        {
            return skips;
        }

        skips = HasSupportedOSPlatformWindowsOnEnclosingTypes(type);
        for (var current = type; !skips && current is { DeclaringSyntaxReferences.Length: > 0 }; current = current.BaseType)
        {
            skips = ConstructorsReachSkip(current.InstanceConstructors, checkedSafe) ||
                ConstructorsReachSkip(current.StaticConstructors, checkedSafe);
        }

        cache[type] = skips;
        return skips;
    }

    private bool ConstructorsReachSkip(ImmutableArray<IMethodSymbol> constructors, HashSet<IMethodSymbol> checkedSafe)
    {
        foreach (var constructor in constructors)
        {
            if (MethodReachesSkip(constructor, checkedSafe))
            {
                return true;
            }
        }

        return false;
    }

    private bool MethodReachesSkip(IMethodSymbol method, HashSet<IMethodSymbol> checkedSafe)
    {
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        if (ReachesWindowsSkip(Normalize(method), visited, checkedSafe))
        {
            return true;
        }

        checkedSafe.UnionWith(visited);
        return false;
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

        if (facts.SkipsOnLinux)
        {
            return true;
        }

        foreach (var site in facts.PendingSites)
        {
            if (Evaluate(site.Condition) == (site.SkipsWhen ? LinuxValue.True : LinuxValue.False))
            {
                return true;
            }
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

    // A helper is followed one level: its own value was decided without other helpers
    private LinuxValue Evaluate(LinuxCondition condition) =>
        condition.Kind switch
        {
            LinuxConditionKind.Constant => condition.Value,
            LinuxConditionKind.Helper when condition.Helper is { } helper && _facts.TryGetValue(helper, out var facts) =>
                facts.ReturnsOnLinux,
            LinuxConditionKind.Not when condition.Left is { } operand => LinuxCondition.Negate(Evaluate(operand)),
            LinuxConditionKind.And when condition is { Left: { } left, Right: { } right } =>
                LinuxCondition.And(Evaluate(left), Evaluate(right)),
            LinuxConditionKind.Or when condition is { Left: { } left, Right: { } right } =>
                LinuxCondition.Or(Evaluate(left), Evaluate(right)),
            _ => LinuxValue.Unknown,
        };

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

    // if (L) return; / if (W) { ... } else throw new SkipException(...); / W ? x : throw new SkipException(...)
    private void AnalyzeConditional(IConditionalOperation conditional, FactsBuilder builder, bool endsWithReturn)
    {
        var whenTrue = conditional.WhenTrue is { } trueBranch && BranchSkips(conditional, trueBranch, endsWithReturn);
        var whenFalse = conditional.WhenFalse is { } falseBranch &&
            BranchSkips(conditional, falseBranch, endsWithReturn);
        if (!whenTrue && !whenFalse)
        {
            return;
        }

        var condition = BuildCondition(conditional.Condition, builder, 0);
        if (whenTrue)
        {
            builder.AddSite(condition, skipsWhen: true);
        }

        if (whenFalse)
        {
            builder.AddSite(condition, skipsWhen: false);
        }
    }

    private bool BranchSkips(IConditionalOperation conditional, IOperation branch, bool endsWithReturn) =>
        ContainsUnconditionalSkip(branch) ||
        (endsWithReturn && IsBareReturn(branch) && !IsInsideFunction(conditional));

    // return; alone, so the method ends before it checks anything
    private static bool IsBareReturn(IOperation branch)
    {
        if (branch is IBlockOperation { Operations.Length: 1 } block)
        {
            branch = block.Operations[0];
        }

        return branch is IReturnOperation { Kind: OperationKind.Return, ReturnedValue: null };
    }

    private static bool IsInsideFunction(IOperation operation)
    {
        for (var parent = operation.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                return true;
            }
        }

        return false;
    }

    // static bool IsWindowsHost => OperatingSystem.IsWindows(); (an expression body or a single return)
    private LinuxValue ReturnValueOnLinux(ImmutableArray<IOperation> blocks, FactsBuilder builder)
    {
        if (blocks.Length != 1)
        {
            return LinuxValue.Unknown;
        }

        var body = blocks[0];
        if (body is IBlockOperation { Operations.Length: 1 } block)
        {
            body = block.Operations[0];
        }

        if (body is not IReturnOperation { ReturnedValue: { } returned })
        {
            return LinuxValue.Unknown;
        }

        var condition = BuildCondition(returned, builder, 0);
        return condition.Kind == LinuxConditionKind.Constant ? condition.Value : LinuxValue.Unknown;
    }

    private LinuxCondition BuildCondition(IOperation operation, FactsBuilder builder, int depth)
    {
        operation = UnwrapBool(operation);
        switch (operation)
        {
            case IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } unary:
                return LinuxCondition.Not(BuildCondition(unary.Operand, builder, depth));
            case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.And } andOperation:
                return LinuxCondition.And(
                    BuildCondition(andOperation.LeftOperand, builder, depth),
                    BuildCondition(andOperation.RightOperand, builder, depth));
            case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalOr or BinaryOperatorKind.Or } orOperation:
                return LinuxCondition.Or(
                    BuildCondition(orOperation.LeftOperand, builder, depth),
                    BuildCondition(orOperation.RightOperand, builder, depth));
            case IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals } comparison:
                return BuildComparison(comparison, builder, depth);
            case IIsPatternOperation { Pattern: IConstantPatternOperation { Value: { } patternValue } } isPattern
                when TryGetBoolConstant(patternValue, out var patternBool):
                var tested = BuildCondition(isPattern.Value, builder, depth);
                return patternBool ? tested : LinuxCondition.Not(tested);
            case IInvocationOperation invocation:
                return OsCheckValue(invocation) is { } value
                    ? LinuxCondition.Of(value)
                    : HelperCondition(invocation.TargetMethod, invocation.Arguments.Length);
            case IPropertyReferenceOperation { Property.GetMethod: { } getter }:
                return HelperCondition(getter, 0);
            case ILocalReferenceOperation local when depth < MaxLocalDepth &&
                builder.Locals.TryGetSingleValue(local.Local) is { } initializer:
                return BuildCondition(initializer, builder, depth + 1);
            default:
                // A bool literal does not depend on the platform: Skip.If(true) is not a Windows-only skip
                return LinuxCondition.Unknown;
        }
    }

    private LinuxCondition BuildComparison(IBinaryOperation comparison, FactsBuilder builder, int depth)
    {
        var equals = comparison.OperatorKind == BinaryOperatorKind.Equals;
        if (TryMatchBoolComparison(comparison, out var inner, out var comparedTo))
        {
            var condition = BuildCondition(inner, builder, depth);
            return comparedTo == equals ? condition : LinuxCondition.Not(condition);
        }

        // Environment.OSVersion.Platform == PlatformID.Win32NT
        if ((PlatformIdValue(comparison.LeftOperand, comparison.RightOperand) ??
                PlatformIdValue(comparison.RightOperand, comparison.LeftOperand)) is { } platformIs)
        {
            return LinuxCondition.Of(equals ? platformIs : LinuxCondition.Negate(platformIs));
        }

        return LinuxCondition.Unknown;
    }

    private LinuxValue? PlatformIdValue(IOperation platform, IOperation platformId)
    {
        if (types.OperatingSystem is null || types.PlatformId is null || types.UnixPlatformIdValue is null ||
            UnwrapConversions(platform) is not IPropertyReferenceOperation { Property.Name: "Platform" } property ||
            !SymbolEqualityComparer.Default.Equals(property.Property.ContainingType, types.OperatingSystem))
        {
            return null;
        }

        var id = UnwrapConversions(platformId);
        if (!SymbolEqualityComparer.Default.Equals(id.Type, types.PlatformId) || !id.ConstantValue.HasValue)
        {
            return null;
        }

        return Equals(id.ConstantValue.Value, types.UnixPlatformIdValue) ? LinuxValue.True : LinuxValue.False;
    }

    // Parameterless bool members declared in source; their value is resolved at the end, one level deep
    private static LinuxCondition HelperCondition(IMethodSymbol method, int argumentCount) =>
        argumentCount == 0 &&
        method.ReturnType.SpecialType == SpecialType.System_Boolean &&
        method.MethodKind is MethodKind.Ordinary or MethodKind.PropertyGet &&
        method.DeclaringSyntaxReferences.Length > 0
            ? LinuxCondition.ForHelper(Normalize(method))
            : LinuxCondition.Unknown;

    // The value of an OS check on a Linux host
    private LinuxValue? OsCheckValue(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (types.OperatingSystem is not null &&
            SymbolEqualityComparer.Default.Equals(method.ContainingType, types.OperatingSystem) &&
            method.IsStatic)
        {
            switch (method.Name)
            {
                case "IsLinux":
                    return LinuxValue.True;
                case "IsOSPlatform":
                    return ConstantStringArgument(invocation) is { } platform
                        ? IsLinuxName(platform) ? LinuxValue.True : LinuxValue.False
                        : null;
                case "IsOSPlatformVersionAtLeast":
                    return ConstantStringArgument(invocation) is { } versioned && !IsLinuxName(versioned)
                        ? LinuxValue.False
                        : null;
                default:
                    // IsWindows, IsWindowsVersionAtLeast, IsMacOS, IsFreeBSD, ...: other systems
                    return method.Name.StartsWith("Is", StringComparison.Ordinal) ? LinuxValue.False : null;
            }
        }

        if (types.RuntimeInformation is not null &&
            types.OSPlatform is not null &&
            SymbolEqualityComparer.Default.Equals(method.ContainingType, types.RuntimeInformation) &&
            method.Name == "IsOSPlatform" &&
            invocation.Arguments.Length == 1 &&
            UnwrapConversions(invocation.Arguments[0].Value) is IPropertyReferenceOperation propertyReference &&
            SymbolEqualityComparer.Default.Equals(propertyReference.Property.ContainingType, types.OSPlatform))
        {
            return propertyReference.Property.Name == "Linux" ? LinuxValue.True : LinuxValue.False;
        }

        return null;
    }

    private static bool IsLinuxName(string platform) =>
        string.Equals(platform, "linux", StringComparison.OrdinalIgnoreCase);

    private static string? ConstantStringArgument(IInvocationOperation invocation) =>
        invocation.Arguments.Length > 0 &&
        UnwrapConversions(invocation.Arguments[0].Value).ConstantValue is { HasValue: true, Value: string value }
            ? value
            : null;

    private static IOperation UnwrapConversions(IOperation operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
        {
            operation = conversion.Operand;
        }

        return operation is IParenthesizedOperation paren ? UnwrapConversions(paren.Operand) : operation;
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

    private static bool IsGeneratorOutput(IOperation block) =>
        block.Syntax.SyntaxTree.FilePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase);

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

    private sealed record PlatformMethodFacts(
        bool SkipsOnLinux,
        ImmutableArray<IMethodSymbol> Callees,
        ImmutableArray<PendingSite> PendingSites,
        LinuxValue ReturnsOnLinux);

    // A skip site whose condition depends on a helper: it skips when the condition has this value on Linux
    private readonly record struct PendingSite(LinuxCondition Condition, bool SkipsWhen);

    private sealed class FactsBuilder(ImmutableArray<IOperation> blocks, bool skipsOnLinux)
    {
        private ImmutableArray<PendingSite>.Builder? _pending;
        private LocalValues? _locals;

        public bool SkipsOnLinux { get; private set; } = skipsOnLinux;

        public ImmutableArray<IMethodSymbol>.Builder Callees { get; } = ImmutableArray.CreateBuilder<IMethodSymbol>();

        public LocalValues Locals => _locals ??= new LocalValues(blocks);

        public bool IsEmpty => !SkipsOnLinux && Callees.Count == 0 && _pending is null;

        public void AddSite(LinuxCondition condition, bool skipsWhen)
        {
            if (condition.Kind == LinuxConditionKind.Constant)
            {
                SkipsOnLinux |= condition.Value == (skipsWhen ? LinuxValue.True : LinuxValue.False);
                return;
            }

            (_pending ??= ImmutableArray.CreateBuilder<PendingSite>()).Add(new PendingSite(condition, skipsWhen));
        }

        public PlatformMethodFacts ToFacts(LinuxValue returnsOnLinux) =>
            new(
                SkipsOnLinux,
                Callees.ToImmutable(),
                _pending?.ToImmutable() ?? ImmutableArray<PendingSite>.Empty,
                returnsOnLinux);
    }

    // The initial values of the locals of one method body that are never written after their declaration
    private sealed class LocalValues
    {
        private readonly Dictionary<ILocalSymbol, IOperation> _initializers = new(SymbolEqualityComparer.Default);
        private readonly HashSet<ILocalSymbol> _written = new(SymbolEqualityComparer.Default);

        public LocalValues(ImmutableArray<IOperation> blocks)
        {
            foreach (var block in blocks)
            {
                foreach (var operation in block.DescendantsAndSelf())
                {
                    switch (operation)
                    {
                        case IVariableDeclaratorOperation { Initializer.Value: { } value } declarator:
                            if (declarator.Symbol.RefKind == RefKind.None)
                            {
                                _initializers[declarator.Symbol] = value;
                            }
                            else
                            {
                                _written.Add(declarator.Symbol);
                            }

                            break;
                        case ILocalReferenceOperation reference when IsWrite(reference):
                            _written.Add(reference.Local);
                            break;
                    }
                }
            }
        }

        public IOperation? TryGetSingleValue(ILocalSymbol local) =>
            !_written.Contains(local) && _initializers.TryGetValue(local, out var value) ? value : null;

        // Any write, alias or address taken after the declaration makes the local unknown
        private static bool IsWrite(ILocalReferenceOperation reference) =>
            reference.Parent switch
            {
                IAssignmentOperation assignment => ReferenceEquals(assignment.Target, reference),
                IIncrementOrDecrementOperation or IAddressOfOperation or ITupleOperation or IDeclarationExpressionOperation => true,
                IArgumentOperation argument => argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out,
                IVariableInitializerOperation { Parent: IVariableDeclaratorOperation { Symbol.RefKind: not RefKind.None } } => true,
                _ => false,
            };
    }
}

internal enum LinuxValue : byte
{
    Unknown,
    False,
    True,
}

internal enum LinuxConditionKind : byte
{
    Constant,
    Helper,
    Not,
    And,
    Or,
}

/// <summary>
/// A bool condition evaluated for a Linux host, folded while it is built; only a condition that still depends on a
/// helper member keeps a tree.
/// </summary>
internal sealed class LinuxCondition
{
    public static readonly LinuxCondition Unknown = new(LinuxConditionKind.Constant, LinuxValue.Unknown, null, null, null);

    private static readonly LinuxCondition s_false = new(LinuxConditionKind.Constant, LinuxValue.False, null, null, null);

    private static readonly LinuxCondition s_true = new(LinuxConditionKind.Constant, LinuxValue.True, null, null, null);

    private LinuxCondition(
        LinuxConditionKind kind,
        LinuxValue value,
        LinuxCondition? left,
        LinuxCondition? right,
        IMethodSymbol? helper)
    {
        Kind = kind;
        Value = value;
        Left = left;
        Right = right;
        Helper = helper;
    }

    public LinuxConditionKind Kind { get; }

    public LinuxValue Value { get; }

    public LinuxCondition? Left { get; }

    public LinuxCondition? Right { get; }

    public IMethodSymbol? Helper { get; }

    public static LinuxCondition Of(LinuxValue value) =>
        value switch
        {
            LinuxValue.True => s_true,
            LinuxValue.False => s_false,
            _ => Unknown,
        };

    public static LinuxCondition ForHelper(IMethodSymbol helper) =>
        new(LinuxConditionKind.Helper, LinuxValue.Unknown, null, null, helper);

    public static LinuxCondition Not(LinuxCondition operand) =>
        operand.Kind == LinuxConditionKind.Constant
            ? Of(Negate(operand.Value))
            : new(LinuxConditionKind.Not, LinuxValue.Unknown, operand, null, null);

    public static LinuxCondition And(LinuxCondition left, LinuxCondition right) =>
        Combine(LinuxConditionKind.And, left, right, LinuxValue.False);

    public static LinuxCondition Or(LinuxCondition left, LinuxCondition right) =>
        Combine(LinuxConditionKind.Or, left, right, LinuxValue.True);

    public static LinuxValue Negate(LinuxValue value) =>
        value switch
        {
            LinuxValue.True => LinuxValue.False,
            LinuxValue.False => LinuxValue.True,
            _ => LinuxValue.Unknown,
        };

    public static LinuxValue And(LinuxValue left, LinuxValue right) =>
        left == LinuxValue.False || right == LinuxValue.False ? LinuxValue.False
        : left == LinuxValue.True && right == LinuxValue.True ? LinuxValue.True
        : LinuxValue.Unknown;

    public static LinuxValue Or(LinuxValue left, LinuxValue right) =>
        left == LinuxValue.True || right == LinuxValue.True ? LinuxValue.True
        : left == LinuxValue.False && right == LinuxValue.False ? LinuxValue.False
        : LinuxValue.Unknown;

    // dominant: the value that decides the operator alone (false for &&, true for ||)
    private static LinuxCondition Combine(
        LinuxConditionKind kind,
        LinuxCondition left,
        LinuxCondition right,
        LinuxValue dominant)
    {
        var leftConstant = left.Kind == LinuxConditionKind.Constant;
        var rightConstant = right.Kind == LinuxConditionKind.Constant;
        if ((leftConstant && left.Value == dominant) || (rightConstant && right.Value == dominant))
        {
            return Of(dominant);
        }

        if (leftConstant && rightConstant)
        {
            return Of(kind == LinuxConditionKind.And ? And(left.Value, right.Value) : Or(left.Value, right.Value));
        }

        // The neutral value drops out: true && x is x, false || x is x
        var neutral = Negate(dominant);
        if (leftConstant && left.Value == neutral)
        {
            return right;
        }

        if (rightConstant && right.Value == neutral)
        {
            return left;
        }

        return new(kind, LinuxValue.Unknown, left, right, null);
    }
}
