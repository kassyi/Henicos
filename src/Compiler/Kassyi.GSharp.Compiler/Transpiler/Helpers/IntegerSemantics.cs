using System.Numerics;
using Microsoft.CodeAnalysis.Operations;
using Names = Kassyi.GSharp.Compiler.GSharpInternalNames.IntegerSemantics;

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Where the C++ of an integer operator or a float-to-integer conversion would leave undefined what C# defines, and the
/// Prelude function (<c>IntegerSemantics.hpp</c>) that gives the C# result instead (#722, #724).
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
/// The operator stays as it is where the result is known not to overflow: a constant (C# rejects an overflowing constant
/// outside <c>unchecked</c>), operands whose types or constant values bound the result, and the step of a for loop counter
/// that the loop condition bounds (<see cref="IsBoundedLoopStep"/>). The rest costs no instruction on the GPU: the
/// unsigned operation is the same instruction without the no-overflow assumption, and the masked shift is one funnel
/// shift (<c>tmp/handoff/issue-722-design.md</c>).
/// </para>
/// </remarks>
internal static class IntegerSemantics
{
    /// <summary>
    /// The Prelude function that <paramref name="operation"/> is written as (<c>__gsharp_add_i32(a,b)</c>), or
    /// <see langword="null"/> for an operator that C++ gives the C# result (then <see cref="GetShiftMask"/> tells whether
    /// its count is masked).
    /// </summary>
    public static string? GetBinaryHelper(IBinaryOperation operation)
    {
        if (!IsIntrinsicIntegerOperation(operation) || GetWidth(operation.Type) is not (var width and not 0) ||
            !IsSigned(operation.Type))
        {
            return null;
        }

        return operation.OperatorKind switch
        {
            BinaryOperatorKind.Add when !FitsWithoutWrap(operation) => Pick(width, Names.AddInt32, Names.AddInt64),
            BinaryOperatorKind.Subtract when !FitsWithoutWrap(operation) =>
                Pick(width, Names.SubtractInt32, Names.SubtractInt64),
            BinaryOperatorKind.Multiply when !FitsWithoutWrap(operation) =>
                Pick(width, Names.MultiplyInt32, Names.MultiplyInt64),
            BinaryOperatorKind.LeftShift => Pick(width, Names.ShiftLeftInt32, Names.ShiftLeftInt64),
            BinaryOperatorKind.UnsignedRightShift =>
                Pick(width, Names.UnsignedShiftRightInt32, Names.UnsignedShiftRightInt64),
            _ => null
        };
    }

    /// <summary>
    /// The mask of the count of a shift that stays a C++ operator (<c>&lt;&lt;</c> of uint and ulong, <c>&gt;&gt;</c>,
    /// <c>&gt;&gt;&gt;</c> of uint and ulong): 31 or 63; 0 for any other operation.
    /// </summary>
    public static int GetShiftMask(IBinaryOperation operation)
    {
        if (!IsIntrinsicIntegerOperation(operation) || GetWidth(operation.Type) is not (var width and not 0))
        {
            return 0;
        }

        var staysOperator = operation.OperatorKind switch
        {
            BinaryOperatorKind.RightShift => true,
            BinaryOperatorKind.LeftShift or BinaryOperatorKind.UnsignedRightShift => !IsSigned(operation.Type),
            _ => false
        };
        return staysOperator ? Mask(width) : 0;
    }

