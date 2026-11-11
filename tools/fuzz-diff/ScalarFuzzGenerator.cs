using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Deterministic kernel generator for G# differential fuzzing.
/// Generates type-safe, UB-free C# GPU kernels and companion <c>DifferentialCase</c> definitions
/// exercising scalar types, nested structs, <see cref="Nullable{T}"/>, <see cref="Span{T}"/>,
/// <c>switch</c> expressions/statements, <c>ref</c> ternary, tuple deconstruction, bounded loops,
/// <c>Gpu.ForUnroll</c>, and multi-stage <c>[GpuDeviceFunction]</c> call DAGs under Swarm Testing
/// and per-issue avoidance switches (<see cref="KnownIssueFilter"/>).
/// </summary>
public sealed class ScalarFuzzGenerator
{
    /// <summary>The 64-bit FNV-1a offset basis.</summary>
    public const ulong FnvOffsetBasis = FuzzChecksumEmitter.FnvOffsetBasis;

    /// <summary>The 64-bit FNV-1a prime multiplier.</summary>
    public const ulong FnvPrime = FuzzChecksumEmitter.FnvPrime;

    private readonly FuzzOptions _options;
    private readonly KnownIssueFilter _issueFilter;
    private readonly IReadOnlyList<DeviceFunctionSpec> _deviceFunctions;
    private readonly IReadOnlyList<string> _splitHelperSources;

    /// <summary>Initializes a new instance of the <see cref="ScalarFuzzGenerator"/> class.</summary>
    public ScalarFuzzGenerator(FuzzOptions? options = null)
    {
        _options = options ?? new FuzzOptions();
        _issueFilter = new KnownIssueFilter(_options.Profile, _options.AllowedIssues);
        _deviceFunctions = FuzzDeviceFunctionEmitter.BuildDeviceFunctionDag(_options.FuzzSeed, _options.KernelClassName);
        _splitHelperSources = FuzzSplitKernelEmitter.BuildHelperSources(_options.FuzzSeed);
    }

    /// <summary>Gets the active <see cref="KnownIssueFilter"/> configured for this generator.</summary>
    public KnownIssueFilter IssueFilter => _issueFilter;

    /// <summary>Gets the topological <c>[GpuDeviceFunction]</c> DAG generated for this batch.</summary>
    public IReadOnlyList<DeviceFunctionSpec> DeviceFunctions => _deviceFunctions;

    /// <summary>
    /// Gets the device functions that split kernels call (<see cref="FuzzSplitKernelEmitter.BuildHelperSources"/>); a
    /// batch declares them only when it has a split kernel.
    /// </summary>
    public IReadOnlyList<string> SplitHelperSources => _splitHelperSources;

    private FuzzSplitGenerationContext SplitContext => new(_options, _issueFilter, _deviceFunctions);

    /// <summary>
    /// Generates a full batch of <see cref="FuzzOptions.BatchSize"/> kernels (plus metamorphic variants when
    /// <see cref="FuzzOptions.Metamorphic"/> is enabled) and their companion <c>DifferentialCase</c> definitions.
    /// </summary>
    public GeneratedBatch GenerateBatch()
    {
        int batchSize = Math.Clamp(_options.BatchSize, 1, 256);
        int metaVariants = _options.Metamorphic ? Math.Clamp(_options.MetamorphicVariantsPerKernel, 1, 4) : 0;
        var masterRng = new FuzzRandom(_options.FuzzSeed);
        var kernels = new List<GeneratedKernelSpec>(batchSize * (1 + metaVariants));
        var extraDeviceFunctions = new List<string>();
        bool hasSplitKernel = false;

        for (int i = 0; i < batchSize; i++)
        {
            ulong kernelSeed = masterRng.NextUInt64();
            int size = _options.BatchSize == 1
                ? Math.Clamp(_options.Size, 1, 8)
                : 1 + (i * 8 / batchSize);
            size = Math.Clamp(size, 1, 8);
            string kernelName = string.Create(CultureInfo.InvariantCulture, $"Kernel_{i:D2}");
            var splitForms = FuzzSplitKernelEmitter.ChooseForms(kernelSeed, _options.SwarmFocus);
            if (splitForms != FuzzSplitForms.None)
            {
                // No metamorphic variants: a variant is checked against its base kernel run as plain C#, which cannot
                // run a shuffle.
                kernels.Add(FuzzSplitKernelGenerator.Generate(SplitContext, kernelName, kernelSeed, size, splitForms));
                hasSplitKernel = true;
                continue;
            }

            var baseSpec = GenerateSingleKernel(kernelName, kernelSeed, size);
            kernels.Add(baseSpec);

            AddMetamorphicVariants(baseSpec, metaVariants, kernels, extraDeviceFunctions);
        }

        string kernelsSource = BuildKernelsCompilationUnit(kernels, extraDeviceFunctions, hasSplitKernel);
        string casesSource = BuildCasesCompilationUnit(kernels);

        ValidateSyntaxTree(kernelsSource, "FuzzBatchKernels.cu.cs");
        ValidateSyntaxTree(casesSource, "FuzzBatchCases.cs");

        return new GeneratedBatch(_options.FuzzSeed, _options.InputSeed, kernelsSource, casesSource, kernels);
    }

