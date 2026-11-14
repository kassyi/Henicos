namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>What a type test or <c>as</c> is written as (<see cref="TypeTests.Plan"/>).</summary>
internal enum TypeTestKind
{
    /// <summary>The constant that the types decide; the operand is still evaluated.</summary>
    Constant,

    /// <summary>Whether the operand is not null: its array or nullable has a value.</summary>
    NotNull,

    /// <summary>The operand itself: <c>as</c> with an identity conversion gives the value, null as null.</summary>
    Operand,

    /// <summary>A form that needs run-time type information or a heap object (GSH0003).</summary>
    Unsupported
}

/// <summary>What device code writes for a type test or <c>as</c>: the kind, and the constant of a constant test.</summary>
/// <param name="Kind">How the test is written.</param>
/// <param name="Value">The constant of <see cref="TypeTestKind.Constant"/>.</param>
internal readonly record struct TypeTestPlan(TypeTestKind Kind, bool Value = false);

/// <summary>
/// What device code does with <c>x is T</c>, <c>x is T</c> as a type pattern and <c>x as T</c>. The static types decide
/// every test that C# can decide without the run-time type of an object: a value is its own type (the constant), an
/// array or nullable that matches its type is a null test, and <c>as</c> with the identity is the operand. A test that
/// needs the run-time type of an object (an operand of a base or interface type, a conversion to one) needs a heap
/// object (GSH0003).
/// </summary>
/// <remarks>
/// A value type has no run-time type that differs from its static type. <c>v is T</c> holds when <c>T</c> is the type
/// itself, its nullable (<c>int is int?</c>), or a type that boxes it (an interface it implements, <c>object</c>,
/// <c>ValueType</c>); <c>int is long</c> is false, as it is in C#, since the test looks at the boxed value. An array
/// or nullable is null or has a value, so its test is a null test: <c>a is int[]</c> is <c>a != null</c>,
/// <c>n is int</c> is <c>n.HasValue</c>, and <c>a as int[]</c> is <c>a</c>.
/// </remarks>
internal static class TypeTests
{
    /// <summary>The GSH0003 text for a test or conversion that needs the run-time type of a heap object.</summary>
    public const string RuntimeTypeNeeded =
        "type test or 'as' conversion that needs the run-time type of a heap object (a reference of a base type or an " +
        "interface, or a conversion to one)";

    /// <summary>The plan for <c>operand is target</c> (<paramref name="isAs"/> false) or <c>operand as target</c>.</summary>
    public static TypeTestPlan Plan(bool isAs, ITypeSymbol? operand, ITypeSymbol? target, Compilation compilation)
    {
        var unsupported = new TypeTestPlan(TypeTestKind.Unsupported);
        if (operand is null || target is null)
        {
            return unsupported;
        }

        if (operand.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            operand is INamedTypeSymbol { TypeArguments: [var underlyingOperand] })
        {
            return PlanNullable(isAs, operand, underlyingOperand, target, compilation);
        }

        if (operand is { IsValueType: true, TypeKind: not TypeKind.TypeParameter })
        {
            return isAs ? unsupported : new TypeTestPlan(TypeTestKind.Constant, Boxes(operand, target, compilation));
        }

        // An array of the same type: the test is whether it is there, the conversion is the array
        return operand is IArrayTypeSymbol && operand.IsEqualTo(target)
            ? new TypeTestPlan(isAs ? TypeTestKind.Operand : TypeTestKind.NotNull)
            : unsupported;
    }

    /// <summary>
    /// Writes <paramref name="plan"/> for <paramref name="operand"/>; false, writing nothing, for an unsupported plan.
    /// </summary>
    public static bool TryAppend(in TypeTestPlan plan, ExpressionSyntax operand, ref TranspileContext context)
    {
        switch (plan.Kind)
        {
            case TypeTestKind.Constant:
                AppendConstant(plan.Value, operand, ref context);
                return true;
            case TypeTestKind.NotNull:
                NullRepresentation.AppendNullTest(operand, isNull: false, ref context);
                return true;
            case TypeTestKind.Operand:
                context.Visitor.Visit(operand);
                return true;
            default:
                return false;
        }
    }

    // n is U, n is U? and n is I (an interface that U implements) are n.HasValue; n as U? is n; n is long is false
    private static TypeTestPlan PlanNullable(bool isAs, ITypeSymbol operand, ITypeSymbol underlying,
        ITypeSymbol target, Compilation compilation)
    {
        if (isAs)
        {
            return new TypeTestPlan(operand.IsEqualTo(target) ? TypeTestKind.Operand : TypeTestKind.Unsupported);
        }

        return Boxes(underlying, target, compilation)
            ? new TypeTestPlan(TypeTestKind.NotNull)
            : new TypeTestPlan(TypeTestKind.Constant, false);
    }

    // Whether a boxed value of <value> is a <target>: the type itself, its nullable, or a type it boxes to
    private static bool Boxes(ITypeSymbol value, ITypeSymbol target, Compilation compilation)
    {
        var underlying = target is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } named
            ? named.TypeArguments[0]
            : target;
        if (underlying.IsEqualTo(value))
        {
            return true;
        }

        var conversion = compilation.ClassifyConversion(value, target);
        return conversion.IsBoxing || conversion.IsIdentity;
    }

    // ((void)(<operand>),<constant>): C# evaluates the operand, and the types decide the test
    private static void AppendConstant(bool value, ExpressionSyntax operand, ref TranspileContext context)
    {
        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Writer.Append("(void)");
            using (context.Emitter.Writer.Parentheses())
            {
                context.Visitor.Visit(operand);
            }

            context.Emitter.Writer.Append(value ? ",true" : ",false");
        }
    }
}
