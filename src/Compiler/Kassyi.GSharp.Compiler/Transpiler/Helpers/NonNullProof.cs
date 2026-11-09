namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// The arrays that device code may read without a null check because a boundary has already checked them (#1409): the
/// C# nullable annotation of a parameter or field is the contract, and the launcher (for kernel arguments) or the caller
/// (for device function arguments) enforces it before the value reaches the code that reads it.
/// </summary>
/// <remarks>
/// <para>
/// The contract: an array declared non-nullable (<c>T[]</c>, <see cref="NullableAnnotation.NotAnnotated"/>) holds an
/// array. A nullable (<c>T[]?</c>) or oblivious (<c>#nullable disable</c>) one may hold null. The launchers reject a
/// null in a non-nullable kernel array parameter and in a non-nullable array field that a by-value struct argument
/// reaches through struct fields (<see cref="GetNonNullableArrayFields"/>); a call of a device function checks such an
/// argument once, at the call, when the caller has not proved it (<see cref="NeedsBoundaryCheck"/>). C# throws only
/// when such a null is read; G# throws at the boundary, earlier, and only for a null in a non-nullable place.
/// </para>
/// <para>
/// The proof (<see cref="IsProvenNonNull"/>): an array is non-null when it is a stack array (a local array of a
/// constant size), a parameter of that kind that nothing in its method writes, a non-nullable array field of a proven
/// struct, or a local that nothing writes after an initializer that is proven. A proven struct is a by-value struct
/// parameter that nothing writes, a struct field of a proven struct, or a local that nothing writes after an
/// initializer that is a proven struct. An element of an array is never proven: the launchers do not walk the
/// elements.
/// </para>
/// </remarks>
internal static class NonNullProof
{
    /// <summary>Whether <paramref name="expression"/>, of an array type, cannot be null where it is read.</summary>
    public static bool IsProvenNonNull(ExpressionSyntax expression, SemanticModel model)
    {
        expression = SkipParentheses(expression);

        // A stack array is storage of the thread (a C array on the device), never null (#1543)
        if (StackArrayLocals.IsStackArrayReference(expression, model))
        {
            return true;
        }

        return expression switch
        {
            ConditionalExpressionSyntax conditional =>
                IsProvenNonNull(conditional.WhenTrue, model) && IsProvenNonNull(conditional.WhenFalse, model),
            MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.SimpleMemberAccessExpression } access =>
                IsProvenField(access, model),
            IdentifierNameSyntax identifier => IsProvenIdentifier(identifier, model),
            _ => false
        };
    }

    // s.Data: a non-nullable array field of a proven struct
    private static bool IsProvenField(MemberAccessExpressionSyntax access, SemanticModel model) =>
        model.GetSymbol(access) is IFieldSymbol { IsStatic: false, ContainingType.IsValueType: true } field &&
        IsNonNullableArray(field.Type, field.NullableAnnotation) &&
        IsProvenStruct(access.Expression, model);

    // A non-nullable array parameter that the launch or the call checked, or an unwritten local with a proven
    // initializer
    private static bool IsProvenIdentifier(IdentifierNameSyntax identifier, SemanticModel model) =>
        model.GetSymbol(identifier) switch
        {
            IParameterSymbol parameter => IsNonNullableArray(parameter.Type, parameter.NullableAnnotation) &&
                                          IsCheckedParameter(parameter, model.Compilation),
            ILocalSymbol local => IsUnwrittenLocal(local, model.Compilation, out var initializer,
                                      out var initializerModel) &&
                                  IsProvenNonNull(initializer, initializerModel),
            _ => false
        };

    /// <summary>
    /// Whether <paramref name="expression"/>, of a struct type, holds an array in every non-nullable array field it
    /// reaches through struct fields.
    /// </summary>
    public static bool IsProvenStruct(ExpressionSyntax expression, SemanticModel model)
    {
        expression = SkipParentheses(expression);
        switch (expression)
        {
            case MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.SimpleMemberAccessExpression } access:
                return model.GetSymbol(access) is IFieldSymbol { IsStatic: false, ContainingType.IsValueType: true, Type.IsValueType: true } &&
                       IsProvenStruct(access.Expression, model);
            case IdentifierNameSyntax identifier:
                return model.GetSymbol(identifier) switch
                {
                    IParameterSymbol { Type.IsValueType: true } parameter => IsCheckedParameter(parameter, model.Compilation),
                    ILocalSymbol { Type.IsValueType: true } local =>
                        IsUnwrittenLocal(local, model.Compilation, out var initializer, out var initializerModel) &&
                        IsProvenStruct(initializer, initializerModel),
                    _ => false
                };
            default:
                return false;
        }
    }

    /// <summary>The GSH0003 text for a method group of a device function whose calls check their arguments.</summary>
    public const string PassedAsDelegate =
        "a [GpuDeviceFunction] with a non-nullable array parameter (or a struct parameter that holds one) passed as a delegate";

    /// <summary>
    /// Records a failure (GSH0003) when <paramref name="reference"/> names <paramref name="method"/> without calling it
    /// (a method group) and a call of the method checks its arguments: a call through a delegate cannot check them, and
    /// the method reads them without a check.
    /// </summary>
    public static void FailIfReferencedWithoutCall(ExpressionSyntax reference, IMethodSymbol method,
        ref TranspileContext context)
    {
        var called = reference.Parent is InvocationExpressionSyntax invocation && invocation.Expression == reference ||
                     reference.Parent is MemberAccessExpressionSyntax access && access.Name == reference &&
                     access.Parent is InvocationExpressionSyntax outer && outer.Expression == access;
        if (!called && !method.IsExtensionMethod &&
            CppNameResolver.IsEmittedDeviceFunction(method, context.Emitter.Dialect) && ChecksArguments(method))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, reference, PassedAsDelegate);
        }
    }

    /// <summary>Whether <paramref name="type"/> with <paramref name="annotation"/> is a non-nullable array.</summary>
    public static bool IsNonNullableArray(ITypeSymbol type, NullableAnnotation annotation) =>
        type is IArrayTypeSymbol && annotation == NullableAnnotation.NotAnnotated;

    /// <summary>
    /// The non-nullable array fields that a value of <paramref name="type"/> reaches through struct fields, each as the
    /// chain of fields from the value (empty for a type that is not a struct or has none).
    /// </summary>
    public static ImmutableArray<ImmutableArray<IFieldSymbol>> GetNonNullableArrayFields(ITypeSymbol type)
    {
        var paths = ImmutableArray.CreateBuilder<ImmutableArray<IFieldSymbol>>();
        CollectFields(type, ImmutableArray<IFieldSymbol>.Empty, paths, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
        return paths.ToImmutable();
    }

    private static void CollectFields(ITypeSymbol type, ImmutableArray<IFieldSymbol> prefix,
        ImmutableArray<ImmutableArray<IFieldSymbol>>.Builder paths, HashSet<ITypeSymbol> visiting)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } structType ||
            structType.ContainingNamespace.IsSystemOrNumerics() ||
            !visiting.Add(structType))
        {
            return;
        }

        foreach (var member in structType.GetMembers())
        {
            if (member is IFieldSymbol { IsStatic: false, IsConst: false } field)
            {
                AddFieldPath(field, prefix, paths, visiting);
            }
        }

        visiting.Remove(structType);
    }

    // A non-nullable array field ends a path; a struct field is followed into its own fields
    private static void AddFieldPath(IFieldSymbol field, ImmutableArray<IFieldSymbol> prefix,
        ImmutableArray<ImmutableArray<IFieldSymbol>>.Builder paths, HashSet<ITypeSymbol> visiting)
    {
        if (IsNonNullableArray(field.Type, field.NullableAnnotation))
        {
            paths.Add(prefix.Add(field));
        }
        else if (field.Type.IsValueType)
        {
            CollectFields(field.Type, prefix.Add(field), paths, visiting);
        }
    }

    /// <summary>
    /// Whether an argument for <paramref name="parameter"/> of a device function is checked at the call: a by-value
    /// non-nullable array, or a by-value struct that reaches a non-nullable array field.
    /// </summary>
    public static bool NeedsBoundaryCheck(IParameterSymbol parameter) =>
        parameter.RefKind == RefKind.None &&
        (IsNonNullableArray(parameter.Type, parameter.NullableAnnotation) ||
         parameter.Type.IsValueType && GetNonNullableArrayFields(parameter.Type).Length > 0);

    /// <summary>Whether a call of <paramref name="method"/> checks some of its arguments (<see cref="NeedsBoundaryCheck"/>).</summary>
    public static bool ChecksArguments(IMethodSymbol method)
    {
        foreach (var parameter in method.Parameters)
        {
            if (NeedsBoundaryCheck(parameter))
            {
                return true;
            }
        }

        return false;
    }

    // A by-value parameter that nothing in its method writes, of a kernel (the launcher checked it) or of a device
    // function whose calls all check it: an emitted device function that is not an extension method (a method group of
    // one with such a parameter is rejected; a suspending call checks its arguments in the lowered resume call too)
    private static bool IsCheckedParameter(IParameterSymbol parameter, Compilation compilation)
    {
        // A parameter with a default value takes it when the call omits the argument, unchecked
        if (parameter is not { RefKind: RefKind.None, HasExplicitDefaultValue: false, ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.Ordinary, IsStatic: true } method })
        {
            return false;
        }

        var isKernel = method.HasAttribute(KnownAttributes.GpuKernel);
        var isDeviceFunction = !isKernel &&
                               method.OriginalDefinition.HasAttribute(KnownAttributes.GpuDeviceFunction) &&
                               !method.IsExtensionMethod;
        return (isKernel || isDeviceFunction) && !VariableWrites.IsWritten(parameter, compilation);
    }

    // A local that nothing writes after its declaration, with the initializer it was declared with
    private static bool IsUnwrittenLocal(ILocalSymbol local, Compilation compilation,
        [NotNullWhen(true)] out ExpressionSyntax? initializer, [NotNullWhen(true)] out SemanticModel? initializerModel)
    {
        initializer = null;
        initializerModel = null;
        if (local is not { IsRef: false, IsConst: false } ||
            local.DeclaringSyntaxReferences is not [var reference] ||
            reference.GetSyntax() is not VariableDeclaratorSyntax { Initializer.Value: { } value } ||
            VariableWrites.IsWritten(local, compilation))
        {
            return false;
        }

        initializer = value;
        initializerModel = compilation.GetCachedSemanticModel(value.SyntaxTree);
        return true;
    }

    // Parentheses, and the null-forgiving operator, which has no run-time meaning (a! is proven when a is)
    private static ExpressionSyntax SkipParentheses(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    break;
                case PostfixUnaryExpressionSyntax suppressed
                    when suppressed.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = suppressed.Operand;
                    break;
                default:
                    return expression;
            }
        }
    }
}