    /// <summary>
    /// Adds <paramref name="variantCount"/> metamorphic variants of <paramref name="baseSpec"/> (<c>{name}_Meta{m}</c>),
    /// each checked against the base kernel as its oracle, and collects the device functions the variants add.
    /// </summary>
    private void AddMetamorphicVariants(
        GeneratedKernelSpec baseSpec,
        int variantCount,
        List<GeneratedKernelSpec> kernels,
        List<string> extraDeviceFunctions)
    {
        for (int m = 0; m < variantCount; m++)
        {
            string metaKernelName = string.Create(CultureInfo.InvariantCulture, $"{baseSpec.KernelName}_Meta{m}");
            ulong metaSeed = baseSpec.KernelSeed ^ ((ulong)(m + 1) * 0x9E3779B97F4A7C15UL);
            var (variantMethodSource, extraHelpers) = FuzzMetamorphicRewriter.BuildMetamorphicVariant(
                _options.KernelClassName,
                _deviceFunctions,
                baseSpec.KernelName,
                baseSpec.KernelMethodSource,
                metaKernelName,
                m,
                metaSeed);
            extraDeviceFunctions.AddRange(extraHelpers);

            string metaCaseHeader = string.Create(
                CultureInfo.InvariantCulture,
                $"// fuzz-seed: {FuzzRandom.FormatSeed(baseSpec.KernelSeed)}, metamorphic-variant: {m}");
            string metaCaseProperty = FuzzCaseEmitter.RenderDifferentialCaseProperty(
                _options.KernelClassName,
                metaKernelName,
                _options.InputSeed,
                metaCaseHeader,
                oracleMethodName: baseSpec.KernelName);
            kernels.Add(baseSpec with
            {
                KernelName = metaKernelName,
                KernelMethodSource = variantMethodSource,
                CasePropertySource = metaCaseProperty,
            });
        }
    }

