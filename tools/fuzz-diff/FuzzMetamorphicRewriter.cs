using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Control-flow wrapper kind for <see cref="FuzzMetamorphicRewriter.WrapStatementInControlFlow"/>.
/// </summary>
public enum MetamorphicWrapKind
{
    /// <summary>Wraps the target statement in a plain block <c>{ S }</c>.</summary>
    Block = 0,

    /// <summary>Wraps the target statement in <c>if (true) { S }</c>.</summary>
    IfTrue = 1,

    /// <summary>Wraps the target statement in <c>for (int metaOnce0 = 0; metaOnce0 &lt; 1; metaOnce0++) { S }</c>.</summary>
    SingleIterationLoop = 2,
}

/// <summary>
/// Entry point for C#-semantics-preserving metamorphic source-to-source kernel transformations (<c>#1138</c>).
/// Coordinates <see cref="CSharpCompilation"/> and <see cref="SemanticModel"/> updates across transformation stages.
/// </summary>
public static class FuzzMetamorphicRewriter
{
    /// <summary>
    /// Gets the pool of C++ keyword and CUDA/Prelude candidate identifiers used for alpha-renaming.
    /// </summary>
    public static IReadOnlyList<string> CandidateIdentifiers => MetamorphicIdentifierRenamer.CandidateIdentifiers;

    /// <summary>
    /// Transformation 1: Consistently alpha-renames non-reserved parameters and local variables
    /// in the target <c>[GpuKernel]</c> method using <see cref="SemanticModel"/> symbol resolution.
    /// </summary>
    public static string RenameLocalsAndParameters(string source, ulong seed = 1UL, string? targetMethodName = null)
    {
        _ = MetamorphicTypeResolver.CreateCompilation(source, out var root, out var semanticModel);
        var updatedRoot = MetamorphicIdentifierRenamer.Apply(root, semanticModel, seed, targetMethodName);
        return ReferenceEquals(updatedRoot, root) ? source : FuzzSyntaxHelpers.RenderFullNode(updatedRoot);
    }

    /// <summary>
    /// Transformation 2a: Extracts a pure, unconditionally-evaluated subexpression into a fresh local variable
    /// declared immediately before the enclosing statement.
    /// </summary>
    public static string ExtractPureSubexpressionToLocal(string source, ulong seed = 1UL, string? targetMethodName = null)
    {
        _ = MetamorphicTypeResolver.CreateCompilation(source, out var root, out var semanticModel);
        var updatedRoot = MetamorphicSubexpressionExtractor.ExtractToLocal(root, semanticModel, seed, targetMethodName);
        return ReferenceEquals(updatedRoot, root) ? source : FuzzSyntaxHelpers.RenderFullNode(updatedRoot);
    }

    /// <summary>
    /// Transformation 2b: Extracts a pure, unconditionally-evaluated subexpression into a new static
    /// <c>[GpuDeviceFunction]</c> method in the enclosing kernel class and replaces the subexpression with a call.
    /// </summary>
    public static string ExtractPureSubexpressionToDeviceFunction(
        string source,
        ulong seed = 1UL,
        string? targetMethodName = null,
        string? helperMethodName = null)
    {
        _ = MetamorphicTypeResolver.CreateCompilation(source, out var root, out var semanticModel);
        var updatedRoot = MetamorphicSubexpressionExtractor.ExtractToDeviceFunction(
            root,
            semanticModel,
            seed,
            targetMethodName,
            helperMethodName);
        return ReferenceEquals(updatedRoot, root) ? source : FuzzSyntaxHelpers.RenderFullNode(updatedRoot);
    }

    /// <summary>
    /// Transformation 3: Inlines a pure, non-recursive <c>[GpuDeviceFunction]</c> call at its call site
    /// by substituting its pure argument expressions into its return expression.
    /// </summary>
    public static string InlineDeviceFunctionCall(string source, ulong seed = 1UL, string? targetMethodName = null)
    {
        _ = MetamorphicTypeResolver.CreateCompilation(source, out var root, out var semanticModel);
        var updatedRoot = MetamorphicDeviceFunctionInliner.Apply(root, semanticModel, seed, targetMethodName);
        return ReferenceEquals(updatedRoot, root) ? source : FuzzSyntaxHelpers.RenderFullNode(updatedRoot);
    }

    /// <summary>
    /// Transformation 4: Reorders two adjacent independent statements whose read and write symbol sets do not overlap.
    /// </summary>
    public static string ReorderIndependentStatements(string source, ulong seed = 1UL, string? targetMethodName = null)
    {
        _ = MetamorphicTypeResolver.CreateCompilation(source, out var root, out var semanticModel);
        var updatedRoot = MetamorphicStatementReorderer.Apply(root, semanticModel, seed, targetMethodName);
        return ReferenceEquals(updatedRoot, root) ? source : FuzzSyntaxHelpers.RenderFullNode(updatedRoot);
    }

