using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Compiler.Transpiler.Dialects;
using Kassyi.GSharp.Compiler.Transpiler.Helpers;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// GCNR0002: lowers every kernel and suspending device function in the shared test compilation and verifies that
/// <see cref="ContextHoister"/> and <see cref="LoweringIdentity"/> maintain declaration/reference consistency across
/// <c>ContextFields</c> and <c>LoweredBody</c>.
/// </summary>
public class HoistedSymbolConsistencyTests
{
    private const string ConsistencyId = "GCNR0002";

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Test_LoweredBodies_MaintainHoistedDeclarationIdentityConsistency()
    {
        var compilation = MegaCompilationCache.Compilation;
        var dialect = new StandardCppDialect();
        var violations = new List<string>();
        var loweredCount = 0;

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (!method.HasBody() || model.GetDeclaredSymbol(method) is not { } symbol)
                {
                    continue;
                }

                var isKernel = symbol.HasAttribute(KnownAttributes.GpuKernel);
                var isSuspendingDevice = !isKernel &&
                                         symbol.HasAttribute(KnownAttributes.GpuDeviceFunction) &&
                                         !IteratorTypeResolver.IsDeviceIteratorMethod(symbol) &&
                                         SuspensionAnalysis.RequiresStateMachine(symbol, compilation);
                if (!isKernel && !isSuspendingDevice)
                {
                    continue;
                }

                var methodLabel = $"{symbol.ContainingType.Name}.{symbol.Name}";
                LoweredStateMachine lowered;
                try
                {
                    lowered = AstLoweringPipeline.Lower(method, model, symbol, isSuspendingDevice);
                }
                catch (Exception ex)
                {
                    violations.Add($"{methodLabel}: exception during lowering: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                loweredCount++;
                VerifyLoweredMethod(methodLabel, symbol, lowered, model, dialect, violations);
            }
        }

        Assert.True(loweredCount > 0, "No GPU method was lowered; the consistency scan did not run.");
        Assert.True(
            violations.Count == 0,
            $"{ConsistencyId}: hoisted declaration identity violations:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    private static void VerifyLoweredMethod(
        string methodLabel,
        IMethodSymbol methodSymbol,
        LoweredStateMachine lowered,
        SemanticModel model,
        ITranspilerDialect dialect,
        List<string> violations)
    {
        var hoistedKeys = new HashSet<DeclarationKey>();
        var cppFieldNames = new HashSet<string>(StringComparer.Ordinal);

        // (a) No duplicate DeclarationKey in ContextFields, and (e) unique C++ field names in ContextFields.
        foreach (var field in lowered.ContextFields)
        {
            var key = LoweringIdentity.ForDeclaration(field, model);
            if (!hoistedKeys.Add(key))
            {
                violations.Add($"{methodLabel}: (a) duplicate DeclarationKey in ContextFields: '{field.Identifier.ValueText}' (region {key.Region})");
            }

            // Synthesized temporaries have no symbol and use their unique counter name verbatim.
            var cppName = CppNameResolver.Resolve(
                key.Symbol,
                field.Identifier.ValueText,
                dialect,
                key.Region).ToString();
            if (!cppFieldNames.Add(cppName))
            {
                violations.Add($"{methodLabel}: (e) duplicate C++ field name in ContextFields: '{cppName}'");
            }
        }

        var copiedParameterKeys = new HashSet<DeclarationKey>();
        if (lowered.CopiedParameters.Count > 0)
        {
            var copiedNames = new HashSet<string>(lowered.CopiedParameters, StringComparer.Ordinal);
            foreach (var parameter in methodSymbol.Parameters)
            {
                if (copiedNames.Contains(parameter.Name))
                {
                    copiedParameterKeys.Add(new DeclarationKey(parameter, parameter.Name, 0));
                }
            }
        }

        // (b) LoweredBody contains no VariableDeclaratorSyntax whose DeclarationKey is in hoistedKeys.
        foreach (var declarator in lowered.LoweredBody.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            var key = LoweringIdentity.ForDeclaration(declarator, model);
            if (hoistedKeys.Contains(key))
            {
                violations.Add($"{methodLabel}: (b) hoisted declarator '{declarator.Identifier.ValueText}' (region {key.Region}) remains in LoweredBody");
            }
        }

        // (c) Every IdentifierNameSyntax bound to a hoisted key (or copied parameter key) appears only as the Name of __gsharp_ctx->Name.
        foreach (var identifier in lowered.LoweredBody.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var key = LoweringIdentity.ForReference(identifier, model);
            if (!hoistedKeys.Contains(key) && !copiedParameterKeys.Contains(key))
            {
                continue;
            }

            var isCtxMemberName = identifier.Parent is MemberAccessExpressionSyntax memberAccess &&
                                  ContextLocalRewriter.IsContextPointerAccess(memberAccess) &&
                                  memberAccess.Name == identifier;
            if (!isCtxMemberName)
            {
                violations.Add($"{methodLabel}: (c) hoisted reference '{identifier.Identifier.ValueText}' (region {key.Region}) appears outside __gsharp_ctx-> in '{identifier.Parent}'");
            }
        }

        // (d) Every __gsharp_ctx->Name resolves to a key in hoistedKeys (or copiedParameterKeys).
        foreach (var memberAccess in lowered.LoweredBody.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (!ContextLocalRewriter.IsContextPointerAccess(memberAccess))
            {
                continue;
            }

            var key = LoweringIdentity.ForReference(memberAccess.Name, model);
            if (!hoistedKeys.Contains(key) && !copiedParameterKeys.Contains(key))
            {
                violations.Add($"{methodLabel}: (d) context access '__gsharp_ctx->{memberAccess.Name.Identifier.ValueText}' (symbol '{key.Symbol?.Kind.ToString() ?? "null"}', region {key.Region}) is not in the hoisted set");
            }
        }
    }
}
