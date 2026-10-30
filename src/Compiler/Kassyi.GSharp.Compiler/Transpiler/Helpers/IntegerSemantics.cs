using System.Numerics;
using Microsoft.CodeAnalysis.Operations;
using Names = Kassyi.GSharp.Compiler.GSharpInternalNames.IntegerSemantics;

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Where the C++ of an integer operator or a float-to-integer conversion would leave undefined what C# defines, and the
/// Prelude function (<c>IntegerSemantics.hpp</c>) that gives the C# result instead (#722, #724).
/// <see cref="IntegerSemanticsEmitter"/> writes the forms, <see cref="BoundedLoopStep"/> proves a loop counter bounded.
/// </summary>
/// <remarks>
/// <para>
/// int and long <c>+ - *</c>, unary <c>-</c>, <c>++</c>, <c>--</c> and their compound assignments wrap (C# spec §12.10,
/// unchecked context; <c>checked</c> is GSH0003 in kernels); C++ leaves signed overflow undefined. A shift uses the low 5
/// or 6 bits of its count (§12.11), as Roslyn's lowering writes it (<c>RewriteBuiltInShiftOperation</c>: a constant count
/// is masked in place, any other one gets <c>&amp; 0x1F</c> / <c>&amp; 0x3F</c>); C++ leaves a count of the width or more,
/// and a left shift of a negative value, undefined. A float converted to an integer saturates and NaN gives 0, as .NET 9
/// and later convert it; C++ leaves an out-of-range conversion undefined.
/// </para>
/// <para>
/// An operator or conversion with a constant result is written as its value (<see cref="GetConstant"/>), as Roslyn
/// folds it: <c>3 &lt;&lt; 33</c> is <c>6</c>. The operator stays as it is where the result is known not to overflow:
/// operands whose types or constant values bound the result, and the step of a for loop counter that the loop condition
/// bounds (<see cref="BoundedLoopStep"/>). The rest costs no instruction on the GPU: the unsigned operation is the same
/// instruction without the no-overflow assumption, and the masked shift is one funnel shift. Which definition wins:
/// <c>docs/spec/en/01_language/06_semantics.md</c>.
/// </para>
/// </remarks>
internal static class IntegerSemantics
{
    /// <summary>
    /// The type and value of <paramref name="operation"/> when it is an int, uint, long or ulong operator or conversion
    /// with a constant result, which is written as its value; null otherwise.
    /// </summary>
    /// <remarks>
    /// [WHY] C# folds a constant shift with the masked count and never reports it (<c>3 &lt;&lt; 33</c> is 6), while
    /// the C++ of the expression as written is undefined (nvcc and MSVC give 0). Every other integer constant is folded
    /// as well, so that no constant expression depends on what C++ does with it.
    /// </remarks>
    public static (ITypeSymbol Type, object Value)? GetConstant(IOperation? operation) =>
        operation is { ConstantValue: { HasValue: true, Value: { } value }, Type: { } type } && GetWidth(type) != 0
            ? (type, value)
            : null;

    /// <summary>
    /// The Prelude function that <paramref name="operation"/> is written as (<c>__gsharp_add_i32(a,b)</c>), or
    /// <see langword="null"/> for an operator that C++ gives the C# result (then <see cref="GetShiftMask"/> tells whether
    /// its count is masked).
    /// </summary>
    public static string? GetBinaryHelper(IBinaryOperation operation)
    {
        if (!IsIntrinsicIntegerOperation(operation) || operation.Type is not { } type)
        {
            return null;
        }

        var (helper, _) = Lookup(operation.OperatorKind, type.SpecialType);
        var arithmetic = operation.OperatorKind is BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or
            BinaryOperatorKind.Multiply;
        return arithmetic && FitsWithoutWrap(operation) ? null : helper;
    }

    /// <summary>
    /// The mask of the count of a shift that stays a C++ operator (<c>&lt;&lt;</c> of uint and ulong, <c>&gt;&gt;</c>,
    /// <c>&gt;&gt;&gt;</c> of uint and ulong): 31 or 63; 0 for any other operation.
    /// </summary>
    public static int GetShiftMask(IBinaryOperation operation) =>
        IsIntrinsicIntegerOperation(operation) && operation.Type is { } type
            ? Lookup(operation.OperatorKind, type.SpecialType).ShiftMask
            : 0;