    /// <summary>
    /// Writes the count of a shift whose mask is <paramref name="mask"/>: a constant in range as written, any other
    /// constant masked (<c>33</c> is <c>1</c>), and anything else as <c>(&lt;count&gt;&amp;31)</c>.
    /// </summary>
    public static void AppendShiftCount(IOperation? count, ExpressionSyntax syntax, int mask, ref TranspileContext context)
    {
        if (count?.ConstantValue is { HasValue: true, Value: int constant })
        {
            if ((constant & mask) == constant)
            {
                context.Visitor.Visit(syntax);
                return;
            }

            context.Emitter.Writer.Append(constant & mask);
            return;
        }

        using (context.Emitter.Writer.Parentheses())
        {
            AppendOperand(Unparenthesize(syntax), ref context);
            context.Emitter.Writer.Append($"&{mask}");
        }
    }

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
    /// The Prelude function that writes the increment or decrement <paramref name="operation"/> of an int or long
    /// (<paramref name="node"/>), or null when the C++ operator cannot overflow there. A step whose value is not used
    /// is written as the compound assignment (<c>__gsharp_add_assign_i32(x,1)</c>), as is a prefix one; a postfix one
    /// whose value is used returns the old value (<c>__gsharp_postinc_i32(x)</c>).
    /// </summary>
    public static string? GetIncrementHelper(IIncrementOrDecrementOperation operation, ExpressionSyntax node,
        SemanticModel model)
    {
        if (operation.OperatorMethod != null || operation.IsLifted || GetWidth(operation.Type) is not (var width and not 0) ||
            !IsSigned(operation.Type) || IsBoundedLoopStep(operation, node, model))
        {
            return null;
        }

        var increment = operation.Kind == OperationKind.Increment;
        if (!operation.IsPostfix || !IsValueUsed(node, model))
        {
            return increment
                ? Pick(width, Names.AddAssignInt32, Names.AddAssignInt64)
                : Pick(width, Names.SubtractAssignInt32, Names.SubtractAssignInt64);
        }

        return increment
            ? Pick(width, Names.PostIncrementInt32, Names.PostIncrementInt64)
            : Pick(width, Names.PostDecrementInt32, Names.PostDecrementInt64);
    }

    /// <summary>
    /// The Prelude function that writes the compound assignment <paramref name="operation"/>
    /// (<c>__gsharp_add_assign_i32(x,y)</c>), and in <paramref name="binaryHelper"/> the one of its operator
    /// (<c>__gsharp_add_i32</c>), which the form that evaluates its operands into temporaries uses; null for an
    /// assignment that C++ gives the C# result (see <see cref="GetCompoundShiftMask"/>).
    /// </summary>
    public static string? GetCompoundHelper(ICompoundAssignmentOperation operation, out string? binaryHelper)
    {
        binaryHelper = null;
        var type = GetCompoundOperatorType(operation);
        if (operation.OperatorMethod != null || operation.IsLifted || GetWidth(type) is not (var width and not 0) ||
            !IsSigned(type))
        {
            return null;
        }

        (binaryHelper, var helper) = operation.OperatorKind switch
        {
            BinaryOperatorKind.Add => (Pick(width, Names.AddInt32, Names.AddInt64),
                Pick(width, Names.AddAssignInt32, Names.AddAssignInt64)),
            BinaryOperatorKind.Subtract => (Pick(width, Names.SubtractInt32, Names.SubtractInt64),
                Pick(width, Names.SubtractAssignInt32, Names.SubtractAssignInt64)),
            BinaryOperatorKind.Multiply => (Pick(width, Names.MultiplyInt32, Names.MultiplyInt64),
                Pick(width, Names.MultiplyAssignInt32, Names.MultiplyAssignInt64)),
            BinaryOperatorKind.LeftShift => (Pick(width, Names.ShiftLeftInt32, Names.ShiftLeftInt64),
                Pick(width, Names.ShiftLeftAssignInt32, Names.ShiftLeftAssignInt64)),
            BinaryOperatorKind.UnsignedRightShift => (
                Pick(width, Names.UnsignedShiftRightInt32, Names.UnsignedShiftRightInt64),
                Pick(width, Names.UnsignedShiftRightAssignInt32, Names.UnsignedShiftRightAssignInt64)),
            _ => ((string?)null, (string?)null)
        };
        return helper;
    }

    /// <summary>
    /// The mask of the count of a compound shift that stays a C++ operator (<c>&gt;&gt;=</c>, and <c>&lt;&lt;=</c> and
    /// <c>&gt;&gt;&gt;=</c> of uint and ulong): 31 or 63; 0 for any other assignment.
    /// </summary>
    public static int GetCompoundShiftMask(ICompoundAssignmentOperation operation)
    {
        var type = GetCompoundOperatorType(operation);
        if (operation.OperatorMethod != null || operation.IsLifted || GetWidth(type) is not (var width and not 0))
        {
            return 0;
        }

        var staysOperator = operation.OperatorKind switch
        {
            BinaryOperatorKind.RightShift => true,
            BinaryOperatorKind.LeftShift or BinaryOperatorKind.UnsignedRightShift => !IsSigned(type),
            _ => false
        };
        return staysOperator ? Mask(width) : 0;
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
        needsCast = helper != null && !SymbolEqualityComparer.Default.Equals(integral, target) ||
                    integral.SpecialType is SpecialType.System_SByte or SpecialType.System_Byte or
                        SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char;
        return helper;
    }

