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

    private const string EnumDeclarationsBlock =
        """
        public enum FuzzEnumByte : byte { Zero = 0, One = 1, Two = 2, Bit = 0x10, High = 0x80, Mask = 0x55, Alt = 0xAA, Edge = byte.MaxValue }
        public enum FuzzEnumShort : short { Zero = 0, One = 1, Two = 2, Bit = 0x0100, High = -256, Mask = 0x1234, Alt = -1, Edge = short.MinValue }
        public enum FuzzEnumInt : int { Zero = 0, One = 1, Two = 2, Bit = 0x00010000, High = -65536, Mask = 0x13579BDF, Alt = -1, Edge = int.MinValue }
        public enum FuzzEnumULong : ulong { Zero = 0UL, One = 1UL, Two = 2UL, Bit = 0x0000000100000000UL, High = 0x8000000000000000UL, Mask = 0x5555555555555555UL, Alt = 0xAAAAAAAAAAAAAAAAUL, Edge = ulong.MaxValue }
        """;

    private readonly FuzzOptions _options;
    private readonly KnownIssueFilter _issueFilter;
    private readonly IReadOnlyList<DeviceFunctionSpec> _deviceFunctions;

    /// <summary>Initializes a new instance of the <see cref="ScalarFuzzGenerator"/> class.</summary>
    public ScalarFuzzGenerator(FuzzOptions? options = null)
    {
        _options = options ?? new FuzzOptions();
        _issueFilter = new KnownIssueFilter(_options.Profile, _options.AllowedIssues);
        _deviceFunctions = FuzzDeviceFunctionEmitter.BuildDeviceFunctionDag(_options.FuzzSeed, _options.KernelClassName);
    }

    /// <summary>Gets the active <see cref="KnownIssueFilter"/> configured for this generator.</summary>
    public KnownIssueFilter IssueFilter => _issueFilter;

    /// <summary>Gets the topological <c>[GpuDeviceFunction]</c> DAG generated for this batch.</summary>
    public IReadOnlyList<DeviceFunctionSpec> DeviceFunctions => _deviceFunctions;

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

        for (int i = 0; i < batchSize; i++)
        {
            ulong kernelSeed = masterRng.NextUInt64();
            int size = _options.BatchSize == 1
                ? Math.Clamp(_options.Size, 1, 8)
                : 1 + (i * 8 / batchSize);
            size = Math.Clamp(size, 1, 8);
            string kernelName = string.Create(CultureInfo.InvariantCulture, $"Kernel_{i:D2}");
            var baseSpec = GenerateSingleKernel(kernelName, kernelSeed, size);
            kernels.Add(baseSpec);

            AddMetamorphicVariants(baseSpec, metaVariants, kernels, extraDeviceFunctions);
        }

        string kernelsSource = BuildKernelsCompilationUnit(kernels, extraDeviceFunctions);
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

        var rng = new FuzzRandom(kernelSeed);
        var swarm = FuzzSwarmConfig.Create(rng, _options.SwarmFocus);
        double interferenceProb = Math.Clamp(_options.InterferenceProbability, 0.25, 0.60);
        var state = new KernelGenerationState(
            rng,
            size,
            interferenceProb,
            _issueFilter,
            swarm,
            _options.KernelClassName,
            _deviceFunctions);

        InitializeScalarLocals(state);
        FuzzCompositeExpressionEmitter.InitializeCompositeLocals(state);

        EmitCoreScalarStatements(state);
        FuzzCompositeExpressionEmitter.EmitCoreCompositeStatements(state);
        FuzzControlFlowEmitter.EmitCoreControlFlowStatements(state);
        FuzzDeviceFunctionEmitter.EmitCoreDeviceFunctionCalls(state);

        int extraStatements = 2 + size * 2;
        for (int s = 0; s < extraStatements; s++)
        {
            EmitRandomStatement(state);
            if (s % 3 == 2)
            {
                FuzzChecksumEmitter.EmitChecksumFoldSubset(state);
            }
        }

        FuzzChecksumEmitter.EmitChecksumFoldAll(state);

        string kernelMethod = RenderKernelMethod(kernelName, kernelSeed, size, state);
        string caseHeader = string.Create(CultureInfo.InvariantCulture, $"// fuzz-seed: {FuzzRandom.FormatSeed(kernelSeed)}");
        string caseProperty = FuzzCaseEmitter.RenderDifferentialCaseProperty(
            _options.KernelClassName,
            kernelName,
            _options.InputSeed,
            caseHeader);

        var localNames = new List<string>(state.Locals.Count);
        foreach (var local in state.Locals)
        {
            localNames.Add(local.Name);
        }

        return new GeneratedKernelSpec(
            kernelName,
            kernelSeed,
            size,
            kernelMethod,
            caseProperty,
            localNames,
            state.Statements.ToArray());
    }

    /// <summary>Builds a division or remainder expression with an inline ternary guard on the divisor.</summary>
    public static string BuildGuardedDivisionOrModulo(
        FuzzScalarKind promotedKind,
        string leftExpr,
        string rightExpr,
        bool isModulo,
        bool leftIsSimpleLocal) =>
        FuzzScalarExpressionEmitter.BuildGuardedDivisionOrModulo(promotedKind, leftExpr, rightExpr, isModulo, leftIsSimpleLocal);

    /// <summary>Appends the device enum declarations used by generated kernels to <paramref name="writer"/>.</summary>
    public static void AppendEnumDeclarations(SourceWriter writer) =>
        writer.AppendLine(EnumDeclarationsBlock);

    /// <summary>
    /// Emits <paramref name="count"/> generated statements into <paramref name="writer"/> at the next control-flow depth
    /// (up to depth <c>2</c>), preserving loop-variable immutability and scoping invariants.
    /// </summary>
    internal static void EmitNestedStatements(
        KernelGenerationState state,
        SourceWriter writer,
        int count,
        bool inSwitchSection = false)
    {
        if (state.ControlFlowDepth >= 2 || count <= 0)
        {
            return;
        }

        var nestedBuffer = new List<string>(count * 2);
        var savedBuffer = state.SwapStatementsBuffer(nestedBuffer);
        bool savedInSwitch = state.InSwitchSection;
        state.ControlFlowDepth++;
        state.InSwitchSection = savedInSwitch || inSwitchSection;
        try
        {
            for (int i = 0; i < count; i++)
            {
                EmitRandomStatement(state);
            }
        }
        finally
        {
            state.InSwitchSection = savedInSwitch;
            state.ControlFlowDepth--;
            state.SwapStatementsBuffer(savedBuffer);
        }

        foreach (string stmt in nestedBuffer)
        {
            writer.AppendLine(stmt);
        }
    }

    private static void InitializeScalarLocals(KernelGenerationState state)
    {
        FuzzScalarKind[] baseTypes =
        [
            FuzzScalarKind.SByte,
            FuzzScalarKind.Byte,
            FuzzScalarKind.Int16,
            FuzzScalarKind.UInt16,
            FuzzScalarKind.Int32,
            FuzzScalarKind.UInt32,
            FuzzScalarKind.Int64,
            FuzzScalarKind.UInt64,
            FuzzScalarKind.Boolean,
            FuzzScalarKind.Char,
            FuzzScalarKind.EnumByte,
            FuzzScalarKind.EnumShort,
            FuzzScalarKind.EnumInt,
            FuzzScalarKind.EnumULong,
        ];

        var activeTypes = new List<FuzzTypeInfo>(baseTypes.Length);
        foreach (var kind in baseTypes)
        {
            activeTypes.Add(FuzzTypeInfo.Get(kind));
        }

        for (int i = activeTypes.Count - 1; i > 0; i--)
        {
            int j = state.Rng.NextBelow(i + 1);
            (activeTypes[i], activeTypes[j]) = (activeTypes[j], activeTypes[i]);
        }

        foreach (var typeInfo in activeTypes)
        {
            string varName = $"v{state.Locals.Count}";
            string initExpr = BuildInputInitializationExpression(state.Rng, typeInfo, state.Locals.Count);
            state.Statements.Add($"{typeInfo.Keyword} {varName} = {initExpr};");
            state.Locals.Add(new ScalarLocalVariable(varName, typeInfo));
        }
    }

    private static string BuildInputInitializationExpression(FuzzRandom rng, FuzzTypeInfo type, int index)
    {
        string src = (index % 3) switch { 0 => "inA[gid]", 1 => "inB[gid]", _ => "inC[gid]" };
        string alt = ((index + 1) % 3) switch { 0 => "inA[gid]", 1 => "inB[gid]", _ => "inC[gid]" };

        return type.Kind switch
        {
            FuzzScalarKind.SByte => $"(sbyte){src}",
            FuzzScalarKind.Byte => $"(byte){src}",
            FuzzScalarKind.Int16 => $"(short){src}",
            FuzzScalarKind.UInt16 => $"(ushort){src}",
            FuzzScalarKind.Int32 => src,
            FuzzScalarKind.UInt32 => $"(uint){src}",
            FuzzScalarKind.Int64 => rng.NextBool() ? $"(((long){src} << 32) ^ (uint){alt})" : $"(long){src}",
            FuzzScalarKind.UInt64 => rng.NextBool() ? $"(((ulong)(uint){src} << 32) | (uint){alt})" : $"(ulong){src}",
            FuzzScalarKind.Boolean => $"((({src} ^ {alt}) & 1) != 0)",
            FuzzScalarKind.Char => $"(char){src}",
            FuzzScalarKind.EnumByte => $"(FuzzEnumByte)(byte){src}",
            FuzzScalarKind.EnumShort => $"(FuzzEnumShort)(short){src}",
            FuzzScalarKind.EnumInt => $"(FuzzEnumInt){src}",
            FuzzScalarKind.EnumULong => $"(FuzzEnumULong)(ulong){src}",
            _ => src,
        };
    }

    private static void EmitCoreScalarStatements(KernelGenerationState state)
    {
        var rng = state.Rng;
        var int32Local = state.FindLocal(FuzzScalarKind.Int32);
        var uint32Local = state.FindLocal(FuzzScalarKind.UInt32);
        var int64Local = state.FindLocal(FuzzScalarKind.Int64);
        var uint64Local = state.FindLocal(FuzzScalarKind.UInt64);

        string shiftOp32 = rng.NextBelow(3) switch { 0 => "<<", 1 => ">>", _ => ">>>" };
        string shiftOp64 = rng.NextBelow(3) switch { 0 => "<<", 1 => ">>", _ => ">>>" };
        string dynamicShiftCount = rng.NextBool() ? "inC[gid]" : $"(inC[gid] + {rng.NextRange(32, 95)})";
        state.Statements.Add($"{int32Local.Name} = ({int32Local.Name} {shiftOp32} {dynamicShiftCount}) ^ (int)({uint32Local.Name} >>> inC[gid]);");
        state.Statements.Add($"{int64Local.Name} = ({int64Local.Name} {shiftOp64} {dynamicShiftCount}) + (long)({uint64Local.Name} << inC[gid]);");

        var (interfTarget, interfAux) = state.PickDistinctPair(FuzzScalarKind.Int32, FuzzScalarKind.Int32);
        string stepDelta = FuzzScalarExpressionEmitter.FormatLiteral(interfTarget.Type, rng.NextRange(1, 7));
        string binOp = rng.NextBelow(3) switch { 0 => "+", 1 => "-", _ => "^" };
        string three = FuzzScalarExpressionEmitter.FormatLiteral(interfTarget.Type, 3);
        state.Statements.Add(
            rng.NextBool()
                ? $"{interfAux.Name} = ((({interfTarget.Name} += {stepDelta}) {binOp} ({interfTarget.Name} * {three})));"
                : $"{interfAux.Name} = ((({interfTarget.Name} * {three}) {binOp} ({interfTarget.Name} += {stepDelta})));");

        FuzzScalarKind narrowKind = rng.NextBelow(4) switch
        {
            0 => FuzzScalarKind.SByte,
            1 => FuzzScalarKind.Byte,
            2 => FuzzScalarKind.Int16,
            _ => FuzzScalarKind.UInt16,
        };
        var narrowLocal = state.FindLocal(narrowKind);
        string narrowType = narrowLocal.Type.Keyword;
        string narrowRhs = $"(({narrowType})(({int32Local.Name} += 5) + 129))";
        string narrowCompoundOp = rng.NextBelow(4) switch { 0 => "+=", 1 => "-=", 2 => "*=", _ => "^=" };
        state.Statements.Add($"{narrowLocal.Name} {narrowCompoundOp} {narrowRhs};");
        state.Statements.Add($"{narrowLocal.Name}++;");
        state.Statements.Add($"--{narrowLocal.Name};");

        if (!state.IssueFilter.AvoidNarrowIncDecValue)
        {
            state.Statements.Add($"{int32Local.Name} = ({int32Local.Name} ^ (++{narrowLocal.Name}) ^ ({narrowLocal.Name}--));");
        }

        string signedDivExpr = FuzzScalarExpressionEmitter.BuildGuardedDivisionOrModulo(
            FuzzScalarKind.Int32, "inA[gid]", "inB[gid]", isModulo: rng.NextBool(), leftIsSimpleLocal: true);
        state.Statements.Add($"{int32Local.Name} = ({int32Local.Name} + {signedDivExpr});");

        string unsignedDivExpr = FuzzScalarExpressionEmitter.BuildGuardedDivisionOrModulo(
            FuzzScalarKind.UInt32, uint32Local.Name, "(uint)inB[gid]", isModulo: rng.NextBool(), leftIsSimpleLocal: true);
        state.Statements.Add($"{uint32Local.Name} = ({uint32Local.Name} ^ {unsignedDivExpr});");

        if (!state.IssueFilter.AvoidMsvcNegatedConstantDivision)
        {
            state.Statements.Add($"{int32Local.Name} = ({int32Local.Name} ^ ((-{int32Local.Name}) / 3));");
        }

        var enumByteLocal = state.FindLocal(FuzzScalarKind.EnumByte);
        var enumShortLocal = state.FindLocal(FuzzScalarKind.EnumShort);
        var enumIntLocal = state.FindLocal(FuzzScalarKind.EnumInt);
        state.Statements.Add($"{enumByteLocal.Name} = ~{enumByteLocal.Name} | FuzzEnumByte.Bit;");
        state.Statements.Add($"{enumShortLocal.Name} = ({enumShortLocal.Name} ^ FuzzEnumShort.Mask) & ~FuzzEnumShort.One;");
        state.Statements.Add($"{enumIntLocal.Name} = ({enumByteLocal.Name} < FuzzEnumByte.High) ? ({enumIntLocal.Name} | FuzzEnumInt.Alt) : ~{enumIntLocal.Name};");
    }

    /// <summary>
    /// One kind of random statement. A draw <c>choice</c> in [0, 100) selects the first row, in table order, whose
    /// <see cref="Below"/> exceeds it and whose <see cref="CanEmit"/> holds; a row whose condition fails passes the draw
    /// on to the next row, so the probability of a kind is <c>Below</c> minus the <c>Below</c> of the row above only
    /// where every condition holds.
    /// </summary>
    private sealed record StatementKind(
        int Below,
        Func<KernelGenerationState, ScalarLocalVariable, bool> CanEmit,
        Action<KernelGenerationState, ScalarLocalVariable> Emit);

    private static readonly Func<KernelGenerationState, ScalarLocalVariable, bool> s_always = static (_, _) => true;

    private static bool IsArithmeticTarget(KernelGenerationState state, ScalarLocalVariable target) =>
        !target.Type.IsEnum && target.Type.Kind != FuzzScalarKind.Boolean;

    private static readonly StatementKind[] s_statementKinds =
    [
        new(25, s_always, EmitAssignmentStatement),
        new(42, IsArithmeticTarget, EmitCompoundAssignmentStatement),
        new(
            52,
            s_always,
            static (state, _) => FuzzCompositeExpressionEmitter.EmitTupleDeconstructionStatement(state)),
        new(
            62,
            s_always,
            static (state, _) => FuzzCompositeExpressionEmitter.EmitNullableStatement(state)),
        new(
            70,
            s_always,
            static (state, _) => FuzzCompositeExpressionEmitter.EmitSpanStatement(state)),
        new(
            78,
            s_always,
            static (state, _) => FuzzCompositeExpressionEmitter.EmitRefTernaryStatement(state)),
        new(
            86,
            static (state, _) => state.Swarm.DeviceCallWeight > 1 && !state.InSwitchSection,
            static (state, _) => FuzzDeviceFunctionEmitter.EmitRandomDeviceFunctionCall(state)),
        new(
            92,
            static (state, _) => state.ControlFlowDepth < 2
                && (state.Swarm.ControlFlowWeight > 1 || state.Swarm.LoopWeight > 1),
            static (state, _) => EmitControlFlowStatement(state)),
        new(96, IsArithmeticTarget, EmitIncrementStatement),
        new(
            int.MaxValue,
            static (state, _) => state.ControlFlowDepth == 0,
            static (state, _) => EmitNewLocalStatement(state)),
    ];

    private static void EmitRandomStatement(KernelGenerationState state)
    {
        var rng = state.Rng;
        var target = rng.Choose(state.Locals);
        if (state.ProtectedLoopVariables.Contains(target.Name))
        {
            target = state.FindLocal(FuzzScalarKind.Int32);
        }

        int choice = rng.NextBelow(100);

        foreach (var kind in s_statementKinds)
        {
            if (choice < kind.Below && kind.CanEmit(state, target))
            {
                kind.Emit(state, target);
                return;
            }
        }

        string fallbackRhs = FuzzScalarExpressionEmitter.GenerateLeafExpression(state, target.Type);
        state.Statements.Add($"{target.Name} = {fallbackRhs};");
    }

    private static void EmitAssignmentStatement(KernelGenerationState state, ScalarLocalVariable target)
    {
        string rhs = FuzzScalarExpressionEmitter.GenerateExpression(state, target.Type, depth: 0, maxDepth: 2 + (state.Size / 3));
        if (string.Equals(rhs, target.Name, StringComparison.Ordinal))
        {
            rhs = FuzzScalarExpressionEmitter.GenerateLeafExpression(state, target.Type);
        }

        state.Statements.Add($"{target.Name} = {rhs};");
    }

    private static void EmitControlFlowStatement(KernelGenerationState state)
    {
        var rng = state.Rng;
        int cfKind = state.InSwitchSection ? rng.NextBelow(2) : rng.NextBelow(3);
        if (cfKind == 0)
        {
            FuzzControlFlowEmitter.EmitIfElseStatement(state);
        }
        else if (cfKind == 1)
        {
            FuzzControlFlowEmitter.EmitBoundedLoopStatement(state);
        }
        else
        {
            FuzzControlFlowEmitter.EmitSwitchStatementWithAcyclicGoto(state);
        }
    }

    private static void EmitIncrementStatement(KernelGenerationState state, ScalarLocalVariable target)
    {
        var rng = state.Rng;
        bool canUseIncDecValue = !target.Type.IsNarrowIntegral || !state.IssueFilter.AvoidNarrowIncDecValue;
        if (canUseIncDecValue)
        {
            string stepOp = rng.NextBool() ? "++" : "--";
            string stepExpr = rng.NextBool() ? $"{stepOp}{target.Name}" : $"{target.Name}{stepOp}";
            var receiver = state.FindLocal(target.Type.Kind);
            state.Statements.Add($"{receiver.Name} = ({target.Type.Keyword})({stepExpr});");
        }
        else
        {
            state.Statements.Add($"{target.Name}++;");
        }
    }

    private static void EmitNewLocalStatement(KernelGenerationState state)
    {
        var newType = state.Rng.Choose(FuzzTypeInfo.All);
        string newName = $"v{state.Locals.Count}";
        string init = FuzzScalarExpressionEmitter.GenerateExpression(state, newType, depth: 0, maxDepth: 2);
        state.Statements.Add($"{newType.Keyword} {newName} = {init};");
        state.Locals.Add(new ScalarLocalVariable(newName, newType));
    }

    private static void EmitCompoundAssignmentStatement(KernelGenerationState state, ScalarLocalVariable target)
    {
        var rng = state.Rng;
        string[] ops = ["+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<=", ">>=", ">>>="];
        string op = ops[rng.NextBelow(ops.Length)];

        if (op is "<<=" or ">>=" or ">>>=")
        {
            string countExpr = FuzzScalarExpressionEmitter.GenerateShiftCountExpression(state, depth: 1, maxDepth: 2);
            state.Statements.Add($"{target.Name} {op} {countExpr};");
            return;
        }

        if (op is "/=" or "%=")
        {
            string rawDivisor = FuzzScalarExpressionEmitter.GenerateLeafExpression(state, target.Type);
            string guardedDivisor = FuzzScalarExpressionEmitter.WrapDivisorForCompoundAssignment(target.Name, target.Type, rawDivisor);
            state.Statements.Add($"{target.Name} {op} {guardedDivisor};");
            return;
        }

        string rhs = FuzzScalarExpressionEmitter.GenerateExpression(state, target.Type, depth: 1, maxDepth: 2);
        string typedRhs = FuzzScalarExpressionEmitter.EnsureType(rhs, target.Type);
        state.Statements.Add($"{target.Name} {op} {typedRhs};");
    }

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
        IReadOnlyList<string> extraDeviceFunctions)
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

            for (int i = 0; i < kernels.Count; i++)
            {
                if (i > 0)
                {
                    writer.AppendLine();
                }

                writer.AppendLine(kernels[i].KernelMethodSource);
            }
        }

        return writer.ToString();
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