    /// <summary>
    /// Transformation 5: Swaps the left and right operands of a commutative binary operator
    /// (<c>+</c>, <c>*</c>, <c>&amp;</c>, <c>|</c>, <c>^</c>, <c>==</c>, <c>!=</c>) whose operands are pure and share the same non-float value type.
    /// </summary>
    public static string SwapCommutativeOperands(string source, ulong seed = 1UL, string? targetMethodName = null)
    {
        _ = MetamorphicTypeResolver.CreateCompilation(source, out var root, out var semanticModel);
        var updatedRoot = MetamorphicCommutativeSwapper.Apply(root, semanticModel, seed, targetMethodName);
        return ReferenceEquals(updatedRoot, root) ? source : FuzzSyntaxHelpers.RenderFullNode(updatedRoot);
    }

    /// <summary>
    /// Transformation 6: Wraps an executable non-declaring statement without barriers, warp calls, or jumps
    /// in a block <c>{ S }</c>, <c>if (true) { S }</c>, or a single-iteration loop.
    /// </summary>
    public static string WrapStatementInControlFlow(
        string source,
        ulong seed = 1UL,
        string? targetMethodName = null,
        MetamorphicWrapKind? wrapKind = null)
    {
        _ = MetamorphicTypeResolver.CreateCompilation(source, out var root, out var semanticModel);
        var updatedRoot = MetamorphicControlFlowWrapper.Apply(root, semanticModel, seed, targetMethodName, wrapKind);
        return ReferenceEquals(updatedRoot, root) ? source : FuzzSyntaxHelpers.RenderFullNode(updatedRoot);
    }

    /// <summary>
    /// Transformation 7: Inserts dead code (an unused local variable initialized with a pure expression
    /// and an unreachable <c>if (false)</c> branch) into the target method body.
    /// </summary>
    public static string AddDeadCode(string source, ulong seed = 1UL, string? targetMethodName = null)
    {
        _ = MetamorphicTypeResolver.CreateCompilation(source, out var root, out var semanticModel);
        var updatedRoot = MetamorphicDeadCodeInserter.Apply(root, semanticModel, seed, targetMethodName);
        return ReferenceEquals(updatedRoot, root) ? source : FuzzSyntaxHelpers.RenderFullNode(updatedRoot);
    }