    /// <summary>
    /// Whether the C++ of <paramref name="expression"/> is a Prelude call that stands for its operator, and so begins
    /// with a name rather than with the operator or the left operand (<see cref="OperatorSpacing"/>).
    /// </summary>
    public static bool IsWrittenAsCall(ExpressionSyntax expression, SemanticModel model)
    {
        if (expression is not (BinaryExpressionSyntax or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax or
                AssignmentExpressionSyntax) ||
            model.GetSafeOperation(expression) is not { } operation || operation.Syntax.RawKind != expression.RawKind)
        {
            return false;
        }

        return operation switch
        {
            IBinaryOperation binary => GetBinaryHelper(binary) != null,
            IUnaryOperation unary => GetNegationHelper(unary) != null,
            IIncrementOrDecrementOperation step => GetIncrementHelper(step, expression, model) != null,
            ICompoundAssignmentOperation compound => GetCompoundHelper(compound, out _) != null,
            _ => false
        };
    }

    /// <summary>
    /// Writes the increment or decrement <paramref name="operation"/> (<paramref name="node"/>, of
    /// <paramref name="operand"/>), for which <see cref="GetIncrementHelper"/> gave <paramref name="referenceHelper"/>:
    /// <c>x=__gsharp_add_i32(x,1)</c> for a statement or a prefix step (parenthesized where its value is used), and
    /// <c>__gsharp_sub_i32((x=__gsharp_add_i32(x,1)),1)</c> for a postfix step whose value is used (the old value, as
    /// the step wraps). The target is written twice, so it is a location whose evaluation writes nothing.
    /// </summary>
    /// <remarks>
    /// [WHY] The target stays an expression: a <c>T&amp;</c> parameter bound to a member of a packed struct
    /// (<c>#pragma pack(1)</c>) would read it as aligned (CUDA error 716). A target whose evaluation writes something
    /// (<c>a[i++]++</c>) is passed by reference to <paramref name="referenceHelper"/> instead.
    /// </remarks>
    public static void AppendStep(IIncrementOrDecrementOperation operation, ExpressionSyntax node,
        ExpressionSyntax operand, string referenceHelper, ref TranspileContext context)
    {
        if (!CanRepeatTarget(operand, context.Query.SemanticModel))
        {
            AppendUnaryCall(referenceHelper, operand, ref context);
            return;
        }

        var width = GetWidth(operation.Type);
        var increment = operation.Kind == OperationKind.Increment;
        var step = increment
            ? Pick(width, Names.AddInt32, Names.AddInt64)
            : Pick(width, Names.SubtractInt32, Names.SubtractInt64);
        var writer = context.Emitter.Writer;
        if (!IsValueUsed(node, context.Query.SemanticModel))
        {
            AppendRepeatedAssignment(operand, step, value: null, cast: null, ref context);
            return;
        }

        if (!operation.IsPostfix)
        {
            using (writer.Parentheses())
            {
                AppendRepeatedAssignment(operand, step, value: null, cast: null, ref context);
            }

            return;
        }

        // The value before the step: the new value stepped back, which wraps back as well
        writer.Append(increment
            ? Pick(width, Names.SubtractInt32, Names.SubtractInt64)
            : Pick(width, Names.AddInt32, Names.AddInt64));
        using (writer.Parentheses())
        {
            using (writer.Parentheses())
            {
                AppendRepeatedAssignment(operand, step, value: null, cast: null, ref context);
            }

            writer.Append(",1");
        }
    }