    /// <summary>The Prelude function that writes unary minus <paramref name="operation"/> (<c>__gsharp_neg_i32</c>), or null.</summary>
    public static string? GetNegationHelper(IUnaryOperation operation)
    {
        if (operation.OperatorKind != UnaryOperatorKind.Minus || operation.OperatorMethod != null || operation.IsLifted ||
            operation.ConstantValue.HasValue || GetWidth(operation.Type) is not (var width and not 0) ||
            !IsSigned(operation.Type))
        {
            return null;
        }

        // -x overflows only for the minimum value of the type
        if (TryGetRange(operation.Operand, out var min, out _) && min > MinValue(width))
        {
            return null;
        }

        return Pick(width, Names.NegateInt32, Names.NegateInt64);
    }

    /// <summary>
    /// The Prelude function of the step of the increment or decrement <paramref name="operation"/> of an int or long
    /// (<paramref name="node"/>): <c>__gsharp_add_i32</c> for <c>++</c>, <c>__gsharp_sub_i32</c> for <c>--</c>; null when
    /// the C++ operator cannot overflow there (<see cref="IntegerSemanticsEmitter.AppendStep"/> writes the step).
    /// </summary>
    /// <remarks>
    /// The analysis of a for loop step runs once per step: its result is kept in
    /// <see cref="Emitter.BoundedLoopSteps"/>.
    /// </remarks>
    public static string? GetIncrementHelper(IIncrementOrDecrementOperation operation, ExpressionSyntax node,
        ref TranspileContext context)
    {
        if (GetStepHelper(operation, undo: false) is not { } helper)
        {
            return null;
        }

        var steps = context.Emitter.BoundedLoopSteps;
        if (!steps.TryGetValue(node, out var bounded))
        {
            bounded = BoundedLoopStep.IsBounded(operation, node, context.Query.SemanticModel);
            steps[node] = bounded;
        }

        return bounded ? null : helper;
    }

    /// <summary>
    /// The Prelude function that steps the value of the increment or decrement <paramref name="operation"/> back
    /// (<c>__gsharp_sub_i32</c> for <c>++</c>), which gives the old value of a postfix step that wraps.
    /// </summary>
    public static string? GetStepUndoHelper(IIncrementOrDecrementOperation operation) =>
        GetStepHelper(operation, undo: true);

    // __gsharp_add_i32 / __gsharp_sub_i32 for a predefined int or long ++ / -- (the other one for <undo>), before the
    // loop counter analysis
    private static string? GetStepHelper(IIncrementOrDecrementOperation operation, bool undo)
    {
        if (operation.OperatorMethod != null || operation.IsLifted || operation.Type is not { } type)
        {
            return null;
        }

        var add = (operation.Kind == OperationKind.Increment) != undo;
        return Lookup(add ? BinaryOperatorKind.Add : BinaryOperatorKind.Subtract, type.SpecialType).Helper;
    }

    /// <summary>
    /// The Prelude function of the operator of the compound assignment <paramref name="operation"/>
    /// (<c>__gsharp_add_i32</c> for an int <c>+=</c>), which <see cref="IntegerSemanticsEmitter.AppendCompound"/> and the
    /// form that evaluates the operands into temporaries call; null for an assignment that C++ gives the C# result (see
    /// <see cref="GetCompoundShiftMask"/>).
    /// </summary>
    public static string? GetCompoundHelper(ICompoundAssignmentOperation operation) =>
        operation.OperatorMethod == null && !operation.IsLifted
            ? Lookup(operation.OperatorKind, GetCompoundOperatorType(operation)).Helper
            : null;

    /// <summary>
    /// The mask of the count of a compound shift that stays a C++ operator (<c>&gt;&gt;=</c>, and <c>&lt;&lt;=</c> and
    /// <c>&gt;&gt;&gt;=</c> of uint and ulong): 31 or 63; 0 for any other assignment.
    /// </summary>
    public static int GetCompoundShiftMask(ICompoundAssignmentOperation operation) =>
        operation.OperatorMethod == null && !operation.IsLifted
            ? Lookup(operation.OperatorKind, GetCompoundOperatorType(operation)).ShiftMask
            : 0;

