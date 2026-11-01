#nullable enable
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Kassyi.GSharp.Tests.Generators;

/// <summary>
/// Reports tests that check nothing or can hide a failure (issue #597, the machine-checkable items of
/// <c>.agents/skills/property-test-design/SKILL.md</c> §8). Repository-internal, like
/// <see cref="TestArchitectureAnalyzer"/>; it runs only in test assemblies.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description>HNT0004: a test method (<c>[Fact]</c>, <c>[Theory]</c>, <c>[SkippableFact]</c> ...) that
///   reaches no call to <c>Xunit.Assert</c>, <c>VerifyXunit.Verifier</c> or a <c>FluentAssertions</c> member. Calls to
///   methods declared in the same compilation are followed (helpers, base class members, method groups), and from an
///   abstract, virtual or interface member on to its overrides and implementations. Lambdas and local functions are
///   part of the method that declares them.</description></item>
///   <item><description>HNT0005: <c>System.Diagnostics.Debug.Assert</c> in a test method.</description></item>
///   <item><description>HNT0006: an unseeded random source anywhere in a test assembly: <c>new System.Random()</c>,
///   <c>Random.Shared</c>, <c>System.Security.Cryptography.RandomNumberGenerator</c>.</description></item>
///   <item><description>HNT0007: <c>catch</c> or <c>catch (Exception)</c> without a <c>when</c> filter in a test
///   method whose handler drops the exception. A handler keeps it when it assigns the exception variable or passes it
///   (also inside a larger expression) to a method other than a Console / test output / Debug / Trace writer, calls an
///   assertion, or ends in a <c>throw</c> with no <c>return</c>, <c>break</c> or <c>continue</c> before it (a thrown
///   <c>Xunit.SkipException</c> turns the failure into a skip, so it does not count).</description></item>
/// </list>
/// All matches are by symbol; a user type that only shares a name (<c>Assert</c>, <c>Debug</c>, <c>Random</c>) is
/// not reported or counted.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class TestStrengthAnalyzer : DiagnosticAnalyzer
{
    private const string FluentAssertionsNamespace = "FluentAssertions";

#pragma warning disable RS2008 // Repository-internal analyzer; not shipped, so release tracking does not apply.
    public static readonly DiagnosticDescriptor TestMethodWithoutAssertion = new(
        id: "HNT0004",
        title: "Test method checks nothing",
        messageFormat: "Test method '{0}' never reaches an assertion (Xunit.Assert, VerifyXunit.Verifier or FluentAssertions), not even through the methods it calls. Check its results.",
        category: "GSharpTestArchitecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor DebugAssertInTest = new(
        id: "HNT0005",
        title: "Debug.Assert in a test method",
        messageFormat: "Debug.Assert is removed from Release builds, so this check does not run there. Use an xUnit Assert member.",
        category: "GSharpTestArchitecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnseededRandom = new(
        id: "HNT0006",
        title: "Unseeded random source in a test assembly",
        messageFormat: "{0} gives different inputs on every run, so a failure cannot be reproduced. Use new Random(seed) with a fixed seed.",
        category: "GSharpTestArchitecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BroadCatchInTest = new(
        id: "HNT0007",
        title: "Catch of every exception in a test method",
        messageFormat: "'{0}' catches every exception and drops it, so a failure of the code under test can pass unnoticed. Catch a narrow type, add a 'when' filter, record the exception and assert on it, or use Assert.Throws.",
        category: "GSharpTestArchitecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
#pragma warning restore RS2008

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(TestMethodWithoutAssertion, DebugAssertInTest, UnseededRandom, BroadCatchInTest);

    public override void Initialize(AnalysisContext context)
    {
        // tests/** is generated code in .editorconfig, so the analyzer has to opt in to it
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics
        );
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static compilationContext =>
        {
            var compilation = compilationContext.Compilation;
            if (!TestSymbols.IsTestAssembly(compilation))
            {
                return;
            }

            var randomSources = new RandomSources(
                compilation.GetTypeByMetadataName("System.Random"),
                compilation.GetTypeByMetadataName("System.Security.Cryptography.RandomNumberGenerator")
            );
            compilationContext.RegisterOperationAction(
                randomSources.AnalyzeObjectCreation,
                OperationKind.ObjectCreation
            );
            compilationContext.RegisterOperationAction(
                randomSources.AnalyzePropertyReference,
                OperationKind.PropertyReference
            );
            compilationContext.RegisterOperationAction(randomSources.AnalyzeInvocation, OperationKind.Invocation);

            if (compilation.GetTypeByMetadataName(TestSymbols.FactAttributeMetadataName) is not { } factAttribute)
            {
                return;
            }

            var analysis = new Analysis(factAttribute, new KnownTypes(compilation));
            compilationContext.RegisterOperationBlockAction(analysis.AnalyzeOperationBlock);
            compilationContext.RegisterCompilationEndAction(analysis.ReportTestsWithoutAssertion);
        });
    }

    /// <summary>
    /// HNT0006: the unseeded random sources.
    /// </summary>
    private sealed class RandomSources(INamedTypeSymbol? randomType, INamedTypeSymbol? randomNumberGeneratorType)
    {
        public void AnalyzeObjectCreation(OperationAnalysisContext context)
        {
            var creation = (IObjectCreationOperation)context.Operation;
            if (creation.Constructor is { Parameters.Length: 0 } constructor &&
                IsType(constructor.ContainingType, randomType))
            {
                Report(context, "new Random()");
            }
        }

        public void AnalyzePropertyReference(OperationAnalysisContext context)
        {
            var reference = (IPropertyReferenceOperation)context.Operation;
            if (reference.Property is { IsStatic: true, Name: "Shared" } property &&
                IsType(property.ContainingType, randomType))
            {
                Report(context, "Random.Shared");
            }
        }

        public void AnalyzeInvocation(OperationAnalysisContext context)
        {
            var method = ((IInvocationOperation)context.Operation).TargetMethod;
            if (IsType(method.ContainingType, randomNumberGeneratorType))
            {
                Report(context, "RandomNumberGenerator." + method.Name);
            }
        }

        private static bool IsType(INamedTypeSymbol? type, INamedTypeSymbol? expected) =>
            expected is not null && SymbolEqualityComparer.Default.Equals(type, expected);

        private static void Report(OperationAnalysisContext context, string source) =>
            context.ReportDiagnostic(Diagnostic.Create(UnseededRandom, context.Operation.Syntax.GetLocation(), source));
    }

    /// <summary>
    /// The types HNT0004, HNT0005 and HNT0007 compare against; each may be missing from a compilation.
    /// </summary>
    private sealed class KnownTypes(Compilation compilation)
    {
        public INamedTypeSymbol? Assert { get; } = compilation.GetTypeByMetadataName("Xunit.Assert");

        public INamedTypeSymbol? Verifier { get; } = compilation.GetTypeByMetadataName("VerifyXunit.Verifier");

        public INamedTypeSymbol? SkipException { get; } = compilation.GetTypeByMetadataName("Xunit.SkipException");

        public INamedTypeSymbol? Debug { get; } = compilation.GetTypeByMetadataName("System.Diagnostics.Debug");

        public INamedTypeSymbol Object { get; } = compilation.GetSpecialType(SpecialType.System_Object);

        public INamedTypeSymbol? Exception { get; } = compilation.GetTypeByMetadataName("System.Exception");

        /// <summary>
        /// Types whose methods only write an exception out; passing it to them still drops it.
        /// </summary>
        public ImmutableArray<INamedTypeSymbol> Writers { get; } = CollectWriters(compilation);

        private static ImmutableArray<INamedTypeSymbol> CollectWriters(Compilation compilation)
        {
            var writers = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
            string[] names =
            [
                "System.Console",
                "System.Diagnostics.Debug",
                "System.Diagnostics.Trace",
                "Xunit.Abstractions.ITestOutputHelper",
                "Xunit.ITestOutputHelper",
            ];
            foreach (var name in names)
            {
                if (compilation.GetTypeByMetadataName(name) is { } type)
                {
                    writers.Add(type);
                }
            }

            return writers.ToImmutable();
        }
    }

    /// <summary>
    /// What one method body does, as far as HNT0004 needs it.
    /// </summary>
    /// <param name="Asserts">
    /// The body (with its lambdas and local functions) calls an assertion member directly.
    /// </param>
    /// <param name="Callees">
    /// Methods declared in this compilation that the body calls or passes as a method group.
    /// </param>
    private sealed record MethodFacts(bool Asserts, ImmutableArray<IMethodSymbol> Callees);

    private sealed class Analysis(INamedTypeSymbol factAttribute, KnownTypes types)
    {
        private readonly ConcurrentDictionary<IMethodSymbol, MethodFacts> _facts = new(SymbolEqualityComparer.Default);

        // An abstract, virtual or interface member -> the overrides and implementations that a call to it can run
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
            var asserts = false;
            var callees = ImmutableArray.CreateBuilder<IMethodSymbol>();
            foreach (var block in context.OperationBlocks)
            {
                foreach (var operation in block.DescendantsAndSelf())
                {
                    switch (operation)
                    {
                        case IInvocationOperation invocation:
                            asserts |= IsAssertion(invocation.TargetMethod);
                            AddCallee(callees, invocation.TargetMethod);
                            if (isTestMethod && IsDebugAssert(invocation.TargetMethod))
                            {
                                context.ReportDiagnostic(
                                    Diagnostic.Create(DebugAssertInTest, invocation.Syntax.GetLocation())
                                );
                            }

                            break;
                        case IMethodReferenceOperation reference:
                            asserts |= IsAssertion(reference.Method);
                            AddCallee(callees, reference.Method);
                            break;
                        case ICatchClauseOperation catchClause when isTestMethod && DropsEverything(catchClause):
                            ReportBroadCatch(context, catchClause);
                            break;
                    }
                }
            }

            var normalized = Normalize(method);
            _facts[normalized] = new MethodFacts(asserts, callees.ToImmutable());
            RegisterImplementation(normalized);
            if (isTestMethod)
            {
                _testMethods.Add(method);
            }
        }

        public void ReportTestsWithoutAssertion(CompilationAnalysisContext context)
        {
            // A method known to reach no assertion, so a later test that calls it does not walk it again
            var checkedNothing = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            foreach (var testMethod in _testMethods)
            {
                var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
                if (ReachesAssertion(Normalize(testMethod), visited, checkedNothing))
                {
                    continue;
                }

                checkedNothing.UnionWith(visited);
                if (testMethod.Locations.Length > 0)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(TestMethodWithoutAssertion, testMethod.Locations[0], testMethod.Name)
                    );
                }
            }
        }

        private bool ReachesAssertion(
            IMethodSymbol method,
            HashSet<IMethodSymbol> visited,
            HashSet<IMethodSymbol> checkedNothing)
        {
            if (checkedNothing.Contains(method) || !visited.Add(method))
            {
                return false;
            }

            // An abstract or interface member has no body of its own; its implementations may assert
            if (_implementations.TryGetValue(method, out var implementations))
            {
                foreach (var implementation in implementations)
                {
                    if (ReachesAssertion(implementation, visited, checkedNothing))
                    {
                        return true;
                    }
                }
            }

            if (!_facts.TryGetValue(method, out var facts))
            {
                return false;
            }

            if (facts.Asserts)
            {
                return true;
            }

            foreach (var callee in facts.Callees)
            {
                if (ReachesAssertion(callee, visited, checkedNothing))
                {
                    return true;
                }
            }

            return false;
        }

        // Records the members that a call can dispatch to this method: what it overrides and what it implements
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
                            method
                        ))
                    {
                        AddImplementation(interfaceMethod, method);
                    }
                }
            }
        }

        private void AddImplementation(IMethodSymbol member, IMethodSymbol implementation) =>
            _implementations.GetOrAdd(Normalize(member), static _ => []).Add(implementation);

        private bool IsAssertion(IMethodSymbol method)
        {
            var containingType = method.ContainingType;
            if (containingType is null)
            {
                return false;
            }

            if (SymbolEqualityComparer.Default.Equals(containingType, types.Assert) ||
                SymbolEqualityComparer.Default.Equals(containingType, types.Verifier))
            {
                return true;
            }

            // FluentAssertions spreads its assertion methods over many types; its namespace identifies them
            var root = containingType.ContainingNamespace;
            while (root is { ContainingNamespace.IsGlobalNamespace: false })
            {
                root = root.ContainingNamespace;
            }

            return root is { IsGlobalNamespace: false, Name: FluentAssertionsNamespace };
        }

        private bool IsDebugAssert(IMethodSymbol method) =>
            method.Name == "Assert" && SymbolEqualityComparer.Default.Equals(method.ContainingType, types.Debug);

        private bool DropsEverything(ICatchClauseOperation catchClause)
        {
            // 'catch' without a declaration catches object
            var catchesEverything =
                SymbolEqualityComparer.Default.Equals(catchClause.ExceptionType, types.Object) ||
                SymbolEqualityComparer.Default.Equals(catchClause.ExceptionType, types.Exception);
            if (!catchesEverything || catchClause.Filter is not null)
            {
                return false;
            }

            var exceptionLocal = (catchClause.ExceptionDeclarationOrExpression as IVariableDeclaratorOperation)?.Symbol;
            return !EndsInThrow(catchClause.Handler) && !KeepsException(catchClause.Handler, exceptionLocal);
        }

        // The handler throws on every path: a 'throw' statement of its own, and no return / break / continue before it
        private bool EndsInThrow(IBlockOperation handler)
        {
            var throws = false;
            foreach (var statement in handler.Operations)
            {
                var thrown = statement switch
                {
                    IExpressionStatementOperation { Operation: IThrowOperation inner } => inner,
                    IThrowOperation direct => direct,
                    _ => null,
                };
                if (thrown is not null && !IsSkip(thrown))
                {
                    throws = true;
                }
            }

            return throws && !LeavesEarly(handler);
        }

        private bool IsSkip(IThrowOperation thrown)
        {
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

        private static bool LeavesEarly(IOperation handler)
        {
            foreach (var operation in DescendantsOutsideFunctions(handler))
            {
                if (operation is IReturnOperation or IBranchOperation)
                {
                    return true;
                }
            }

            return false;
        }

        // The handler records the exception (assigns it, passes it on) or checks it with an assertion
        private bool KeepsException(IOperation handler, ILocalSymbol? exceptionLocal)
        {
            foreach (var operation in handler.Descendants())
            {
                switch (operation)
                {
                    case IInvocationOperation invocation when IsAssertion(invocation.TargetMethod):
                        return true;
                    case IInvocationOperation invocation when exceptionLocal is not null && !IsWriter(invocation):
                        foreach (var argument in invocation.Arguments)
                        {
                            if (References(argument.Value, exceptionLocal))
                            {
                                return true;
                            }
                        }

                        break;
                    case ISimpleAssignmentOperation assignment when exceptionLocal is not null &&
                        References(assignment.Value, exceptionLocal):
                        return true;
                }
            }

            return false;
        }

        private bool IsWriter(IInvocationOperation invocation)
        {
            foreach (var writer in types.Writers)
            {
                if (SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, writer))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool References(IOperation value, ILocalSymbol local)
        {
            foreach (var operation in value.DescendantsAndSelf())
            {
                if (operation is ILocalReferenceOperation reference &&
                    SymbolEqualityComparer.Default.Equals(reference.Local, local))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<IOperation> DescendantsOutsideFunctions(IOperation operation)
        {
            foreach (var child in operation.ChildOperations)
            {
                if (child is IAnonymousFunctionOperation or ILocalFunctionOperation)
                {
                    continue;
                }

                yield return child;
                foreach (var descendant in DescendantsOutsideFunctions(child))
                {
                    yield return descendant;
                }
            }
        }

        private static void ReportBroadCatch(OperationBlockAnalysisContext context, ICatchClauseOperation catchClause)
        {
            if (catchClause.Syntax is not CatchClauseSyntax syntax)
            {
                return;
            }

            // From 'catch' to the end of '(Exception ex)', or 'catch' alone
            var end = syntax.Declaration?.Span.End ?? syntax.CatchKeyword.Span.End;
            var span = TextSpan.FromBounds(syntax.CatchKeyword.SpanStart, end);
            var location = Location.Create(syntax.SyntaxTree, span);
            var text = syntax.SyntaxTree.GetText(context.CancellationToken).ToString(span);
            context.ReportDiagnostic(Diagnostic.Create(BroadCatchInTest, location, text));
        }

        private static void AddCallee(ImmutableArray<IMethodSymbol>.Builder callees, IMethodSymbol method)
        {
            // Local functions and lambdas are walked as part of the enclosing body; metadata methods have no body here
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