    /// <summary>
    /// Writes the compound assignment <paramref name="operation"/> (<paramref name="node"/>), for which
    /// <see cref="GetCompoundHelper"/> gave <paramref name="referenceHelper"/> and <paramref name="binaryHelper"/>, as
    /// <c>x=(T)__gsharp_add_i32(x,y)</c> (parenthesized where its value is used; the cast narrows to a target narrower
    /// than the operator), or as <c>__gsharp_add_assign_i32(x,y)</c> when evaluating the target writes something
    /// (see <see cref="AppendStep"/>).
    /// </summary>
    public static void AppendCompound(ICompoundAssignmentOperation operation, AssignmentExpressionSyntax node,
        string referenceHelper, string binaryHelper, ref TranspileContext context)
    {
        if (!CanRepeatTarget(node.Left, context.Query.SemanticModel))
        {
            AppendCall(referenceHelper, node.Left, node.Right, ref context);
            return;
        }

        var cast = operation.Target.Type is { } target && target.SpecialType != GetCompoundOperatorType(operation)
            ? target
            : null;
        if (!IsValueUsed(node, context.Query.SemanticModel))
        {
            AppendRepeatedAssignment(node.Left, binaryHelper, node.Right, cast, ref context);
            return;
        }

        using (context.Emitter.Writer.Parentheses())
        {
            AppendRepeatedAssignment(node.Left, binaryHelper, node.Right, cast, ref context);
        }
    }

    // <target>=(<cast>)<helper>(<target>,<value or 1>)
    private static void AppendRepeatedAssignment(ExpressionSyntax target, string helper, ExpressionSyntax? value,
        ITypeSymbol? cast, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        context.Visitor.Visit(target);
        writer.Append('=');
        if (cast != null)
        {
            using (writer.Parentheses())
            {
                context.Emitter.Dialect.AppendType(ref context, cast);
            }
        }

        writer.Append(helper);
        using (writer.Parentheses())
        {
            context.Visitor.Visit(Unparenthesize(target));
            writer.Append(',');
            if (value == null)
            {
                writer.Append('1');
            }
            else
            {
                context.Visitor.Visit(Unparenthesize(value));
            }
        }
    }

    // Whether the target of a step can be written twice: finding the location writes nothing
    private static bool CanRepeatTarget(ExpressionSyntax target, SemanticModel model) =>
        ExpressionEffects.OfLocation(target, model).Writes.IsEmpty;

    /// <summary>Writes <c>&lt;helper&gt;(&lt;left&gt;,&lt;right&gt;)</c>.</summary>
    public static void AppendCall(string helper, ExpressionSyntax left, ExpressionSyntax right,
        ref TranspileContext context)
    {
        context.Emitter.Writer.Append(helper);
        using (context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(Unparenthesize(left));
            context.Emitter.Writer.Append(',');
            context.Visitor.Visit(Unparenthesize(right));
        }
    }

    /// <summary>
    /// Writes <c>&lt;helper&gt;(&lt;operand&gt;)</c> (a negation, a postfix step), or <c>&lt;helper&gt;(&lt;operand&gt;,1)</c>
    /// for a step written as a compound assignment (<see cref="GetIncrementHelper"/>).
    /// </summary>
    public static void AppendUnaryCall(string helper, ExpressionSyntax operand, ref TranspileContext context)
    {
        context.Emitter.Writer.Append(helper);
        using (context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(Unparenthesize(operand));
            if (IsAssignment(helper))
            {
                context.Emitter.Writer.Append(",1");
            }
        }
    }

    private static bool IsAssignment(string helper) =>
        helper is Names.AddAssignInt32 or Names.AddAssignInt64 or Names.SubtractAssignInt32 or Names.SubtractAssignInt64;

    // An argument of a call needs no parentheses of its own: (i+1)*2 is __gsharp_mul_i32(__gsharp_add_i32(i,1),2)
    private static ExpressionSyntax Unparenthesize(ExpressionSyntax syntax)
    {
        while (syntax is ParenthesizedExpressionSyntax parenthesized)
        {
            syntax = parenthesized.Expression;
        }

        return syntax;
    }

    /// <summary>
    /// Writes <paramref name="syntax"/> as the left operand of <c>&amp;</c>, parenthesized when its C++ binds more
    /// loosely (<c>?:</c>, an assignment, <c>|</c>, <c>^</c>, <c>&amp;&amp;</c>, <c>||</c>, <c>??</c>).
    /// </summary>
    private static void AppendOperand(ExpressionSyntax syntax, ref TranspileContext context)
    {
        var loose = syntax is ConditionalExpressionSyntax or AssignmentExpressionSyntax or
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.BitwiseOrExpression or (int)SyntaxKind.ExclusiveOrExpression or
                (int)SyntaxKind.LogicalAndExpression or (int)SyntaxKind.LogicalOrExpression or
                (int)SyntaxKind.CoalesceExpression };
        if (!loose)
        {
            context.Visitor.Visit(syntax);
            return;
        }

