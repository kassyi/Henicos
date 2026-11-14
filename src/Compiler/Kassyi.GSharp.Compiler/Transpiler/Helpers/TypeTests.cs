namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

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

    /// <summary>The plan of the opposite test (<c>is not</c>); an unsupported plan stays unsupported.</summary>
    public static TypeTestPlan Negate(in TypeTestPlan plan) => plan.Kind switch
    {
        TypeTestKind.Constant => plan with { Value = !plan.Value },
        TypeTestKind.NotNull => plan with { Negated = !plan.Negated },
        _ => plan
    };

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
                NullRepresentation.AppendNullTest(operand, isNull: plan.Negated, ref context);
                return true;
            case TypeTestKind.Operand:
                context.Visitor.Visit(operand);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// The plan of <paramref name="pattern"/>, the declaration pattern of <c>x is T v</c> or <c>x is not T v</c>
    /// (negated for <c>is not</c>), with the <c>is</c> expression; null for a declaration pattern anywhere else (a
    /// switch case, a nested pattern), which no rule writes, and for a variable that is not of the operand's own type.
    /// </summary>
    public static (TypeTestPlan Plan, IsPatternExpressionSyntax Owner)? PlanDeclaration(
        DeclarationPatternSyntax pattern, SemanticModel model)
    {
        var negated = pattern.Parent is UnaryPatternSyntax { RawKind: (int)SyntaxKind.NotPattern };
        if ((negated ? pattern.Parent?.Parent : pattern.Parent) is not IsPatternExpressionSyntax owner)
        {
            return null;
        }

        var operand = model.GetTypeSymbol(owner.Expression);
        var declared = model.GetTypeSymbol(pattern.Type);
        var plan = Plan(false, operand, declared, model.Compilation);
        var isNullable = operand is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            TypeArguments: [var underlying]
        };

        // The variable has the operand's own type, or the underlying type of a nullable: a variable of an interface or
        // a base type is a reference to a heap object
        var isOwnType = operand is not null && declared is not null &&
                        (declared.IsEqualTo(operand) || (isNullable && declared.IsEqualTo(underlying)));
        if (plan.Kind == TypeTestKind.Unsupported || !isOwnType)
        {
            return null;
        }

        plan = plan with
        {
            AssignsVariable = plan.Kind != TypeTestKind.Constant || plan.Value,
            UnwrapsNullable = isNullable && declared!.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
        };
        return (negated ? Negate(in plan) : plan, owner);
    }

    /// <summary>
    /// Writes <c>([&amp;]() { auto __gsharp_seq_n = &lt;operand&gt;; &lt;name&gt; = &lt;value&gt;; return &lt;test&gt;; }())</c>
    /// for <c>x is T name</c> through <see cref="EvaluationOrder"/>: the operand is evaluated once, the variable
    /// (declared before the statement) gets its value when the test holds, and the sequence gives the test.
    /// </summary>
    public static void AppendDeclaration(in TypeTestPlan plan, ExpressionSyntax operand,
        SingleVariableDesignationSyntax designation, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        using var sequence = EvaluationOrder.BeginSequence(writer);
        var temp = EvaluationOrder.DeclareTemp(ref context, operand, reference: false);
        if (plan.AssignsVariable)
        {
            CppNameResolver.AppendDeclaredName(designation, designation.Identifier, ref context);
            writer.Append(" = ");
            EvaluationOrder.AppendTempName(writer, temp);
            writer.AppendIf(plan.UnwrapsNullable, ".Value");
            writer.AppendLine(";");
        }

        sequence.Return();
        if (plan.Kind != TypeTestKind.NotNull)
        {
            writer.Append(plan.Value ? "true" : "false");
            return;
        }

        writer.AppendIf(plan.Negated, "!(");
        EvaluationOrder.AppendTempName(writer, temp);
        writer.Append(plan.OperandIsNullable ? ".HasValue" : ".data != nullptr");
        writer.AppendIf(plan.Negated, ")");
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
            ? new TypeTestPlan(TypeTestKind.NotNull, OperandIsNullable: true)
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
