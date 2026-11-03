using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Deterministic scalar kernel generator for G# differential fuzzing.
/// Generates type-safe, UB-free C# GPU kernels and companion <c>DifferentialCase</c> definitions
/// exercising all 8 integer types, <see langword="bool"/>, <see langword="char"/>, and device <see langword="enum"/> types
/// with raw operators, inline division/overflow guards, left-to-right interference bias, and FNV-1a 64-bit checksum folding.
/// </summary>
public sealed class ScalarFuzzGenerator
{
    /// <summary>The 64-bit FNV-1a offset basis.</summary>
    public const ulong FnvOffsetBasis = 14695981039346656037UL;

    /// <summary>The 64-bit FNV-1a prime multiplier.</summary>
    public const ulong FnvPrime = 1099511628211UL;

    private static readonly sbyte[] s_sbyteLiterals = [0, 1, -1, 2, -2, 7, -8, 63, -64, 126, sbyte.MaxValue, sbyte.MinValue + 1, sbyte.MinValue];
    private static readonly byte[] s_byteLiterals = [0, 1, 2, 3, 7, 15, 31, 63, 127, 128, 254, byte.MaxValue];
    private static readonly short[] s_shortLiterals = [0, 1, -1, 2, -2, 127, -128, 255, 256, 32766, short.MaxValue, short.MinValue + 1, short.MinValue];
    private static readonly ushort[] s_ushortLiterals = [0, 1, 2, 7, 127, 128, 255, 256, 32767, 32768, 65534, ushort.MaxValue];
    private static readonly int[] s_intLiterals = [0, 1, -1, 2, -2, 3, -3, 7, 15, 31, 32, 33, 63, 64, -31, -32, 255, 65535, 0x40000000, int.MaxValue, int.MinValue + 1, int.MinValue];
    private static readonly uint[] s_uintLiterals = [0U, 1U, 2U, 3U, 7U, 15U, 31U, 32U, 33U, 63U, 64U, 255U, 65535U, 0x7FFFFFFFU, 0x80000000U, uint.MaxValue];
    private static readonly long[] s_longLiterals = [0L, 1L, -1L, 2L, -2L, 31L, 32L, 63L, 64L, int.MaxValue, int.MinValue, (long)uint.MaxValue, long.MaxValue, long.MinValue + 1L, long.MinValue];
    private static readonly ulong[] s_ulongLiterals = [0UL, 1UL, 2UL, 3UL, 31UL, 32UL, 63UL, 64UL, 255UL, uint.MaxValue, (ulong)long.MaxValue, 0x8000000000000000UL, ulong.MaxValue];
    private static readonly char[] s_charLiterals = ['\0', '\u0001', '0', 'A', 'Z', 'a', 'z', '\u007F', '\u0080', '\u00FF', '\u7FFF', '\u8000', '\uFFFE', '\uFFFF'];

    private static readonly string[] s_enumMemberNames = ["Zero", "One", "Two", "Bit", "High", "Mask", "Alt", "Edge"];

    private readonly FuzzOptions _options;

    /// <summary>Initializes a new instance of the <see cref="ScalarFuzzGenerator"/> class.</summary>
    public ScalarFuzzGenerator(FuzzOptions? options = null)
    {
        _options = options ?? new FuzzOptions();
    }

    /// <summary>
    /// Generates a full batch of <see cref="FuzzOptions.BatchSize"/> kernels and their companion <c>DifferentialCase</c> definitions.
    /// </summary>
    public GeneratedBatch GenerateBatch()
    {
        int batchSize = Math.Clamp(_options.BatchSize, 1, 256);
        var masterRng = new FuzzRandom(_options.FuzzSeed);
        var kernels = new List<GeneratedKernelSpec>(batchSize);

        for (int i = 0; i < batchSize; i++)
        {
            ulong kernelSeed = masterRng.NextUInt64();
            int size = _options.BatchSize == 1
                ? Math.Clamp(_options.Size, 1, 8)
                : 1 + (i * 8 / batchSize);
            size = Math.Clamp(size, 1, 8);
            string kernelName = string.Create(CultureInfo.InvariantCulture, $"Kernel_{i:D2}");
            kernels.Add(GenerateSingleKernel(kernelName, kernelSeed, size));
        }

        string kernelsSource = BuildKernelsCompilationUnit(kernels);
        string casesSource = BuildCasesCompilationUnit(kernels);

        // Verify with Roslyn syntax parser that the emitted C# has zero syntax diagnostics.
        ValidateSyntaxTree(kernelsSource, "FuzzBatchKernels.cu.cs");
        ValidateSyntaxTree(casesSource, "FuzzBatchCases.cs");

        return new GeneratedBatch(_options.FuzzSeed, _options.InputSeed, kernelsSource, casesSource, kernels);
    }