        using (context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(syntax);
        }
    }

    // A predefined, non-lifted operator on integers without a constant result: a constant cannot have overflowed
    // (CS0220). The left operand has the type of the operator (converted to it), which a pointer difference
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

    private static bool TryGetInteger(object value, out BigInteger integer)
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

    /// <summary>
    /// Whether <paramref name="node"/> is the step <c>i++</c> / <c>++i</c> (<c>i--</c> / <c>--i</c>) of a for loop whose
    /// condition is <c>i &lt; e</c> (<c>i &gt; e</c>) compared in the type of <c>i</c>, or <c>i &lt;= c</c>
    /// (<c>i &gt;= c</c>) with a constant short of the end of the type, and where nothing else writes <c>i</c>: the
    /// condition held for the value that the step adds 1 to, so that value is below the maximum and the step cannot
    /// overflow. Kept as the C++ operator, it keeps nvcc's induction variable widening (naive SGEMM: 202 SASS
    /// instructions with it, 70 without; 11.97 ms against 12.95 ms).
    /// </summary>
    /// <remarks>
    /// "Nothing else writes i": not the body, the condition or another step (Roslyn data flow), no lambda or local
    /// function of the method writes it, and no ref, out or address of it is taken anywhere in the method (a ref local
    /// could write it in the body). A lowered tree (a CPU state machine) is not analyzed, and its steps wrap.
    /// </remarks>
    private static bool IsBoundedLoopStep(IIncrementOrDecrementOperation operation, ExpressionSyntax node,
        SemanticModel model)
    {
        if (node.Parent is not ForStatementSyntax { Condition: { } condition } loop || model.SyntaxTree != node.SyntaxTree ||
            GetCounter(operation.Target) is not { } counter ||
            model.GetOperation(condition) is not IBinaryOperation { OperatorMethod: null, IsLifted: false } comparison ||
            !IsBoundingComparison(comparison, counter, operation.Kind == OperationKind.Increment))
        {
            return false;
        }

        if (WritesCounter(model.AnalyzeDataFlow(loop.Statement), counter) ||
            WritesCounter(model.AnalyzeDataFlow(condition), counter))
        {
            return false;
        }

        foreach (var incrementor in loop.Incrementors)
        {
            if (incrementor != node && WritesCounter(model.AnalyzeDataFlow(incrementor), counter))
            {
                return false;
            }
        }

        return GetMethodBody(loop) is { } body &&
               !body.DescendantNodes()
                   .AsStackEnumerable()
                   .Any((model, counter), static (n, state) => TakesReference(n, state.model, state.counter) ||
                                                               WritesCaptured(n, state.model, state.counter));
    }

    private static ISymbol? GetCounter(IOperation target) => target switch
    {
        ILocalReferenceOperation { Local: { RefKind: RefKind.None } local } => local,
        IParameterReferenceOperation { Parameter: { RefKind: RefKind.None } parameter } => parameter,
        _ => null
    };

    // i < e (or e > i) for a step up, i > e (or e < i) for a step down, compared in the type of i; <= and >= only
    // against a constant short of the end of the type
    private static bool IsBoundingComparison(IBinaryOperation comparison, ISymbol counter, bool increment)
    {
        BinaryOperatorKind kind;
        IOperation bound;
        if (IsCounter(comparison.LeftOperand, counter))
        {
            (kind, bound) = (comparison.OperatorKind, comparison.RightOperand);
        }
        else if (IsCounter(comparison.RightOperand, counter))
        {
            (kind, bound) = (Flip(comparison.OperatorKind), comparison.LeftOperand);
        }
        else
        {
            return false;
        }

        var type = comparison.LeftOperand.Type;
        if (type is null || GetWidth(type) is not (var width and not 0) || !IsSigned(type) ||
            !SymbolEqualityComparer.Default.Equals(type, comparison.RightOperand.Type))
        {
            return false;
        }

        var constant = bound.ConstantValue is { HasValue: true, Value: { } value } && TryGetInteger(value, out var c)
            ? c
            : (BigInteger?)null;
        return (kind, increment) switch
        {
            (BinaryOperatorKind.LessThan, true) => true,
            (BinaryOperatorKind.LessThanOrEqual, true) => constant < MaxValue(width),
            (BinaryOperatorKind.GreaterThan, false) => true,
            (BinaryOperatorKind.GreaterThanOrEqual, false) => constant > MinValue(width),
            _ => false
        };
    }

    private static bool IsCounter(IOperation operand, ISymbol counter) =>
        GetCounter(operand) is { } symbol && SymbolEqualityComparer.Default.Equals(symbol, counter);

    private static BinaryOperatorKind Flip(BinaryOperatorKind kind) => kind switch
    {
        BinaryOperatorKind.LessThan => BinaryOperatorKind.GreaterThan,
        BinaryOperatorKind.LessThanOrEqual => BinaryOperatorKind.GreaterThanOrEqual,
        BinaryOperatorKind.GreaterThan => BinaryOperatorKind.LessThan,
        BinaryOperatorKind.GreaterThanOrEqual => BinaryOperatorKind.LessThanOrEqual,
        _ => kind
    };

    private static bool WritesCounter(DataFlowAnalysis? flow, ISymbol counter) =>
        flow is not { Succeeded: true } || flow.WrittenInside.Contains(counter) ||
        flow.UnsafeAddressTaken.Contains(counter);

    // The body of the method that holds the loop, through the lambdas and local functions around it: a delegate made
    // anywhere in the method may write a counter it captures
    private static SyntaxNode? GetMethodBody(SyntaxNode node)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            if (current is BaseMethodDeclarationSyntax method)
            {
                return (SyntaxNode?)method.Body ?? method.ExpressionBody?.Expression;
            }
        }

        return null;
    }

    // A lambda or local function that writes i (it may run in the loop body); one that only reads it does not matter
    private static bool WritesCaptured(SyntaxNode node, SemanticModel model, ISymbol counter)
    {
        var body = node switch
        {
            AnonymousFunctionExpressionSyntax lambda => lambda.Body,
            LocalFunctionStatementSyntax { Body: { } block } => block,
            LocalFunctionStatementSyntax { ExpressionBody.Expression: { } expression } => expression,
            _ => null
        };
        return body != null && WritesCounter(model.AnalyzeDataFlow(body), counter);
    }

    // ref i, out i, ref i as a ref local initializer, &i
    private static bool TakesReference(SyntaxNode node, SemanticModel model, ISymbol counter)
    {
        var expression = node switch
        {
            ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.RefKeyword or (int)SyntaxKind.OutKeyword } argument =>
                argument.Expression,
            RefExpressionSyntax reference => reference.Expression,
            PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.AddressOfExpression } address => address.Operand,
            _ => null
        };
        return expression is IdentifierNameSyntax &&
               SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(expression).Symbol, counter);
    }

    // Whether the value of an increment or assignment is read: not when it is a statement, a part of a for loop or the
    // body of a lambda that returns nothing (the body of Gpu.ForUnroll(0, 4, i => sum += i))
    private static bool IsValueUsed(ExpressionSyntax node, SemanticModel model) => node.Parent switch
    {
        ExpressionStatementSyntax or ForStatementSyntax => false,
        AnonymousFunctionExpressionSyntax lambda =>
            model.GetSymbol(lambda) is not IMethodSymbol { ReturnsVoid: true },
        _ => true
    };

    // 32 or 64 for int, uint, long and ulong; 0 for any other type
    private static int GetWidth(ITypeSymbol? type) => GetWidth(type?.SpecialType ?? SpecialType.None);

    private static int GetWidth(SpecialType type) => type switch
    {
        SpecialType.System_Int32 or SpecialType.System_UInt32 => 32,
        SpecialType.System_Int64 or SpecialType.System_UInt64 => 64,
        _ => 0
    };

    private static bool IsSigned(ITypeSymbol? type) => IsSigned(type?.SpecialType ?? SpecialType.None);

    private static bool IsSigned(SpecialType type) => type is SpecialType.System_Int32 or SpecialType.System_Int64;

    private static int Mask(int width) => width - 1;

    private static string Pick(int width, string int32, string int64) => width == 32 ? int32 : int64;

    private static BigInteger MinValue(int width) => width == 32 ? int.MinValue : long.MinValue;

    private static BigInteger MaxValue(int width) => width == 32 ? int.MaxValue : long.MaxValue;
}