    /// <summary>
    /// The type that the result of the operator of the compound assignment <paramref name="operation"/> is cast back to,
    /// when the target is narrower than the operator (<c>short += 1</c> computes in int); null when it is not.
    /// </summary>
    public static ITypeSymbol? GetCompoundNarrowing(ICompoundAssignmentOperation operation) =>
        operation.Target.Type is { } target && target.SpecialType != GetCompoundOperatorType(operation)
            ? target
            : null;

    // The one table of the integer operators, keyed by the operator kind (a compound assignment has the kind of its
    // binary operator) and the type of the operator: the Prelude function of a signed int or long operator that C++
    // leaves undefined, or the mask of the count of a shift that stays a C++ operator. Neither for any other operator.
    private static (string? Helper, int ShiftMask) Lookup(BinaryOperatorKind kind, SpecialType type)
    {
        if (GetWidth(type) is not (var width and not 0))
        {
            return (null, 0);
        }

        var signed = IsSigned(type);
        return kind switch
        {
            BinaryOperatorKind.Add when signed => (Pick(width, Names.AddInt32, Names.AddInt64), 0),
            BinaryOperatorKind.Subtract when signed => (Pick(width, Names.SubtractInt32, Names.SubtractInt64), 0),
            BinaryOperatorKind.Multiply when signed => (Pick(width, Names.MultiplyInt32, Names.MultiplyInt64), 0),
            BinaryOperatorKind.LeftShift when signed => (Pick(width, Names.ShiftLeftInt32, Names.ShiftLeftInt64), 0),
            BinaryOperatorKind.UnsignedRightShift when signed =>
                (Pick(width, Names.UnsignedShiftRightInt32, Names.UnsignedShiftRightInt64), 0),
            BinaryOperatorKind.LeftShift or BinaryOperatorKind.RightShift or BinaryOperatorKind.UnsignedRightShift =>
                (null, Mask(width)),
            _ => (null, 0)
        };
    }

