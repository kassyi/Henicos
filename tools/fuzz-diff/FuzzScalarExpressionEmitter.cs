using System.Globalization;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Emits scalar, enum, and boolean expressions, boundary literals, inline division/overflow guards,
/// and left-to-right interfering sub-expression pairs while honoring active <see cref="KnownIssueFilter"/> switches.
/// </summary>
public static class FuzzScalarExpressionEmitter
{
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

    /// <summary>Generates a recursive expression of <paramref name="targetType"/>.</summary>
    public static string GenerateExpression(KernelGenerationState state, FuzzTypeInfo targetType, int depth, int maxDepth)
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

    /// <summary>Generates a leaf expression (local variable, composite member read, or local mixed with a boundary literal).</summary>
    public static string GenerateLeafExpression(KernelGenerationState state, FuzzTypeInfo targetType)
    {
        var rng = state.Rng;

        // Occasionally read from a composite struct member/property or Span element when targeting int/long.
        if (state.StructLocals.Count > 0 && rng.Chance(0.18))
        {
            if (targetType.Kind == FuzzScalarKind.Int32)
            {
                return rng.NextBelow(4) switch
                {
                    0 => "(sL3.Mid.Inner.CompA)",
                    1 => "(sL3.Wrap.Shifted)",
                    2 => "(sPack1.PackedSum)",
                    _ => "((int)sL3.Wrap)",
                };
            }

            if (targetType.Kind == FuzzScalarKind.Int64)
            {
                return rng.NextBool() ? "(sL3.Total)" : "(sPack4.PackedWide)";
            }
        }

        var sameTypeLocals = new List<ScalarLocalVariable>();
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

    /// <summary>Generates a boolean expression.</summary>
    public static string GenerateBooleanExpression(KernelGenerationState state, int depth, int maxDepth)
    {
        var rng = state.Rng;
        int kind = rng.NextBelow(4);
        switch (kind)
        {
            case 0:
            {
                string op = rng.NextBool() ? "&&" : "||";
                string left = GenerateExpression(state, FuzzTypeInfo.Get(FuzzScalarKind.Boolean), depth + 1, maxDepth);
                string right = GenerateExpression(state, FuzzTypeInfo.Get(FuzzScalarKind.Boolean), depth + 1, maxDepth);
                return $"({left} {op} {right})";
            }
            case 1:
            {
                string inner = GenerateExpression(state, FuzzTypeInfo.Get(FuzzScalarKind.Boolean), depth + 1, maxDepth);
                return $"(!{inner})";
            }
            case 2:
            {
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
                string cond = GenerateExpression(state, FuzzTypeInfo.Get(FuzzScalarKind.Boolean), depth + 1, maxDepth);
                string whenTrue = GenerateExpression(state, FuzzTypeInfo.Get(FuzzScalarKind.Boolean), depth + 1, maxDepth);
                string whenFalse = GenerateExpression(state, FuzzTypeInfo.Get(FuzzScalarKind.Boolean), depth + 1, maxDepth);
                return $"({cond} ? {whenTrue} : {whenFalse})";
            }
        }
    }

    /// <summary>Generates a device enum expression.</summary>
    public static string GenerateEnumExpression(KernelGenerationState state, FuzzTypeInfo enumType, int depth, int maxDepth)
    {
        var rng = state.Rng;
        int kind = rng.NextBelow(4);
        switch (kind)
        {
            case 0:
            {
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
                string inner = GenerateExpression(state, enumType, depth + 1, maxDepth);
                return $"(~{inner})";
            }
            case 2:
            {
                string cond = GenerateExpression(state, FuzzTypeInfo.Get(FuzzScalarKind.Boolean), depth + 1, maxDepth);
                string whenTrue = GenerateExpression(state, enumType, depth + 1, maxDepth);
                string whenFalse = GenerateExpression(state, enumType, depth + 1, maxDepth);
                return $"({cond} ? {whenTrue} : {whenFalse})";
            }
            default:
            {
                var underlying = FuzzTypeInfo.Get(enumType.UnderlyingKind);
                string inner = GenerateExpression(state, underlying, depth + 1, maxDepth);
                return $"(({enumType.Keyword}){inner})";
            }
        }
    }

    /// <summary>Generates an integral expression respecting Swarm weights and <see cref="KnownIssueFilter"/>.</summary>
    public static string GenerateIntegralExpression(KernelGenerationState state, FuzzTypeInfo targetType, int depth, int maxDepth)
    {
        var rng = state.Rng;
        var promotedKind = FuzzTypeInfo.PromoteUnary(targetType.Kind);
        var promotedType = FuzzTypeInfo.Get(promotedKind);
        var sw = state.Swarm;

        int totalW = sw.BitwiseWeight + sw.DivisionWeight + sw.ShiftWeight + sw.TernaryWeight + sw.SwitchExprWeight + 20;
        int roll = rng.NextBelow(totalW);
        string resultExpr;

        if (roll < sw.BitwiseWeight)
        {
            string[] ops = ["+", "-", "*", "&", "|", "^"];
            string op = ops[rng.NextBelow(ops.Length)];
            var (left, right) = GeneratePossiblyInterferingPair(state, promotedType, depth + 1, maxDepth);
            resultExpr = $"({left} {op} {right})";
        }
        else if ((roll -= sw.BitwiseWeight) < sw.DivisionWeight)
        {
            bool isModulo = rng.NextBool();
            string left = GenerateExpression(state, promotedType, depth + 1, maxDepth);
            string right = GenerateLeafExpression(state, promotedType);
            resultExpr = BuildGuardedDivisionOrModulo(promotedKind, left, right, isModulo, leftIsSimpleLocal: IsSimpleIdentifier(left));
        }
        else if ((roll -= sw.DivisionWeight) < sw.ShiftWeight)
        {
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
        else if ((roll -= sw.ShiftWeight) < sw.TernaryWeight)
        {
            string cond = GenerateExpression(state, FuzzTypeInfo.Get(FuzzScalarKind.Boolean), depth + 1, maxDepth);
            var (whenTrue, whenFalse) = GeneratePossiblyInterferingPair(state, targetType, depth + 1, maxDepth);
            return $"({cond} ? {EnsureType(whenTrue, targetType)} : {EnsureType(whenFalse, targetType)})";
        }
        else if ((roll -= sw.TernaryWeight) < sw.SwitchExprWeight && depth <= 1)
        {
            return FuzzCompositeExpressionEmitter.GenerateSwitchExpression(state, targetType, depth + 1, maxDepth);
        }
        else if (rng.NextBool())
        {
            string unaryOp = promotedType.IsSigned
                ? (rng.NextBelow(3) switch { 0 => "+", 1 => "-", _ => "~" })
                : (rng.NextBool() ? "+" : "~");
            string operand = GenerateExpression(state, promotedType, depth + 1, maxDepth);
            resultExpr = $"({unaryOp}{operand})";
        }
        else
        {
            var mutTarget = state.FindLocal(targetType.Kind);
            bool canUseIncDecValue = !mutTarget.Type.IsNarrowIntegral || !state.IssueFilter.AvoidNarrowIncDecValue;
            if (canUseIncDecValue && rng.NextBool())
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

    /// <summary>Generates a pair of expressions that may interfere via a shared local variable mutation.</summary>
    public static (string Left, string Right) GeneratePossiblyInterferingPair(
        KernelGenerationState state,
        FuzzTypeInfo operandType,
        int depth,
        int maxDepth)
    {
        var rng = state.Rng;
        if (rng.Chance(state.InterferenceProbability))
        {
            var victim = state.FindLocalOrNull(operandType.Kind);
            if (victim is not null)
            {
                string mutation = BuildInlineMutation(state, victim);
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

    /// <summary>Builds an inline mutation expression on <paramref name="victim"/> respecting <c>#1079</c> avoidance.</summary>
    public static string BuildInlineMutation(KernelGenerationState state, ScalarLocalVariable victim)
    {
        var rng = state.Rng;
        bool allowIncDecValue = !victim.Type.IsNarrowIntegral || !state.IssueFilter.AvoidNarrowIncDecValue;
        string delta = GenerateRandomLiteral(rng, victim.Type);
        if (!allowIncDecValue)
        {
            string op = rng.NextBelow(3) switch
            {
                0 => "+=",
                1 => "-=",
                _ => "^=",
            };
            return $"({victim.Name} {op} {EnsureType(delta, victim.Type)})";
        }

        int mode = rng.NextBelow(4);
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
        ScalarLocalVariable victim,
        FuzzTypeInfo targetType,
        int depth,
        int maxDepth)
    {
        _ = depth;
        _ = maxDepth;
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

    /// <summary>Generates a shift count expression.</summary>
    public static string GenerateShiftCountExpression(KernelGenerationState state, int depth, int maxDepth)
    {
        var rng = state.Rng;
        int mode = rng.NextBelow(3);
        if (mode == 0)
        {
            return "inC[gid]";
        }

        if (mode == 1)
        {
            return state.FindLocal(FuzzScalarKind.Int32).Name;
        }

        string expr = GenerateExpression(state, FuzzTypeInfo.Get(FuzzScalarKind.Int32), depth, maxDepth);
        return $"(int)({expr})";
    }

    /// <summary>Builds a division or remainder expression with an inline ternary guard on the divisor.</summary>
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

    /// <summary>Wraps a divisor expression for <c>/=</c> or <c>%=</c> compound assignment.</summary>
    public static string WrapDivisorForCompoundAssignment(string targetName, FuzzTypeInfo targetType, string rawDivisor)
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
                $"((({typedDivisor}) == ({targetType.Keyword})0 || ({typedDivisor}) == ({targetType.Keyword})(-1)) ? ({targetType.Keyword})1 : ({typedDivisor}))",
        };
    }

    /// <summary>Casts <paramref name="expr"/> to <paramref name="targetType"/>.</summary>
    public static string EnsureType(string expr, FuzzTypeInfo targetType) => targetType.Kind switch
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

    /// <summary>Casts <paramref name="expr"/> from <paramref name="fromType"/> to <paramref name="toType"/>.</summary>
    public static string CastBetweenTypes(string expr, FuzzTypeInfo fromType, FuzzTypeInfo toType)
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

    /// <summary>Generates a random boundary literal of <paramref name="type"/>.</summary>
    public static string GenerateRandomLiteral(FuzzRandom rng, FuzzTypeInfo type) => type.Kind switch
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

    /// <summary>Formats a small integer constant as a typed literal of <paramref name="type"/>.</summary>
    public static string FormatLiteral(FuzzTypeInfo type, int smallValue) => type.Kind switch
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
}