    /// <summary>
    /// Generates a single kernel and its companion <c>DifferentialCase</c> from an explicit <paramref name="kernelSeed"/> and <paramref name="size"/>.
    /// </summary>
    public GeneratedKernelSpec GenerateSingleKernel(string kernelName, ulong kernelSeed, int size = 4)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kernelName);
        size = Math.Clamp(size, 1, 8);
        if (FuzzSplitKernelEmitter.IsSplitFocus(_options.SwarmFocus))
        {
            return FuzzSplitKernelGenerator.Generate(
                SplitContext,
                kernelName,
                kernelSeed,
                size,
                FuzzSplitKernelEmitter.ChooseForms(kernelSeed, _options.SwarmFocus));
        }

        var state = CreateScalarState(kernelSeed, size);
        EmitScalarBody(state, size);

        string caseHeader = string.Create(
            CultureInfo.InvariantCulture,
            $"// fuzz-seed: {FuzzRandom.FormatSeed(kernelSeed)}");
        string caseProperty = FuzzCaseEmitter.RenderDifferentialCaseProperty(
            _options.KernelClassName,
            kernelName,
            _options.InputSeed,
            caseHeader);
        return new GeneratedKernelSpec(
            kernelName,
            kernelSeed,
            size,
            RenderKernelMethod(kernelName, kernelSeed, size, state),
            caseProperty,
            FuzzSplitKernelGenerator.LocalNames(state),
            state.Statements.ToArray());
    }

    private KernelGenerationState CreateScalarState(ulong kernelSeed, int size)
    {
        var rng = new FuzzRandom(kernelSeed);
        var swarm = FuzzSwarmConfig.Create(rng, _options.SwarmFocus);
        double interferenceProb = Math.Clamp(_options.InterferenceProbability, 0.25, 0.60);
        return new KernelGenerationState(
            rng,
            size,
            interferenceProb,
            _issueFilter,
            swarm,
            _options.KernelClassName,
            _deviceFunctions);
    }

    // The locals, the core statements of each emitter, the random statements and the checksum folds
    private static void EmitScalarBody(KernelGenerationState state, int size)
    {
        FuzzScalarLocals.Initialize(state);
        FuzzCompositeExpressionEmitter.InitializeCompositeLocals(state);

        FuzzScalarStatementEmitter.EmitCoreScalarStatements(state);
        FuzzCompositeExpressionEmitter.EmitCoreCompositeStatements(state);
        FuzzControlFlowEmitter.EmitCoreControlFlowStatements(state);
        FuzzDeviceFunctionEmitter.EmitCoreDeviceFunctionCalls(state);

        int extraStatements = 2 + size * 2;
        for (int s = 0; s < extraStatements; s++)
        {
            FuzzScalarStatementEmitter.EmitRandomStatement(state);
            if (s % 3 == 2)
            {
                FuzzChecksumEmitter.EmitChecksumFoldSubset(state);
            }
        }

        FuzzChecksumEmitter.EmitChecksumFoldAll(state);
    }

    /// <summary>Appends the device enum declarations used by generated kernels to <paramref name="writer"/>.</summary>
    public static void AppendEnumDeclarations(SourceWriter writer) =>
        FuzzScalarLocals.AppendEnumDeclarations(writer);

    /// <summary>
    /// Emits <paramref name="count"/> generated statements into <paramref name="writer"/> at the next control-flow depth
    /// (up to depth <c>2</c>), preserving loop-variable immutability and scoping invariants.
    /// </summary>
    internal static void EmitNestedStatements(
        KernelGenerationState state,
        SourceWriter writer,
        int count,
        bool inSwitchSection = false) =>
        FuzzScalarStatementEmitter.EmitNestedStatements(state, writer, count, inSwitchSection);

    private static string RenderKernelMethod(string kernelName, ulong kernelSeed, int size, KernelGenerationState state)
    {
        using var writer = new SourceWriter();
        writer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"// fuzz-seed: {FuzzRandom.FormatSeed(kernelSeed)}, size: {size}"));
        writer.AppendLine("[GpuKernel]");
        using (writer.Scope($"public static void {kernelName}(int[] inA, int[] inB, int[] inC, ulong[] checksum)"))
        {
            writer.AppendLine("int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;");
            using (writer.Scope("if (gid >= checksum.Length)"))
            {
                writer.AppendLine("return;");
            }

            writer.AppendLine();
            writer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"ulong hash = {FnvOffsetBasis}UL;"));
            foreach (string stmt in state.Statements)
            {
                writer.AppendLine(stmt);
            }

            writer.AppendLine("checksum[gid] = hash;");
        }

        return writer.ToString().AsSpan().TrimEnd().ToString();
    }

    private string BuildKernelsCompilationUnit(
        IReadOnlyList<GeneratedKernelSpec> kernels,
        IReadOnlyList<string> extraDeviceFunctions,
        bool hasSplitKernel)
    {
        using var writer = new SourceWriter();
        FuzzCaseEmitter.AppendKernelsFileHeader(writer, _options.Namespace);
        AppendEnumDeclarations(writer);
        writer.AppendLine();
        FuzzCompositeModel.AppendCompositeStructDeclarations(writer);
        writer.AppendLine();

        using (writer.Scope($"public static partial class {_options.KernelClassName}"))
        {
            FuzzDeviceFunctionEmitter.AppendDeviceFunctionDeclarations(writer, _deviceFunctions);
            writer.AppendLine();
            for (int h = 0; h < extraDeviceFunctions.Count; h++)
            {
                writer.AppendLine(extraDeviceFunctions[h]);
                writer.AppendLine();
            }

            if (hasSplitKernel)
            {
                foreach (string helper in _splitHelperSources)
                {
                    writer.AppendLine(helper);
                    writer.AppendLine();
                }
            }

            for (int i = 0; i < kernels.Count; i++)
            {
                if (i > 0)
                {
                    writer.AppendLine();
                }

                writer.AppendLine(kernels[i].KernelMethodSource);
            }
        }

        return hasSplitKernel ? FuzzSplitOracle.WithOracleTwins(writer.ToString()) : writer.ToString();
    }

    private string BuildCasesCompilationUnit(IReadOnlyList<GeneratedKernelSpec> kernels)
    {
        var entries = new List<(string KernelName, ulong KernelSeed, string CasePropertySource)>(kernels.Count);
        foreach (var k in kernels)
        {
            entries.Add((k.KernelName, k.KernelSeed, k.CasePropertySource));
        }

        return FuzzCaseEmitter.BuildCasesCompilationUnit(
            _options.Namespace,
            _options.CasesClassName,
            _options.FuzzSeed,
            entries,
            _options.EmitProgramMain);
    }

    private static void ValidateSyntaxTree(string source, string fileName)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions, path: fileName);
        foreach (var diag in tree.GetDiagnostics())
        {
            if (diag.Severity == DiagnosticSeverity.Error)
            {
                throw new InvalidOperationException($"Generated invalid C# syntax in {fileName}: {diag}");
            }
        }
    }
}