    /// <summary>
    /// The Prelude function that converts the float or double operand of the explicit conversion
    /// <paramref name="operation"/> to its integer type (<c>__gsharp_to_i32</c>), and in <paramref name="needsCast"/>
    /// whether its result is then cast to the target type (a type narrower than int, or an enum); null for any other
    /// conversion.
    /// </summary>
    public static string? GetConversionHelper(IConversionOperation operation, out bool needsCast)
    {
        needsCast = false;
        if (operation.OperatorMethod != null || operation.IsTryCast || operation.ConstantValue.HasValue ||
            operation.Operand.Type?.SpecialType is not (SpecialType.System_Single or SpecialType.System_Double) ||
            operation.Type is not { } target)
        {
            return null;
        }

        var integral = target is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying }
            ? underlying
            : target;
        string? helper = integral.SpecialType switch
        {
            SpecialType.System_Int32 => Names.ConvertToInt32,
            SpecialType.System_UInt32 => Names.ConvertToUInt32,
            SpecialType.System_Int64 => Names.ConvertToInt64,
            SpecialType.System_UInt64 => Names.ConvertToUInt64,
            SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16
                or SpecialType.System_Char => Names.ConvertToInt32,
            _ => null
        };
        // An enum, or a type narrower than int, which the int32 result is truncated to: (byte)__gsharp_to_i32(x).
        // This is the .NET 9 and 10 result, saturated to int32 and then truncated ((byte)300f is 44, (sbyte)-129f is
        // 127, (ushort)-40000f is 25536, (byte)-1f is 255, NaN is 0). .NET 11 saturates to the target type instead
        // ((byte)298f is 255, (ushort)-42f is 0; dotnet/runtime#128604). G# follows .NET 10 for now: the managed path
        // runs on the host runtime, so on net10.0 all three paths agree. IntegerSemanticsScenarios.Conversions fails
        // on a .NET 11 host; see #890 for the switch.
        var isEnum = !SymbolEqualityComparer.Default.Equals(integral, target);
        var isNarrow = integral.SpecialType is SpecialType.System_SByte or SpecialType.System_Byte or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char;
        needsCast = helper != null && (isEnum || isNarrow);
        return helper;
    }

    /// <summary>
    /// Whether the C++ of <paramref name="expression"/> begins with a name or a digit rather than with its operator or
    /// left operand (<see cref="OperatorSpacing"/>): a Prelude call that stands for its operator, or a constant written
    /// as its value (a negative one is parenthesized).
    /// </summary>
    public static bool IsWrittenAsCallOrConstant(ExpressionSyntax expression, SemanticModel model)
    {
        if (expression is not (BinaryExpressionSyntax or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax or
                AssignmentExpressionSyntax or CastExpressionSyntax) ||
            model.GetSafeOperation(expression) is not { } operation || operation.Syntax.RawKind != expression.RawKind)
        {
            return false;
        }

        return operation switch
        {
            _ when GetConstant(operation) != null => true,
            IBinaryOperation binary => GetBinaryHelper(binary) != null,
            IUnaryOperation unary => GetNegationHelper(unary) != null,
            // The step of a for loop, the only one the counter analysis keeps as an operator, is never an operand
            IIncrementOrDecrementOperation step => GetStepHelper(step, undo: false) != null,
            ICompoundAssignmentOperation compound => GetCompoundHelper(compound) != null,
            _ => false
        };
    }

    // A predefined, non-lifted operator on integers whose result is not a constant (a constant is written as its value,
    // GetConstant). The left operand has the type of the operator (converted to it), which a pointer difference
    // (byte* - byte* is long) does not
    private static bool IsIntrinsicIntegerOperation(IBinaryOperation operation) =>
        operation.OperatorMethod == null && !operation.IsLifted && !operation.ConstantValue.HasValue &&
        operation.Type is { } type && operation.LeftOperand.Type?.SpecialType == type.SpecialType;

    // The type of the operator of a compound assignment of an integer target: the binary numeric promotion of the target
    // and the value (C# spec §12.4.7.3, §12.21.4), which does not depend on whether the bound value already carries the
    // conversion to it; for a shift, the promoted type of the target (§12.11). Null for a pointer (p += n), an enum
    // (e += n) or a floating-point value, which have operators of their own.
    private static SpecialType GetCompoundOperatorType(ICompoundAssignmentOperation operation)
    {
        var target = Promote(operation.Target.Type);
        if (target == SpecialType.None || operation.OperatorKind is BinaryOperatorKind.LeftShift or
                BinaryOperatorKind.RightShift or BinaryOperatorKind.UnsignedRightShift)
        {
            return target;
        }

        var value = Promote(operation.Value.Type);
        if (value == SpecialType.None)
        {
            return SpecialType.None;
        }

        if (target == value)
        {
            return target;
        }

        // Mixed: ulong wins, then long; uint with int is long
        if (target == SpecialType.System_UInt64 || value == SpecialType.System_UInt64)
        {
            return SpecialType.System_UInt64;
        }

        return SpecialType.System_Int64;
    }

    // The integral promotions: an integer type narrower than int is int; None for any type that is not an integer
    private static SpecialType Promote(ITypeSymbol? type) => type?.SpecialType switch
    {
        SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Char or SpecialType.System_Int32 => SpecialType.System_Int32,
        SpecialType.System_UInt32 => SpecialType.System_UInt32,
        SpecialType.System_Int64 => SpecialType.System_Int64,
        SpecialType.System_UInt64 => SpecialType.System_UInt64,
        _ => SpecialType.None
    };

    // Whether the operand ranges bound the exact result inside the type of the operation, so that it cannot wrap
    private static bool FitsWithoutWrap(IBinaryOperation operation)
    {
        if (GetWidth(operation.Type) is not (var width and not 0) ||
            !TryGetRange(operation.LeftOperand, out var leftMin, out var leftMax) ||
            !TryGetRange(operation.RightOperand, out var rightMin, out var rightMax))
        {
            return false;
        }

        BigInteger min, max;
        switch (operation.OperatorKind)
        {
            case BinaryOperatorKind.Add:
                (min, max) = (leftMin + rightMin, leftMax + rightMax);
                break;
            case BinaryOperatorKind.Subtract:
                (min, max) = (leftMin - rightMax, leftMax - rightMin);
                break;
            case BinaryOperatorKind.Multiply:
                var a = leftMin * rightMin;
                var b = leftMin * rightMax;
                var c = leftMax * rightMin;
                var d = leftMax * rightMax;
                min = BigInteger.Min(BigInteger.Min(a, b), BigInteger.Min(c, d));
                max = BigInteger.Max(BigInteger.Max(a, b), BigInteger.Max(c, d));
                break;
            default:
                return false;
        }

        return min >= MinValue(width) && max <= MaxValue(width);
    }

    // The values an operand can take: its constant, the range of what an integer conversion converts (when it fits the
    // target type, the conversion keeps the value), or the range of its type. An int or long operand of an operation of
    // its own type spans the type, which never proves anything; the narrower types and an int or uint in a long
    // operation ((long)i * i) do.
    private static bool TryGetRange(IOperation operand, out BigInteger min, out BigInteger max)
    {
        if (operand.ConstantValue is { HasValue: true, Value: { } value } && TryGetInteger(value, out var constant))
        {
            min = max = constant;
            return true;
        }

        if (!TryGetTypeRange(operand.Type, out min, out max))
        {
            return false;
        }

        if (operand is IConversionOperation { OperatorMethod: null } conversion &&
            TryGetRange(conversion.Operand, out var innerMin, out var innerMax) && innerMin >= min && innerMax <= max)
        {
            (min, max) = (innerMin, innerMax);
        }

        return true;
    }

    private static bool TryGetTypeRange(ITypeSymbol? type, out BigInteger min, out BigInteger max)
    {
        (min, max) = type?.SpecialType switch
        {
            SpecialType.System_SByte => (sbyte.MinValue, sbyte.MaxValue),
            SpecialType.System_Byte => (byte.MinValue, byte.MaxValue),
            SpecialType.System_Int16 => (short.MinValue, short.MaxValue),
            SpecialType.System_UInt16 or SpecialType.System_Char => (ushort.MinValue, ushort.MaxValue),
            SpecialType.System_Int32 => (int.MinValue, int.MaxValue),
            SpecialType.System_UInt32 => (uint.MinValue, uint.MaxValue),
            SpecialType.System_Int64 => (long.MinValue, long.MaxValue),
            SpecialType.System_UInt64 => (ulong.MinValue, ulong.MaxValue),
            _ => (BigInteger.One, BigInteger.Zero)
        };
        return min <= max;
    }

    /// <summary>The value of an integer constant of any integer type (char included).</summary>
    internal static bool TryGetInteger(object value, out BigInteger integer)
    {
        switch (value)
        {
            case int i: integer = i; return true;
            case uint u: integer = u; return true;
            case long l: integer = l; return true;
            case ulong ul: integer = ul; return true;
            case short s: integer = s; return true;
            case ushort us: integer = us; return true;
            case sbyte sb: integer = sb; return true;
            case byte b: integer = b; return true;
            case char c: integer = c; return true;
            default: integer = BigInteger.Zero; return false;
        }
    }

    /// <summary>32 or 64 for int, uint, long and ulong; 0 for any other type.</summary>
    internal static int GetWidth(ITypeSymbol? type) => GetWidth(type?.SpecialType ?? SpecialType.None);

    private static int GetWidth(SpecialType type) => type switch
    {
        SpecialType.System_Int32 or SpecialType.System_UInt32 => 32,
        SpecialType.System_Int64 or SpecialType.System_UInt64 => 64,
        _ => 0
    };

    /// <summary>Whether the type is int or long.</summary>
    internal static bool IsSigned(ITypeSymbol? type) => IsSigned(type?.SpecialType ?? SpecialType.None);

    private static bool IsSigned(SpecialType type) => type is SpecialType.System_Int32 or SpecialType.System_Int64;

    private static int Mask(int width) => width - 1;

    private static string Pick(int width, string int32, string int64) => width == 32 ? int32 : int64;

    /// <summary>The minimum of the signed type of the width (32 or 64).</summary>
    internal static BigInteger MinValue(int width) => width == 32 ? int.MinValue : long.MinValue;

    /// <summary>The maximum of the signed type of the width (32 or 64).</summary>
    internal static BigInteger MaxValue(int width) => width == 32 ? int.MaxValue : long.MaxValue;
}