    /// <summary>
    /// Generates a single kernel and its companion <c>DifferentialCase</c> from an explicit <paramref name="kernelSeed"/> and <paramref name="size"/>.
    /// </summary>
    public GeneratedKernelSpec GenerateSingleKernel(string kernelName, ulong kernelSeed, int size = 4)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kernelName);
        size = Math.Clamp(size, 1, 8);

        var rng = new FuzzRandom(kernelSeed);
        double interferenceProb = Math.Clamp(_options.InterferenceProbability, 0.25, 0.60);
        var state = new KernelGenerationState(rng, size, interferenceProb);

        // 1. Seed initial local variables of diverse scalar types from thread-specific inputs.
        InitializeLocals(state);

        // 2. Generate mandatory coverage patterns so shifts, interfering evaluations, narrow compound assignments,
        //    enums, and guarded divisions are guaranteed to appear in every kernel while varied by the RNG.
        EmitCoreFeatureStatements(state);

        // 3. Generate size-scaled random statements and intermediate checksum folds.
        int extraStatements = 3 + size * 2;
        for (int s = 0; s < extraStatements; s++)
        {
            EmitRandomStatement(state);
            if (s % 3 == 2)
            {
                EmitChecksumFoldSubset(state);
            }
        }

        // 4. Final checksum fold of all in-scope locals.
        EmitChecksumFoldAll(state);

        string kernelMethod = RenderKernelMethod(kernelName, kernelSeed, size, state);
        string caseProperty = RenderCaseProperty(kernelName, kernelSeed);

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

    private static void InitializeLocals(KernelGenerationState state)
    {
        // Always include the core scalar types so every kernel can combine them freely.
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

        // Permute the declaration order deterministically from the seed, or keep ordered list for deterministic replay.
        var activeTypes = new List<FuzzTypeInfo>(baseTypes.Length);
        foreach (var kind in baseTypes)
        {
            activeTypes.Add(FuzzTypeInfo.Get(kind));
        }

        // Deterministically shuffle using state.Rng so different seeds declare variables in different orders.
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
            state.Locals.Add(new LocalVariable(varName, typeInfo));
        }
    }

    private static string BuildInputInitializationExpression(FuzzRandom rng, FuzzTypeInfo type, int index)
    {
        string src = (index % 3) switch
        {
            0 => "inA[gid]",
            1 => "inB[gid]",
            _ => "inC[gid]",
        };
        string alt = ((index + 1) % 3) switch
        {
            0 => "inA[gid]",
            1 => "inB[gid]",
            _ => "inC[gid]",
        };

        return type.Kind switch
        {
            FuzzScalarKind.SByte => $"(sbyte){src}",
            FuzzScalarKind.Byte => $"(byte){src}",
            FuzzScalarKind.Int16 => $"(short){src}",
            FuzzScalarKind.UInt16 => $"(ushort){src}",
            FuzzScalarKind.Int32 => src,
            FuzzScalarKind.UInt32 => $"(uint){src}",
            FuzzScalarKind.Int64 => rng.NextBool()
                ? $"(((long){src} << 32) ^ (uint){alt})"
                : $"(long){src}",
            FuzzScalarKind.UInt64 => rng.NextBool()
                ? $"(((ulong)(uint){src} << 32) | (uint){alt})"
                : $"(ulong){src}",
            FuzzScalarKind.Boolean => $"((({src} ^ {alt}) & 1) != 0)",
            FuzzScalarKind.Char => $"(char){src}",
            FuzzScalarKind.EnumByte => $"(FuzzEnumByte)(byte){src}",
            FuzzScalarKind.EnumShort => $"(FuzzEnumShort)(short){src}",
            FuzzScalarKind.EnumInt => $"(FuzzEnumInt){src}",
            FuzzScalarKind.EnumULong => $"(FuzzEnumULong)(ulong){src}",
            _ => src,
        };
    }

    private static void EmitCoreFeatureStatements(KernelGenerationState state)
    {
        var rng = state.Rng;

        // (A) Shift expression with raw dynamic shift count (including negative or >= width counts in inC[gid]).
        var int32Local = FindLocal(state, FuzzScalarKind.Int32);
        var uint32Local = FindLocal(state, FuzzScalarKind.UInt32);
        var int64Local = FindLocal(state, FuzzScalarKind.Int64);
        var uint64Local = FindLocal(state, FuzzScalarKind.UInt64);

        string shiftOp32 = rng.NextBelow(3) switch
        {
            0 => "<<",
            1 => ">>",
            _ => ">>>",
        };
        string shiftOp64 = rng.NextBelow(3) switch
        {
            0 => "<<",
            1 => ">>",
            _ => ">>>",
        };
        string dynamicShiftCount = rng.NextBool() ? "inC[gid]" : $"(inC[gid] + {rng.NextRange(32, 95)})";
        state.Statements.Add($"{int32Local.Name} = ({int32Local.Name} {shiftOp32} {dynamicShiftCount}) ^ (int)({uint32Local.Name} >>> inC[gid]);");
        state.Statements.Add($"{int64Local.Name} = ({int64Local.Name} {shiftOp64} {dynamicShiftCount}) + (long)({uint64Local.Name} << inC[gid]);");

        // (B) Left-to-right operand interference on binary expression (mutating a local in one operand and reading it in the sibling).
        var (interfTarget, interfAux) = PickDistinctPair(state, FuzzScalarKind.Int32, FuzzScalarKind.Int64);
        string stepDelta = FormatLiteral(interfTarget.Type, rng.NextRange(1, 7));
        string binOp = rng.NextBelow(3) switch
        {
            0 => "+",
            1 => "-",
            _ => "^",
        };
        if (rng.NextBool())
        {
            // Mutate on left, read on right: ((v += delta) op (v * 3))
            string three = FormatLiteral(interfTarget.Type, 3);
            state.Statements.Add($"{interfAux.Name} = ((({interfTarget.Name} += {stepDelta}) {binOp} ({interfTarget.Name} * {three})));");
        }
        else
        {
            // Read on left, mutate on right: ((v * 3) op (v += delta))
            string three = FormatLiteral(interfTarget.Type, 3);
            state.Statements.Add($"{interfAux.Name} = ((({interfTarget.Name} * {three}) {binOp} ({interfTarget.Name} += {stepDelta})));");
        }

        // (C) Narrow integer compound assignment with truncation & interference (`sbyte`, `byte`, `short`, `ushort`, `char`).
        FuzzScalarKind narrowKind = rng.NextBelow(4) switch
        {
            0 => FuzzScalarKind.SByte,
            1 => FuzzScalarKind.Byte,
            2 => FuzzScalarKind.Int16,
            _ => FuzzScalarKind.UInt16,
        };
        var narrowLocal = FindLocal(state, narrowKind);
        string narrowType = narrowLocal.Type.Keyword;
        string narrowRhs = $"(({narrowType})(({int32Local.Name} += 5) + 129))";
        string narrowCompoundOp = rng.NextBelow(4) switch
        {
            0 => "+=",
            1 => "-=",
            2 => "*=",
            _ => "^=",
        };
        state.Statements.Add($"{narrowLocal.Name} {narrowCompoundOp} {narrowRhs};");
        state.Statements.Add($"{narrowLocal.Name}++;");
        state.Statements.Add($"--{narrowLocal.Name};");

        // (D) Signed & unsigned division/remainder with inline ternary guard.
        string signedDivExpr = BuildGuardedDivisionOrModulo(
            FuzzScalarKind.Int32,
            "inA[gid]",
            "inB[gid]",
            isModulo: rng.NextBool(),
            leftIsSimpleLocal: true);
        state.Statements.Add($"{int32Local.Name} = ({int32Local.Name} + {signedDivExpr});");

        string unsignedDivExpr = BuildGuardedDivisionOrModulo(
            FuzzScalarKind.UInt32,
            uint32Local.Name,
            "(uint)inB[gid]",
            isModulo: rng.NextBool(),
            leftIsSimpleLocal: true);
        state.Statements.Add($"{uint32Local.Name} = ({uint32Local.Name} ^ {unsignedDivExpr});");

        // (E) Narrow and wide enum bitwise and comparison operations.
        var enumByteLocal = FindLocal(state, FuzzScalarKind.EnumByte);
        var enumShortLocal = FindLocal(state, FuzzScalarKind.EnumShort);
        var enumIntLocal = FindLocal(state, FuzzScalarKind.EnumInt);
        state.Statements.Add($"{enumByteLocal.Name} = ~{enumByteLocal.Name} | FuzzEnumByte.Bit;");
        state.Statements.Add($"{enumShortLocal.Name} = ({enumShortLocal.Name} ^ FuzzEnumShort.Mask) & ~FuzzEnumShort.One;");
        state.Statements.Add($"{enumIntLocal.Name} = ({enumByteLocal.Name} < FuzzEnumByte.High) ? ({enumIntLocal.Name} | FuzzEnumInt.Alt) : ~{enumIntLocal.Name};");
    }

    private static void EmitRandomStatement(KernelGenerationState state)
    {
        var rng = state.Rng;
        var target = rng.Choose(state.Locals);
        int choice = rng.NextBelow(100);

        if (choice < 45)
        {
            // Simple assignment of a nested expression
            string rhs = GenerateExpression(state, target.Type, depth: 0, maxDepth: 2 + (state.Size / 3));
            if (string.Equals(rhs, target.Name, StringComparison.Ordinal))
            {
                rhs = GenerateLeafExpression(state, target.Type);
            }

            state.Statements.Add($"{target.Name} = {rhs};");
        }
        else if (choice < 80 && !target.Type.IsEnum && target.Type.Kind != FuzzScalarKind.Boolean)
        {
            // Compound assignment
            EmitCompoundAssignmentStatement(state, target);
        }
        else if (choice < 92 && !target.Type.IsEnum && target.Type.Kind != FuzzScalarKind.Boolean)
        {
            // Pre/post increment or decrement combined with assignment
            bool isPre = rng.NextBool();
            string stepOp = rng.NextBool() ? "++" : "--";
            string stepExpr = isPre ? $"{stepOp}{target.Name}" : $"{target.Name}{stepOp}";
            var receiver = FindLocal(state, target.Type.Kind);
            state.Statements.Add($"{receiver.Name} = ({target.Type.Keyword})({stepExpr});");
        }
        else
        {
            // Declare a new derived local variable with explicit type (never unsuffixed `var`, avoiding GSH0024)
            var newType = rng.Choose(FuzzTypeInfo.All);
            string newName = $"v{state.Locals.Count}";
            string init = GenerateExpression(state, newType, depth: 0, maxDepth: 2);
            state.Statements.Add($"{newType.Keyword} {newName} = {init};");
            state.Locals.Add(new LocalVariable(newName, newType));
        }
    }

    private static void EmitCompoundAssignmentStatement(KernelGenerationState state, LocalVariable target)
    {
        var rng = state.Rng;
        string[] ops = ["+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<=", ">>=", ">>>="];
        string op = ops[rng.NextBelow(ops.Length)];

        if (op is "<<=" or ">>=" or ">>>=")
        {
            string countExpr = GenerateShiftCountExpression(state, depth: 1, maxDepth: 2);
            state.Statements.Add($"{target.Name} {op} {countExpr};");
            return;
        }

        if (op is "/=" or "%=")
        {
            string rawDivisor = GenerateLeafExpression(state, target.Type);
            string guardedDivisor = WrapDivisorForCompoundAssignment(target.Name, target.Type, rawDivisor);
            state.Statements.Add($"{target.Name} {op} {guardedDivisor};");
            return;
        }

        string rhs = GenerateExpression(state, target.Type, depth: 1, maxDepth: 2);
        string typedRhs = EnsureType(rhs, target.Type);
        state.Statements.Add($"{target.Name} {op} {typedRhs};");
    }

    private static string GenerateExpression(KernelGenerationState state, FuzzTypeInfo targetType, int depth, int maxDepth)
    {
        var rng = state.Rng;
        if (depth >= maxDepth || rng.Chance(0.25))
        {
            return GenerateLeafExpression(state, targetType);
        }

        return targetType.Kind switch
        {
            FuzzScalarKind.Boolean => GenerateBooleanExpression(state, depth, maxDepth),
            _ when targetType.IsEnum => GenerateEnumExpression(state, targetType, depth, maxDepth),
            _ => GenerateIntegralExpression(state, targetType, depth, maxDepth),
        };
    }

    private static string GenerateLeafExpression(KernelGenerationState state, FuzzTypeInfo targetType)
    {
        var rng = state.Rng;
        var sameTypeLocals = new List<LocalVariable>();
        foreach (var local in state.Locals)
        {
            if (local.Type.Kind == targetType.Kind)
            {
                sameTypeLocals.Add(local);
            }
        }

        if (rng.Chance(0.65))
        {
            if (sameTypeLocals.Count > 0 && rng.Chance(0.75))
            {
                return rng.Choose(sameTypeLocals).Name;
            }

            var anyLocal = rng.Choose(state.Locals);
            return CastBetweenTypes(anyLocal.Name, anyLocal.Type, targetType);
        }

        // Mix a boundary literal with an in-scope local variable so boundary values are exercised without
        // forming a pure compile-time constant expression (which could trigger CS0220/CS0221 without `unchecked`).
        string lit = GenerateRandomLiteral(rng, targetType);
        if (sameTypeLocals.Count > 0)
        {
            string localName = rng.Choose(sameTypeLocals).Name;
            return targetType.Kind switch
            {
                FuzzScalarKind.Boolean => $"({localName} ^ {lit})",
                _ when targetType.IsEnum => rng.NextBool() ? lit : $"({localName} ^ {lit})",
                FuzzScalarKind.Int32 or FuzzScalarKind.UInt32 or FuzzScalarKind.Int64 or FuzzScalarKind.UInt64 =>
                    $"({localName} ^ {lit})",
                _ => $"(({targetType.Keyword})({localName} ^ {lit}))",
            };
        }

        var fallbackLocal = rng.Choose(state.Locals);
        return CastBetweenTypes(fallbackLocal.Name, fallbackLocal.Type, targetType);
    }

    private static string GenerateBooleanExpression(KernelGenerationState state, int depth, int maxDepth)
    {
        var rng = state.Rng;
        int kind = rng.NextBelow(4);
        switch (kind)
        {
            case 0:
            {
                // Logical && or ||
                string op = rng.NextBool() ? "&&" : "||";
                string left = GenerateExpression(state, FuzzTypeInfo.Get(FuzzScalarKind.Boolean), depth + 1, maxDepth);
                string right = GenerateExpression(state, FuzzTypeInfo.Get(FuzzScalarKind.Boolean), depth + 1, maxDepth);
                return $"({left} {op} {right})";
            }
            case 1:
            {
                // Logical negation !
                string inner = GenerateExpression(state, FuzzTypeInfo.Get(FuzzScalarKind.Boolean), depth + 1, maxDepth);
                return $"(!{inner})";
            }
            case 2:
            {
                // Relational or equality comparison over an integral or enum type
                var cmpType = rng.Choose(FuzzTypeInfo.IntegralTypes);
                string[] cmpOps = ["==", "!=", "<", "<=", ">", ">="];
                string op = cmpOps[rng.NextBelow(cmpOps.Length)];
                var (left, right) = GeneratePossiblyInterferingPair(state, cmpType, depth + 1, maxDepth);
                if (string.Equals(left, right, StringComparison.Ordinal))
                {
                    right = GenerateRandomLiteral(rng, cmpType);
                }

                return $"({left} {op} {right})";
            }
            default:
            {
                // Conditional ternary returning bool
                string cond = GenerateExpression(state, FuzzTypeInfo.Get(FuzzScalarKind.Boolean), depth + 1, maxDepth);
                string whenTrue = GenerateExpression(state, FuzzTypeInfo.Get(FuzzScalarKind.Boolean), depth + 1, maxDepth);
                string whenFalse = GenerateExpression(state, FuzzTypeInfo.Get(FuzzScalarKind.Boolean), depth + 1, maxDepth);
                return $"({cond} ? {whenTrue} : {whenFalse})";
            }
        }
    }

    private static string GenerateEnumExpression(KernelGenerationState state, FuzzTypeInfo enumType, int depth, int maxDepth)
    {
        var rng = state.Rng;
        int kind = rng.NextBelow(4);
        switch (kind)
        {
            case 0:
            {
                // Bitwise &, |, ^ on enum
                string op = rng.NextBelow(3) switch
                {
                    0 => "&",
                    1 => "|",
                    _ => "^",
                };
                string left = GenerateExpression(state, enumType, depth + 1, maxDepth);
                string right = GenerateExpression(state, enumType, depth + 1, maxDepth);
                return $"({left} {op} {right})";
            }
            case 1:
            {
                // Bitwise ~ on enum
                string inner = GenerateExpression(state, enumType, depth + 1, maxDepth);
                return $"(~{inner})";
            }
            case 2:
            {
                // Ternary on enum
                string cond = GenerateExpression(state, FuzzTypeInfo.Get(FuzzScalarKind.Boolean), depth + 1, maxDepth);
                string whenTrue = GenerateExpression(state, enumType, depth + 1, maxDepth);
                string whenFalse = GenerateExpression(state, enumType, depth + 1, maxDepth);
                return $"({cond} ? {whenTrue} : {whenFalse})";
            }
            default:
            {
                // Explicit cast from underlying integral expression
                var underlying = FuzzTypeInfo.Get(enumType.UnderlyingKind);
                string inner = GenerateExpression(state, underlying, depth + 1, maxDepth);
                return $"(({enumType.Keyword}){inner})";
            }
        }
    }

    private static string GenerateIntegralExpression(KernelGenerationState state, FuzzTypeInfo targetType, int depth, int maxDepth)
    {
        var rng = state.Rng;
        var promotedKind = FuzzTypeInfo.PromoteUnary(targetType.Kind);
        var promotedType = FuzzTypeInfo.Get(promotedKind);

        int category = rng.NextBelow(100);
        string resultExpr;

        if (category < 35)
        {
            // Binary arithmetic (+, -, *) or bitwise (&, |, ^) with optional interference bias
            string[] ops = ["+", "-", "*", "&", "|", "^"];
            string op = ops[rng.NextBelow(ops.Length)];
            var (left, right) = GeneratePossiblyInterferingPair(state, promotedType, depth + 1, maxDepth);
            resultExpr = $"({left} {op} {right})";
        }
        else if (category < 50)
        {
            // Division or modulo with inline UB-avoidance guard (divisor is side-effect-free so the inline ternary guard evaluates deterministically)
            bool isModulo = rng.NextBool();
            string left = GenerateExpression(state, promotedType, depth + 1, maxDepth);
            string right = GenerateLeafExpression(state, promotedType);
            resultExpr = BuildGuardedDivisionOrModulo(promotedKind, left, right, isModulo, leftIsSimpleLocal: IsSimpleIdentifier(left));
        }
        else if (category < 65)
        {
            // Shift <<, >>, >>> with arbitrary (including negative / >= width) int shift count
            string shiftOp = rng.NextBelow(3) switch
            {
                0 => "<<",
                1 => ">>",
                _ => ">>>",
            };
            string left = GenerateExpression(state, promotedType, depth + 1, maxDepth);
            string count = GenerateShiftCountExpression(state, depth + 1, maxDepth);
            resultExpr = $"({left} {shiftOp} {count})";
        }
        else if (category < 78)
        {
            // Unary +, -, ~
            string unaryOp = promotedType.IsSigned
                ? (rng.NextBelow(3) switch { 0 => "+", 1 => "-", _ => "~" })
                : (rng.NextBool() ? "+" : "~");
            string operand = GenerateExpression(state, promotedType, depth + 1, maxDepth);
            resultExpr = $"({unaryOp}{operand})";
        }
        else if (category < 90)
        {
            // Conditional ternary c ? a : b
            string cond = GenerateExpression(state, FuzzTypeInfo.Get(FuzzScalarKind.Boolean), depth + 1, maxDepth);
            var (whenTrue, whenFalse) = GeneratePossiblyInterferingPair(state, targetType, depth + 1, maxDepth);
            return $"({cond} ? {EnsureType(whenTrue, targetType)} : {EnsureType(whenFalse, targetType)})";
        }
        else
        {
            // Inline mutation expression (++v, v--, or (v += expr))
            var mutTarget = FindLocal(state, targetType.Kind);
            if (rng.NextBool())
            {
                string stepOp = rng.NextBool() ? "++" : "--";
                resultExpr = rng.NextBool() ? $"({stepOp}{mutTarget.Name})" : $"({mutTarget.Name}{stepOp})";
                return EnsureType(resultExpr, targetType);
            }

            string rhs = GenerateLeafExpression(state, targetType);
            resultExpr = $"({mutTarget.Name} += {EnsureType(rhs, targetType)})";
            return EnsureType(resultExpr, targetType);
        }

        return EnsureType(resultExpr, targetType);
    }

    private static (string Left, string Right) GeneratePossiblyInterferingPair(
        KernelGenerationState state,
        FuzzTypeInfo operandType,
        int depth,
        int maxDepth)
    {
        var rng = state.Rng;
        if (rng.Chance(state.InterferenceProbability))
        {
            var victim = FindLocalOrNull(state, operandType.Kind);
            if (victim is not null)
            {
                string mutation = BuildInlineMutation(rng, victim);
                string siblingRead = GenerateExpressionWithGuaranteedRead(state, victim, operandType, depth, maxDepth);
                return rng.NextBool()
                    ? (mutation, siblingRead)
                    : (siblingRead, mutation);
            }
        }

        string left = GenerateExpression(state, operandType, depth, maxDepth);
        string right = GenerateExpression(state, operandType, depth, maxDepth);
        return (left, right);
    }

    private static string BuildInlineMutation(FuzzRandom rng, LocalVariable victim)
    {
        int mode = rng.NextBelow(4);
        string delta = GenerateRandomLiteral(rng, victim.Type);
        return mode switch
        {
            0 => $"(++{victim.Name})",
            1 => $"({victim.Name}++)",
            2 => $"(--{victim.Name})",
            _ => $"({victim.Name} += {EnsureType(delta, victim.Type)})",
        };
    }

    private static string GenerateExpressionWithGuaranteedRead(
        KernelGenerationState state,
        LocalVariable victim,
        FuzzTypeInfo targetType,
        int depth,
        int maxDepth)
    {
        var rng = state.Rng;
        string other = GenerateLeafExpression(state, targetType);
        string victimAsTarget = CastBetweenTypes(victim.Name, victim.Type, targetType);
        string op = rng.NextBelow(3) switch
        {
            0 => "+",
            1 => "-",
            _ => "^",
        };
        return EnsureType($"({victimAsTarget} {op} {other})", targetType);
    }

    private static string GenerateShiftCountExpression(KernelGenerationState state, int depth, int maxDepth)
    {
        var rng = state.Rng;
        int mode = rng.NextBelow(3);
        if (mode == 0)
        {
            // Direct dynamic shift count from input buffer (covers negative and >= 32/64 boundary table values on seed 0)
            return "inC[gid]";
        }

        if (mode == 1)
        {
            var intLocal = FindLocal(state, FuzzScalarKind.Int32);
            return intLocal.Name;
        }

        string expr = GenerateExpression(state, FuzzTypeInfo.Get(FuzzScalarKind.Int32), depth, maxDepth);
        return $"(int)({expr})";
    }

    /// <summary>
    /// Builds a division or remainder expression with an inline ternary guard on the divisor.
    /// Keeps the raw <c>/</c> or <c>%</c> operator directly in the AST without wrapping in a helper call,
    /// while preventing <see cref="DivideByZeroException"/> and signed <c>MinValue / -1</c> <see cref="OverflowException"/>.
    /// </summary>
    public static string BuildGuardedDivisionOrModulo(
        FuzzScalarKind promotedKind,
        string leftExpr,
        string rightExpr,
        bool isModulo,
        bool leftIsSimpleLocal)
    {
        string op = isModulo ? "%" : "/";
        return promotedKind switch
        {
            FuzzScalarKind.UInt32 =>
                $"({leftExpr} {op} (({rightExpr}) == 0U ? 1U : ({rightExpr})))",
            FuzzScalarKind.UInt64 =>
                $"({leftExpr} {op} (({rightExpr}) == 0UL ? 1UL : ({rightExpr})))",
            FuzzScalarKind.Int64 => leftIsSimpleLocal
                ? $"({leftExpr} {op} ((({rightExpr}) == 0L || (({leftExpr}) == long.MinValue && ({rightExpr}) == -1L)) ? 1L : ({rightExpr})))"
                : $"({leftExpr} {op} ((({rightExpr}) == 0L || ({rightExpr}) == -1L) ? 2L : ({rightExpr})))",
            _ => leftIsSimpleLocal
                ? $"({leftExpr} {op} ((({rightExpr}) == 0 || (({leftExpr}) == int.MinValue && ({rightExpr}) == -1)) ? 1 : ({rightExpr})))"
                : $"({leftExpr} {op} ((({rightExpr}) == 0 || ({rightExpr}) == -1) ? 2 : ({rightExpr})))",
        };
    }

    private static string WrapDivisorForCompoundAssignment(string targetName, FuzzTypeInfo targetType, string rawDivisor)
    {
        string typedDivisor = EnsureType(rawDivisor, targetType);
        return targetType.Kind switch
        {
            FuzzScalarKind.UInt32 =>
                $"(({typedDivisor}) == 0U ? 1U : ({typedDivisor}))",
            FuzzScalarKind.UInt64 =>
                $"(({typedDivisor}) == 0UL ? 1UL : ({typedDivisor}))",
            FuzzScalarKind.Byte or FuzzScalarKind.UInt16 or FuzzScalarKind.Char =>
                $"(({typedDivisor}) == ({targetType.Keyword})0 ? ({targetType.Keyword})1 : ({typedDivisor}))",
            FuzzScalarKind.Int64 =>
                $"((({typedDivisor}) == 0L || ({targetName} == long.MinValue && ({typedDivisor}) == -1L)) ? 1L : ({typedDivisor}))",
            FuzzScalarKind.Int32 =>
                $"((({typedDivisor}) == 0 || ({targetName} == int.MinValue && ({typedDivisor}) == -1)) ? 1 : ({typedDivisor}))",
            _ =>
                // sbyte and short compound /= and %= promote to int; guard against 0 and -1 (which could overflow if narrowed back to sbyte.MinValue / short.MinValue)
                $"((({typedDivisor}) == ({targetType.Keyword})0 || ({typedDivisor}) == ({targetType.Keyword})(-1)) ? ({targetType.Keyword})1 : ({typedDivisor}))",
        };
    }

    private static void EmitChecksumFoldSubset(KernelGenerationState state)
    {
        var rng = state.Rng;
        int count = Math.Min(3, state.Locals.Count);
        for (int i = 0; i < count; i++)
        {
            var local = rng.Choose(state.Locals);
            EmitChecksumFoldSingle(state, local);
        }
    }

    private static void EmitChecksumFoldAll(KernelGenerationState state)
    {
        foreach (var local in state.Locals)
        {
            EmitChecksumFoldSingle(state, local);
        }
    }

    private static void EmitChecksumFoldSingle(KernelGenerationState state, LocalVariable local)
    {
        ulong siteId = ++state.ChecksumSiteId;
        string bitsExpr = local.Type.Kind switch
        {
            FuzzScalarKind.Boolean => $"({local.Name} ? 1UL : 0UL)",
            _ => $"((ulong){local.Name})",
        };
        state.Statements.Add(
            string.Create(
                CultureInfo.InvariantCulture,
                $"hash = (hash ^ ({bitsExpr} + {siteId}UL)) * {FnvPrime}UL;"));
    }

    private static string EnsureType(string expr, FuzzTypeInfo targetType)
    {
        return targetType.Kind switch
        {
            FuzzScalarKind.SByte or FuzzScalarKind.Byte or FuzzScalarKind.Int16 or FuzzScalarKind.UInt16 or FuzzScalarKind.Char =>
                $"(({targetType.Keyword})({expr}))",
            FuzzScalarKind.Int32 => $"((int)({expr}))",
            FuzzScalarKind.UInt32 => $"((uint)({expr}))",
            FuzzScalarKind.Int64 => $"((long)({expr}))",
            FuzzScalarKind.UInt64 => $"((ulong)({expr}))",
            FuzzScalarKind.EnumByte or FuzzScalarKind.EnumShort or FuzzScalarKind.EnumInt or FuzzScalarKind.EnumULong =>
                $"(({targetType.Keyword})({expr}))",
            _ => expr,
        };
    }

    private static string CastBetweenTypes(string expr, FuzzTypeInfo fromType, FuzzTypeInfo toType)
    {
        if (fromType.Kind == toType.Kind)
        {
            return expr;
        }

        if (toType.Kind == FuzzScalarKind.Boolean)
        {
            if (fromType.IsEnum)
            {
                return $"({expr} != {fromType.Keyword}.Zero)";
            }

            return $"(({expr}) != {FormatLiteral(fromType, 0)})";
        }

        if (fromType.Kind == FuzzScalarKind.Boolean)
        {
            string one = toType.IsEnum ? $"{toType.Keyword}.One" : FormatLiteral(toType, 1);
            string zero = toType.IsEnum ? $"{toType.Keyword}.Zero" : FormatLiteral(toType, 0);
            return $"({expr} ? {one} : {zero})";
        }

        return $"(({toType.Keyword})({expr}))";
    }

    private static string GenerateRandomLiteral(FuzzRandom rng, FuzzTypeInfo type)
    {
        return type.Kind switch
        {
            FuzzScalarKind.SByte => FormatSByte(s_sbyteLiterals[rng.NextBelow(s_sbyteLiterals.Length)]),
            FuzzScalarKind.Byte => FormatByte(s_byteLiterals[rng.NextBelow(s_byteLiterals.Length)]),
            FuzzScalarKind.Int16 => FormatInt16(s_shortLiterals[rng.NextBelow(s_shortLiterals.Length)]),
            FuzzScalarKind.UInt16 => FormatUInt16(s_ushortLiterals[rng.NextBelow(s_ushortLiterals.Length)]),
            FuzzScalarKind.Int32 => FormatInt32(s_intLiterals[rng.NextBelow(s_intLiterals.Length)]),
            FuzzScalarKind.UInt32 => FormatUInt32(s_uintLiterals[rng.NextBelow(s_uintLiterals.Length)]),
            FuzzScalarKind.Int64 => FormatInt64(s_longLiterals[rng.NextBelow(s_longLiterals.Length)]),
            FuzzScalarKind.UInt64 => FormatUInt64(s_ulongLiterals[rng.NextBelow(s_ulongLiterals.Length)]),
            FuzzScalarKind.Boolean => rng.NextBool() ? "true" : "false",
            FuzzScalarKind.Char => FormatChar(s_charLiterals[rng.NextBelow(s_charLiterals.Length)]),
            FuzzScalarKind.EnumByte or FuzzScalarKind.EnumShort or FuzzScalarKind.EnumInt or FuzzScalarKind.EnumULong =>
                $"{type.Keyword}.{s_enumMemberNames[rng.NextBelow(s_enumMemberNames.Length)]}",
            _ => "0",
        };
    }

    private static string FormatLiteral(FuzzTypeInfo type, int smallValue) => type.Kind switch
    {
        FuzzScalarKind.SByte => FormatSByte(unchecked((sbyte)smallValue)),
        FuzzScalarKind.Byte => FormatByte(unchecked((byte)smallValue)),
        FuzzScalarKind.Int16 => FormatInt16(unchecked((short)smallValue)),
        FuzzScalarKind.UInt16 => FormatUInt16(unchecked((ushort)smallValue)),
        FuzzScalarKind.Int32 => FormatInt32(smallValue),
        FuzzScalarKind.UInt32 => FormatUInt32(unchecked((uint)smallValue)),
        FuzzScalarKind.Int64 => FormatInt64(smallValue),
        FuzzScalarKind.UInt64 => FormatUInt64(unchecked((ulong)smallValue)),
        FuzzScalarKind.Boolean => smallValue != 0 ? "true" : "false",
        FuzzScalarKind.Char => FormatChar(unchecked((char)smallValue)),
        _ => $"{type.Keyword}.Zero",
    };

    private static string FormatSByte(sbyte v) =>
        v == sbyte.MinValue ? "sbyte.MinValue" : string.Create(CultureInfo.InvariantCulture, $"((sbyte)({v}))");

    private static string FormatByte(byte v) =>
        string.Create(CultureInfo.InvariantCulture, $"((byte){v}U)");

    private static string FormatInt16(short v) =>
        v == short.MinValue ? "short.MinValue" : string.Create(CultureInfo.InvariantCulture, $"((short)({v}))");

    private static string FormatUInt16(ushort v) =>
        string.Create(CultureInfo.InvariantCulture, $"((ushort){v}U)");

    private static string FormatInt32(int v) =>
        v == int.MinValue ? "int.MinValue" : string.Create(CultureInfo.InvariantCulture, $"({v})");

    private static string FormatUInt32(uint v) =>
        string.Create(CultureInfo.InvariantCulture, $"{v}U");

    private static string FormatInt64(long v) =>
        v == long.MinValue ? "long.MinValue" : string.Create(CultureInfo.InvariantCulture, $"({v}L)");

    private static string FormatUInt64(ulong v) =>
        string.Create(CultureInfo.InvariantCulture, $"{v}UL");

    private static string FormatChar(char c) =>
        string.Create(CultureInfo.InvariantCulture, $"((char)0x{(ushort)c:X4}U)");

    private static LocalVariable FindLocal(KernelGenerationState state, FuzzScalarKind kind) =>
        FindLocalOrNull(state, kind) ?? state.Locals[0];

    private static LocalVariable? FindLocalOrNull(KernelGenerationState state, FuzzScalarKind kind)
    {
        foreach (var local in state.Locals)
        {
            if (local.Type.Kind == kind)
            {
                return local;
            }
        }

        return null;
    }

    private static (LocalVariable First, LocalVariable Second) PickDistinctPair(
        KernelGenerationState state,
        FuzzScalarKind preferredFirst,
        FuzzScalarKind preferredSecond)
    {
        var first = FindLocal(state, preferredFirst);
        foreach (var local in state.Locals)
        {
            if (local.Name != first.Name && local.Type.Kind == preferredSecond)
            {
                return (first, local);
            }
        }

        foreach (var local in state.Locals)
        {
            if (local.Name != first.Name)
            {
                return (first, local);
            }
        }

        return (first, first);
    }

    private static bool IsSimpleIdentifier(string expr)
    {
        if (string.IsNullOrEmpty(expr))
        {
            return false;
        }

        foreach (char c in expr)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static string RenderKernelMethod(string kernelName, ulong kernelSeed, int size, KernelGenerationState state)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    // fuzz-seed: {FuzzRandom.FormatSeed(kernelSeed)}, size: {size}"));
        sb.AppendLine("    [GpuKernel]");
        sb.AppendLine($"    public static void {kernelName}(int[] inA, int[] inB, int[] inC, ulong[] checksum)");
        sb.AppendLine("    {");
        sb.AppendLine("        int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;");
        sb.AppendLine("        if (gid >= checksum.Length)");
        sb.AppendLine("        {");
        sb.AppendLine("            return;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"        ulong hash = {FnvOffsetBasis}UL;"));
        foreach (string stmt in state.Statements)
        {
            sb.Append("        ").AppendLine(stmt);
        }

        sb.AppendLine("        checksum[gid] = hash;");
        sb.Append("    }");
        return sb.ToString();
    }

    private string RenderCaseProperty(string kernelName, ulong kernelSeed)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    // fuzz-seed: {FuzzRandom.FormatSeed(kernelSeed)}"));
        sb.AppendLine($"    public static DifferentialCase {kernelName}_Case {{ get; }} = new(\"{_options.KernelClassName}.{kernelName}\")");
        sb.AppendLine("    {");
        sb.AppendLine("        Shape = LaunchShapes.PartialWarp(),");
        if (_options.InputSeed is { } explicitInputSeed)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"        Seeds = [{explicitInputSeed}],"));
        }

        sb.AppendLine("        Inputs = static (rng, shape) =>");
        sb.AppendLine("        {");
        sb.AppendLine("            int rows = shape.Rows;");
        sb.AppendLine("            var (inA, inB) = DiffValues.Int32Pairs(rng, rows);");
        sb.AppendLine("            var inC = DiffValues.Values(rng, rows, DiffValues.ShiftCounts, DiffValues.NextInt32);");
        sb.AppendLine("            return new DiffBuffers()");
        sb.AppendLine("                .In(\"inA\", inA)");
        sb.AppendLine("                .In(\"inB\", inB)");
        sb.AppendLine("                .In(\"inC\", inC)");
        sb.AppendLine("                .Out<ulong>(\"checksum\", rows, [\"hash\"], OutputPolicy.Exact);");
        sb.AppendLine("        },");
        sb.AppendLine($"        Launch = static (b, s) => {_options.KernelClassName}.Launch{kernelName}(");
        sb.AppendLine("            b.Get<int>(\"inA\"),");
        sb.AppendLine("            b.Get<int>(\"inB\"),");
        sb.AppendLine("            b.Get<int>(\"inC\"),");
        sb.AppendLine("            b.Get<ulong>(\"checksum\"),");
        sb.AppendLine("            gridDimX: s.GridX,");
        sb.AppendLine("            blockDimX: s.BlockX),");
        sb.AppendLine($"        Oracle = static (b, _) => {_options.KernelClassName}.{kernelName}(");
        sb.AppendLine("            b.Get<int>(\"inA\"),");
        sb.AppendLine("            b.Get<int>(\"inB\"),");
        sb.AppendLine("            b.Get<int>(\"inC\"),");
        sb.AppendLine("            b.Get<ulong>(\"checksum\")),");
        sb.Append("    };");
        return sb.ToString();
    }

    private string BuildKernelsCompilationUnit(IReadOnlyList<GeneratedKernelSpec> kernels)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS0162, CS0219, CS1717, CS1718");
        sb.AppendLine("using System;");
        sb.AppendLine("using Kassyi.GSharp.Core;");
        sb.AppendLine();
        sb.AppendLine($"namespace {_options.Namespace};");
        sb.AppendLine();
        AppendEnumDeclarations(sb);
        sb.AppendLine();
        sb.AppendLine($"public static partial class {_options.KernelClassName}");
        sb.AppendLine("{");
        for (int i = 0; i < kernels.Count; i++)
        {
            if (i > 0)
            {
                sb.AppendLine();
            }

            sb.AppendLine(kernels[i].KernelMethodSource);
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>Appends the device enum declarations used by generated kernels to <paramref name="sb"/>.</summary>
    public static void AppendEnumDeclarations(StringBuilder sb)
    {
        sb.AppendLine("public enum FuzzEnumByte : byte");
        sb.AppendLine("{");
        sb.AppendLine("    Zero = 0,");
        sb.AppendLine("    One = 1,");
        sb.AppendLine("    Two = 2,");
        sb.AppendLine("    Bit = 0x10,");
        sb.AppendLine("    High = 0x80,");
        sb.AppendLine("    Mask = 0x55,");
        sb.AppendLine("    Alt = 0xAA,");
        sb.AppendLine("    Edge = byte.MaxValue,");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("public enum FuzzEnumShort : short");
        sb.AppendLine("{");
        sb.AppendLine("    Zero = 0,");
        sb.AppendLine("    One = 1,");
        sb.AppendLine("    Two = 2,");
        sb.AppendLine("    Bit = 0x0100,");
        sb.AppendLine("    High = -256,");
        sb.AppendLine("    Mask = 0x1234,");
        sb.AppendLine("    Alt = -1,");
        sb.AppendLine("    Edge = short.MinValue,");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("public enum FuzzEnumInt : int");
        sb.AppendLine("{");
        sb.AppendLine("    Zero = 0,");
        sb.AppendLine("    One = 1,");
        sb.AppendLine("    Two = 2,");
        sb.AppendLine("    Bit = 0x00010000,");
        sb.AppendLine("    High = -65536,");
        sb.AppendLine("    Mask = 0x13579BDF,");
        sb.AppendLine("    Alt = -1,");
        sb.AppendLine("    Edge = int.MinValue,");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("public enum FuzzEnumULong : ulong");
        sb.AppendLine("{");
        sb.AppendLine("    Zero = 0UL,");
        sb.AppendLine("    One = 1UL,");
        sb.AppendLine("    Two = 2UL,");
        sb.AppendLine("    Bit = 0x0000000100000000UL,");
        sb.AppendLine("    High = 0x8000000000000000UL,");
        sb.AppendLine("    Mask = 0x5555555555555555UL,");
        sb.AppendLine("    Alt = 0xAAAAAAAAAAAAAAAAUL,");
        sb.AppendLine("    Edge = ulong.MaxValue,");
        sb.AppendLine("}");
    }

    private string BuildCasesCompilationUnit(IReadOnlyList<GeneratedKernelSpec> kernels)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// ReSharper disable All");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Globalization;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using Kassyi.GSharp.Core;");
        sb.AppendLine();
        sb.AppendLine($"namespace {_options.Namespace};");
        sb.AppendLine();
        sb.AppendLine($"public static class {_options.CasesClassName}");
        sb.AppendLine("{");
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    public const ulong BatchFuzzSeed = {_options.FuzzSeed}UL;"));
        sb.AppendLine();

        for (int i = 0; i < kernels.Count; i++)
        {
            sb.AppendLine(kernels[i].CasePropertySource);
            sb.AppendLine();
        }

        sb.AppendLine("    public static IReadOnlyList<(string KernelName, ulong KernelSeed, DifferentialCase Case)> All { get; } =");
        sb.AppendLine("    [");
        foreach (var k in kernels)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"        (\"{k.KernelName}\", {k.KernelSeed}UL, {k.KernelName}_Case),"));
        }

        sb.AppendLine("    ];");

        if (_options.EmitProgramMain)
        {
            sb.AppendLine();
            AppendBatchRunnerMain(sb);
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    internal static void AppendBatchRunnerMain(StringBuilder sb)
    {
        sb.AppendLine("    public static int Main(string[] args)");
        sb.AppendLine("    {");
        sb.AppendLine("        bool managedOnly = args.Any(static a => string.Equals(a, \"--managed-only\", StringComparison.OrdinalIgnoreCase));");
        sb.AppendLine("        int? explicitSeed = null;");
        sb.AppendLine("        for (int i = 0; i < args.Length - 1; i++)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (string.Equals(args[i], \"--input-seed\", StringComparison.OrdinalIgnoreCase) &&");
        sb.AppendLine("                int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))");
        sb.AppendLine("            {");
        sb.AppendLine("                explicitSeed = parsed;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        IReadOnlyList<DiffPathRunner> activePaths = DiffPaths.Available;");
        sb.AppendLine("        if (managedOnly)");
        sb.AppendLine("        {");
        sb.AppendLine("            activePaths = activePaths.Where(static p => p.Path == DiffPath.Managed).ToArray();");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        IReadOnlyList<int>? seeds = explicitSeed is { } s ? [s] : null;");
        sb.AppendLine("        var cases = All.Select(static entry => entry.Case).ToArray();");
        sb.AppendLine("        IReadOnlyList<DifferentialReport> reports;");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            reports = DifferentialRunner.EvaluateBatch(cases, seeds, activePaths, KnownDivergences.All, checkStale: false);");
        sb.AppendLine("        }");
        sb.AppendLine("        catch (Exception ex)");
        sb.AppendLine("        {");
        sb.AppendLine("            Console.WriteLine(JsonSerializer.Serialize(new");
        sb.AppendLine("            {");
        sb.AppendLine("                status = \"harness_error\",");
        sb.AppendLine("                batchFuzzSeed = string.Create(CultureInfo.InvariantCulture, $\"0x{BatchFuzzSeed:X16}\"),");
        sb.AppendLine("                exceptionType = ex.GetType().FullName,");
        sb.AppendLine("                message = ex.Message,");
        sb.AppendLine("            }));");
        sb.AppendLine("            return 2;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        int failureCount = 0;");
        sb.AppendLine("        for (int i = 0; i < reports.Count; i++)");
        sb.AppendLine("        {");
        sb.AppendLine("            var (kernelName, kernelSeed, _) = All[i];");
        sb.AppendLine("            var report = reports[i];");
        sb.AppendLine("            if (!report.HasFailures)");
        sb.AppendLine("            {");
        sb.AppendLine("                continue;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            failureCount++;");
        sb.AppendLine("            var firstMismatch = report.UnexpectedMismatches.FirstOrDefault();");
        sb.AppendLine("            var firstPathFailure = report.Failures.FirstOrDefault();");
        sb.AppendLine("            var firstPropFailure = report.PropertyFailures.FirstOrDefault();");
        sb.AppendLine("            int failInputSeed = firstMismatch?.Seed ?? firstPathFailure?.Seed ?? firstPropFailure?.Seed ?? 0;");
        sb.AppendLine("            string failureKind = firstMismatch is not null");
        sb.AppendLine("                ? \"CellMismatch\"");
        sb.AppendLine("                : firstPathFailure is not null");
        sb.AppendLine("                    ? $\"RuntimeException:{firstPathFailure.Exception.GetType().Name}\"");
        sb.AppendLine("                    : \"PropertyFailure\";");
        sb.AppendLine("            string[] failingPaths = firstMismatch is not null");
        sb.AppendLine("                ? firstMismatch.Paths.Where(static p => p.Differs).Select(static p => DiffPaths.Format(p.Path)).ToArray()");
        sb.AppendLine("                : firstPathFailure is not null");
        sb.AppendLine("                    ? [DiffPaths.Format(firstPathFailure.Path)]");
        sb.AppendLine("                    : firstPropFailure is not null");
        sb.AppendLine("                        ? [DiffPaths.Format(firstPropFailure.Path)]");
        sb.AppendLine("                        : [];");
        sb.AppendLine();
        sb.AppendLine("            Console.WriteLine(JsonSerializer.Serialize(new");
        sb.AppendLine("            {");
        sb.AppendLine("                status = \"failed\",");
        sb.AppendLine("                kernel = kernelName,");
        sb.AppendLine("                batchFuzzSeed = string.Create(CultureInfo.InvariantCulture, $\"0x{BatchFuzzSeed:X16}\"),");
        sb.AppendLine("                kernelSeed = string.Create(CultureInfo.InvariantCulture, $\"0x{kernelSeed:X16}\"),");
        sb.AppendLine("                inputSeed = failInputSeed,");
        sb.AppendLine("                failureKind,");
        sb.AppendLine("                failingPaths,");
        sb.AppendLine("                expected = firstMismatch?.Expected,");
        sb.AppendLine("                actual = firstMismatch?.Paths.FirstOrDefault(static p => p.Differs)?.Value ?? firstPathFailure?.Exception.Message ?? firstPropFailure?.Message,");
        sb.AppendLine("                reproCommand = string.Create(CultureInfo.InvariantCulture, $\"--fuzz-seed 0x{BatchFuzzSeed:X16} --input-seed {failInputSeed}\"),");
        sb.AppendLine("                report = report.ToString(),");
        sb.AppendLine("            }));");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        Console.WriteLine(JsonSerializer.Serialize(new");
        sb.AppendLine("        {");
        sb.AppendLine("            status = failureCount == 0 ? \"ok\" : \"batch_failed\",");
        sb.AppendLine("            batchFuzzSeed = string.Create(CultureInfo.InvariantCulture, $\"0x{BatchFuzzSeed:X16}\"),");
        sb.AppendLine("            kernelsEvaluated = reports.Count,");
        sb.AppendLine("            kernelsFailed = failureCount,");
        sb.AppendLine("            paths = activePaths.Select(static p => p.Name).ToArray(),");
        sb.AppendLine("        }));");
        sb.AppendLine("        return failureCount == 0 ? 0 : 1;");
        sb.AppendLine("    }");
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

    private sealed record LocalVariable(string Name, FuzzTypeInfo Type);

    private sealed class KernelGenerationState(FuzzRandom rng, int size, double interferenceProbability)
    {
        public FuzzRandom Rng { get; } = rng;

        public int Size { get; } = size;

        public double InterferenceProbability { get; } = interferenceProbability;

        public List<LocalVariable> Locals { get; } = [];

        public List<string> Statements { get; } = [];

        public ulong ChecksumSiteId { get; set; }
    }
}