    /// <summary>
    /// Applies a deterministic pipeline of the 7 C#-semantics-preserving transformations to produce
    /// metamorphic variant <paramref name="variantKernelName"/> from <paramref name="baseKernelMethodSource"/>,
    /// updating the <see cref="CSharpCompilation"/> via <see cref="CSharpCompilation.ReplaceSyntaxTree"/> after each stage.
    /// </summary>
    internal static (string VariantKernelMethodSource, IReadOnlyList<string> ExtraDeviceFunctionSources) BuildMetamorphicVariant(
        string className,
        IReadOnlyList<DeviceFunctionSpec> baseDeviceFunctions,
        string baseKernelName,
        string baseKernelMethodSource,
        string variantKernelName,
        int variantIndex,
        ulong variantSeed)
    {
        using var unitWriter = new SourceWriter();
        FuzzCaseEmitter.AppendKernelsFileHeader(unitWriter, "Kassyi.GSharp.Tests.Shared.Differential");
        ScalarFuzzGenerator.AppendEnumDeclarations(unitWriter);
        unitWriter.AppendLine();
        FuzzCompositeModel.AppendCompositeStructDeclarations(unitWriter);
        unitWriter.AppendLine();
        using (unitWriter.Scope($"public static partial class {className}"))
        {
            FuzzDeviceFunctionEmitter.AppendDeviceFunctionDeclarations(unitWriter, baseDeviceFunctions);
            unitWriter.AppendLine();
            unitWriter.AppendLine(baseKernelMethodSource);
        }

        var initialRoot = FuzzSyntaxHelpers.ParseRoot(unitWriter.ToString());
        var origMethod = MetamorphicEffectAnalyzer.FindTargetKernelMethod(initialRoot, baseKernelName);
        if (origMethod is null)
        {
            return (baseKernelMethodSource, []);
        }

        var renamedMethod = origMethod.WithIdentifier(SyntaxFactory.Identifier(variantKernelName));
        string initialSource = FuzzSyntaxHelpers.RenderFullNode(initialRoot.ReplaceNode(origMethod, renamedMethod));

        var compilation = MetamorphicTypeResolver.CreateCompilation(initialSource, out var currentRoot, out var semanticModel);
        string extractedHelperName = string.Create(CultureInfo.InvariantCulture, $"MetaHelper_{variantKernelName}");

        if ((variantIndex & 1) == 0)
        {
            ApplyStage(ref compilation, ref currentRoot, ref semanticModel,
                (r, m) => MetamorphicSubexpressionExtractor.ExtractToLocal(r, m, variantSeed ^ 0x10UL, variantKernelName));
            ApplyStage(ref compilation, ref currentRoot, ref semanticModel,
                (r, m) => MetamorphicDeviceFunctionInliner.Apply(r, m, variantSeed ^ 0x20UL, variantKernelName));
            ApplyStage(ref compilation, ref currentRoot, ref semanticModel,
                (r, m) => MetamorphicStatementReorderer.Apply(r, m, variantSeed ^ 0x30UL, variantKernelName));
            ApplyStage(ref compilation, ref currentRoot, ref semanticModel,
                (r, m) => MetamorphicCommutativeSwapper.Apply(r, m, variantSeed ^ 0x40UL, variantKernelName));
            ApplyStage(ref compilation, ref currentRoot, ref semanticModel,
                (r, m) => MetamorphicControlFlowWrapper.Apply(r, m, variantSeed ^ 0x50UL, variantKernelName, MetamorphicWrapKind.IfTrue));
            ApplyStage(ref compilation, ref currentRoot, ref semanticModel,
                (r, m) => MetamorphicDeadCodeInserter.Apply(r, m, variantSeed ^ 0x60UL, variantKernelName));
            ApplyStage(ref compilation, ref currentRoot, ref semanticModel,
                (r, m) => MetamorphicIdentifierRenamer.Apply(r, m, variantSeed ^ 0x70UL, variantKernelName));
        }
        else
        {
            ApplyStage(ref compilation, ref currentRoot, ref semanticModel,
                (r, m) => MetamorphicSubexpressionExtractor.ExtractToDeviceFunction(r, m, variantSeed ^ 0x11UL, variantKernelName, extractedHelperName));
            ApplyStage(ref compilation, ref currentRoot, ref semanticModel,
                (r, m) => MetamorphicDeviceFunctionInliner.Apply(r, m, variantSeed ^ 0x22UL, variantKernelName));
            ApplyStage(ref compilation, ref currentRoot, ref semanticModel,
                (r, m) => MetamorphicStatementReorderer.Apply(r, m, variantSeed ^ 0x33UL, variantKernelName));
            ApplyStage(ref compilation, ref currentRoot, ref semanticModel,
                (r, m) => MetamorphicCommutativeSwapper.Apply(r, m, variantSeed ^ 0x44UL, variantKernelName));
            ApplyStage(ref compilation, ref currentRoot, ref semanticModel,
                (r, m) => MetamorphicControlFlowWrapper.Apply(r, m, variantSeed ^ 0x55UL, variantKernelName, MetamorphicWrapKind.SingleIterationLoop));
            ApplyStage(ref compilation, ref currentRoot, ref semanticModel,
                (r, m) => MetamorphicDeadCodeInserter.Apply(r, m, variantSeed ^ 0x66UL, variantKernelName));
            ApplyStage(ref compilation, ref currentRoot, ref semanticModel,
                (r, m) => MetamorphicIdentifierRenamer.Apply(r, m, variantSeed ^ 0x77UL, variantKernelName));
        }

        var finalMethod = MetamorphicEffectAnalyzer.FindTargetKernelMethod(currentRoot, variantKernelName);
        string variantMethodSource = finalMethod is not null
            ? $"// fuzz-seed: {FuzzRandom.FormatSeed(variantSeed)}, metamorphic-of: {baseKernelName}\n{FuzzSyntaxHelpers.RenderNodeText(finalMethod)}"
            : baseKernelMethodSource;

        var baseHelperNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var df in baseDeviceFunctions)
        {
            baseHelperNames.Add(df.MethodName);
        }

        var extraHelpers = new List<string>();
        foreach (var dfMethod in FuzzSyntaxHelpers.GetDeviceFunctionMethods(currentRoot))
        {
            if (!baseHelperNames.Contains(dfMethod.Identifier.ValueText))
            {
                extraHelpers.Add(FuzzSyntaxHelpers.RenderNodeText(dfMethod));
            }
        }

        return (variantMethodSource, extraHelpers);
    }

    private static void ApplyStage(
        ref CSharpCompilation compilation,
        ref CompilationUnitSyntax currentRoot,
        ref SemanticModel semanticModel,
        Func<CompilationUnitSyntax, SemanticModel, CompilationUnitSyntax> stage)
    {
        var nextRoot = stage(currentRoot, semanticModel);
        if (!ReferenceEquals(nextRoot, currentRoot))
        {
            compilation = MetamorphicTypeResolver.ReplaceSyntaxTree(
                compilation,
                currentRoot.SyntaxTree,
                nextRoot,
                out currentRoot,
                out semanticModel);
        }
    }
}
