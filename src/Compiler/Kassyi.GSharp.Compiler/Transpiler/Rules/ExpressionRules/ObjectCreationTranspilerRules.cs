namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles explicit object creation expressions (<c>new MyStruct(...)</c>) into target dialect C++ struct constructor calls or brace initializers.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class ObjectCreationExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<ObjectCreationExpressionSyntax>
{
    /// <inheritdoc />
    public override void Transpile(ObjectCreationExpressionSyntax node, ref TranspileContext context) =>
        ObjectCreationHelper.TranspileObjectCreation(node, ref context);
}

/// <summary>
/// Transpiles target-typed implicit object creation expressions (<c>new(...)</c>) into target dialect C++ struct initializers.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class ImplicitObjectCreationExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<ImplicitObjectCreationExpressionSyntax>
{
    /// <inheritdoc />
    public override void Transpile(ImplicitObjectCreationExpressionSyntax node, ref TranspileContext context) =>
        ObjectCreationHelper.TranspileObjectCreation(node, ref context);
}

/// <summary>
/// Internal helper facilitating shared struct instantiation and brace-enclosed initialization logic.
/// </summary>
internal static class ObjectCreationHelper
{
    public static void TranspileObjectCreation(BaseObjectCreationExpressionSyntax node, ref TranspileContext context)
    {
        var typeSymbol = StructTranspiler.SubstituteTypeParameters(
            context.Query.SemanticModel.GetTypeSymbol(node), context.Emitter.CurrentStructType,
            context.Query.SemanticModel.Compilation);
        if (!GpuCodeConstraints.IsSupportedObjectCreation(typeSymbol))
        {
            context.Reporter.Fail(GSharpDiagnostics.ObjectCreationNotSupported, node);
            context.Visitor.AppendUnsupportedPlaceholder(node);
            return;
        }

        // An unresolved type is the C# compiler's error
        if (typeSymbol is null or IErrorTypeSymbol || typeSymbol.TypeKind == TypeKind.Error)
        {
            return;
        }

        var initializer = node.Initializer;
        var argumentList = node.ArgumentList;

        if (typeSymbol.IsSharedMemoryType())
        {
            if (typeSymbol is not INamedTypeSymbol namedType || namedType.TypeArguments.Length != 1)
            {
                return;
            }

            using (context.Emitter.Writer.Braces())
            {
                context.Emitter.Writer.Append(' ');
                SharedMemorySiteKey.AppendCpuAllocation(node, namedType.TypeArguments[0], ref context);
                context.Emitter.Writer.Append(' ');
            }
            return;
        }

        var constructor = context.Query.SemanticModel.GetSymbol(node) as IMethodSymbol;
        if (constructor != null && typeSymbol is INamedTypeSymbol { IsGenericType: true } constructedStruct &&
            !constructedStruct.IsEqualTo(constructor.ContainingType))
        {
            foreach (var candidate in constructedStruct.InstanceConstructors)
            {
                if (candidate.OriginalDefinition.IsEqualTo(constructor.OriginalDefinition))
                {
                    constructor = candidate;
                    break;
                }
            }
        }

        // IsSupportedObjectCreation leaves only a struct here
        if (initializer != null)
        {
            TranspileStructCreationWithInitializer(node, typeSymbol, constructor, initializer, argumentList,
                ref context);
        }
        else
        {
            AppendStructCreation(node, typeSymbol, constructor, argumentList, ref context);
        }
    }

    // How `new T(args)` reaches the device
    private enum StructCreationKind
    {
        // A user constructor with a body, or a primary constructor: __gsharp_ctor_...(args) (StructTranspiler)
        Factory,

        // A constructor that the dialect YAML maps to a function: <mapped>(args) (Vector3(float), ...)
        Mapped,

        // Aggregate initialization T{args}: no arguments, or a constructor whose parameters are the members in order
        Braces,

        // Neither: reported (GSH0003), since T{args} would store the arguments in the members in order (#499)
        Unsupported
    }

    private static StructCreationKind Classify(ITypeSymbol typeSymbol, IMethodSymbol? constructor,
        ref TranspileContext context, out string mappedFunction)
    {
        mappedFunction = string.Empty;
        if (constructor != null && StructTranspiler.IsEmittedStructConstructor(constructor))
        {
            return StructCreationKind.Factory;
        }

        if (constructor is null or { Parameters.Length: 0 })
        {
            return StructCreationKind.Braces;
        }

        if (context.Emitter.Dialect.TryMapIntrinsic(constructor, out mappedFunction))
        {
            return StructCreationKind.Mapped;
        }

        return IsPositionalConstructor(typeSymbol, constructor)
            ? StructCreationKind.Braces
            : StructCreationKind.Unsupported;
    }

    // [WHY] A constructor from metadata has no body to emit, so T{args} is right only where G# knows the meaning: the
    // component constructors of the vector types (System.Numerics.Vector2/3/4, Kassyi.GSharp.Core.Float3 ...), whose
    // parameters are the fields in order; Nullable<T>(T), which the C++ constructor of the device type takes; and a
    // span over a pointer and a length, or over an array, which the C++ constructors of GSharpArray take.
    private static bool IsPositionalConstructor(ITypeSymbol typeSymbol, IMethodSymbol constructor)
    {
        var parameters = constructor.Parameters;
        if (typeSymbol.IsSystemNumericsVector() && typeSymbol is INamedTypeSymbol vectorType)
        {
            var index = 0;
            foreach (var member in StructAbiMembers.Get(vectorType))
            {
                if (index >= parameters.Length || !parameters[index].Type.IsEqualTo(member.Type))
                {
                    return false;
                }

                index++;
            }

            return index == parameters.Length;
        }

        if (typeSymbol.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return parameters.Length == 1;
        }

        if (typeSymbol.IsArrayLikeType())
        {
            return parameters switch
            {
                [{ Type: IPointerTypeSymbol }, { Type.SpecialType: SpecialType.System_Int32 }] => true,
                [var source] => source.Type.IsArrayLikeType(),
                _ => false
            };
        }

        return false;
    }

    // constructor 'System.Numerics.Vector3(System.ReadOnlySpan<float>)' without a device equivalent
    private static string DescribeUnsupportedConstructor(ITypeSymbol typeSymbol, IMethodSymbol constructor)
    {
        var parameters = new string[constructor.Parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            parameters[i] = TypeLayoutCalculator.FormatTypeName(constructor.Parameters[i].Type);
        }

        return $"constructor '{TypeLayoutCalculator.FormatTypeName(typeSymbol)}({string.Join(", ", parameters)})' " +
               "without a device equivalent";
    }

    // __gsharp_ctor_<T>(args) | <mapped>(args) | T{args} (named arguments reordered by AppendBraces)
    private static void AppendStructCreation(BaseObjectCreationExpressionSyntax node, ITypeSymbol typeSymbol,
        IMethodSymbol? constructor, ArgumentListSyntax? argumentList, ref TranspileContext context)
    {
        switch (Classify(typeSymbol, constructor, ref context, out var mappedFunction))
        {
            // Classify returns Factory, Mapped and Unsupported only for a constructor it was given
            case StructCreationKind.Factory when constructor != null:
                TranspileConstructorCall(typeSymbol, constructor, argumentList, ref context);
                break;
            case StructCreationKind.Mapped when constructor != null:
                TranspileCall(mappedFunction, constructor, argumentList, ref context);
                break;
            case StructCreationKind.Unsupported when constructor != null:
                context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node,
                    DescribeUnsupportedConstructor(typeSymbol, constructor));
                context.Visitor.AppendUnsupportedPlaceholder(node);
                break;
            default:
                AppendBraces(typeSymbol, constructor, argumentList, ref context);
                break;
        }
    }

    // ([&]() { T __gsharp_tmp<{args} | = <creation>>; __gsharp_tmp.<member> = <value>; ... return __gsharp_tmp; }())
    private static void TranspileStructCreationWithInitializer(BaseObjectCreationExpressionSyntax node,
        ITypeSymbol typeSymbol, IMethodSymbol? constructor, InitializerExpressionSyntax initializer,
        ArgumentListSyntax? argumentList, ref TranspileContext context)
    {
        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Writer.Append("[&]() ");
            using (context.Emitter.Writer.Scope())
            {
                context.Emitter.Dialect.AppendType(ref context, typeSymbol);
                context.Emitter.Writer.Append(' ').Append(GSharpInternalNames.Lowering.AggregateTemp);
                if (Classify(typeSymbol, constructor, ref context, out _) == StructCreationKind.Braces &&
                    (constructor == null || IsInParameterOrder(constructor, argumentList)))
                {
                    TranspileArgumentListAsBraces(argumentList, ref context);
                }
                else
                {
                    context.Emitter.Writer.Append(" = ");
                    AppendStructCreation(node, typeSymbol, constructor, argumentList, ref context);
                }

                context.Emitter.Writer.Append(';');
                context.Emitter.Writer.Append(' ');

                foreach (var expr in initializer.Expressions)
                {
                    if (expr is not AssignmentExpressionSyntax assignment)
                    {
                        continue;
                    }

                    context.Emitter.Writer.Append($"{GSharpInternalNames.Lowering.AggregateTemp}.");
                    ComputedPropertyGetterCall.TranspileAssignment(assignment, " = ", ref context);
                    context.Emitter.Writer.Append("; ");
                }

                context.Emitter.Writer.Append($"return {GSharpInternalNames.Lowering.AggregateTemp}; ");
            }

            context.Emitter.Writer.Append("()");
        }
    }

    internal static void TranspileConstructorCall(ITypeSymbol typeSymbol, IMethodSymbol constructor,
        ArgumentListSyntax? argumentList, ref TranspileContext context)
    {
        StructTranspiler.RegisterUsedStructIfApplicable(typeSymbol, ref context);
        TranspileCall(GlobalCppNames.GetConstructorName(constructor), constructor, argumentList, ref context);
    }

    // <function>(<the arguments matched to the parameters of constructor, defaults filled in>)
    // [WHY] C# evaluates the arguments in the written order and passes them in the parameter order (Roslyn's
    // LocalRewriter.ReorderArguments stores them in temporaries the same way), and C++ evaluates the arguments of a call
    // in no particular order: EvaluationOrder evaluates them into temporaries first when they are written in another
    // order than the parameters, or when one of them changes what another reads (#608).
    private static void TranspileCall(string function, IMethodSymbol constructor, ArgumentListSyntax? argumentList,
        ref TranspileContext context)
    {
        var arguments = argumentList?.Arguments ?? default;
        var matched = ArgumentMatchingHelper.MatchArgumentsToParameters(arguments, constructor);
        using var sequence = EvaluationOrder.BeginCall(ref context, arguments, constructor, receiver: null,
            spillAll: !IsWrittenInParameterOrder(arguments, matched));
        context.Emitter.Writer.Append(function);
        using (context.Emitter.Writer.Parentheses())
        {
            AppendMatchedArguments(constructor, matched, in sequence, ref context);
        }
    }

    // <argument or temporary of each parameter>, or the default of an omitted one
    private static void AppendMatchedArguments(IMethodSymbol constructor, ArgumentSyntax?[] matched,
        in EvaluationOrder.CallSequence sequence, ref TranspileContext context)
    {
        for (var i = 0; i < constructor.Parameters.Length; i++)
        {
            context.Emitter.Writer.AppendIf(i > 0, ", ");
            var parameter = constructor.Parameters[i];
            if (matched[i] is { } argument)
            {
                sequence.AppendArgument(argument, parameter, ref context);
            }
            else
            {
                EmitDefaultArgument(parameter, ref context);
            }
        }
    }

    // The value itself: an expression built for it (ArgumentMatchingHelper.CreateDefaultArgument) is outside the
    // compilation, so a member access such as float.NaN would bind to nothing. default is {}; an enum keeps its cast.
    private static void EmitDefaultArgument(IParameterSymbol parameter, ref TranspileContext context)
    {
        if (parameter.ExplicitDefaultValue is not { } value)
        {
            context.Emitter.Writer.Append("{}");
            return;
        }

        if (parameter.Type.TypeKind == TypeKind.Enum)
        {
            using (context.Emitter.Writer.Parentheses())
            {
                context.Emitter.Dialect.AppendType(ref context, parameter.Type);
            }
        }

        context.Emitter.Writer.Append(ConstexprLiteral.FormatCppConstant(parameter.Type, value));
    }

    // T{args}; arguments in another order than the parameters (named arguments) or omitted ones are evaluated into
    // temporaries in the written order first, so that each lands in its parameter's member:
    // ([&]() { <P0> __gsharp_seq_0 = <first written argument>; ...; return T{<temporary or default of each parameter>}; }())
    private static void AppendBraces(ITypeSymbol typeSymbol, IMethodSymbol? constructor, ArgumentListSyntax? argumentList,
        ref TranspileContext context)
    {
        if (constructor != null && !IsInParameterOrder(constructor, argumentList))
        {
            var arguments = argumentList?.Arguments ?? default;
            using var sequence = EvaluationOrder.BeginCall(ref context, arguments, constructor, receiver: null,
                spillAll: true, isCall: false);
            context.Emitter.Dialect.AppendType(ref context, typeSymbol);
            using (context.Emitter.Writer.Braces())
            {
                AppendMatchedArguments(constructor, ArgumentMatchingHelper.MatchArgumentsToParameters(arguments, constructor),
                    in sequence, ref context);
            }

            return;
        }

        context.Emitter.Dialect.AppendType(ref context, typeSymbol);
        TranspileArgumentListAsBraces(argumentList, ref context);
    }

    // Whether the arguments are written one per parameter, in the parameter order
    private static bool IsInParameterOrder(IMethodSymbol constructor, ArgumentListSyntax? argumentList)
    {
        var arguments = argumentList?.Arguments ?? default;
        if (arguments.Count != constructor.Parameters.Length)
        {
            return false;
        }

        var matched = ArgumentMatchingHelper.MatchArgumentsToParameters(arguments, constructor);
        for (var i = 0; i < matched.Length; i++)
        {
            if (matched[i] != arguments[i])
            {
                return false;
            }
        }

        return true;
    }

    // Whether each written argument goes to a later parameter than the one before it: the call can pass the arguments
    // as written. Omitted parameters in between are filled with their defaults.
    private static bool IsWrittenInParameterOrder(SeparatedSyntaxList<ArgumentSyntax> arguments,
        ArgumentSyntax?[] matched)
    {
        var previous = -1;
        foreach (var argument in arguments)
        {
            var index = IndexOfParameter(matched, argument);
            if (index < previous)
            {
                return false;
            }

            previous = index;
        }

        return true;
    }

    private static int IndexOfParameter(ArgumentSyntax?[] matched, ArgumentSyntax argument)
    {
        for (var p = 0; p < matched.Length; p++)
        {
            if (matched[p] == argument)
            {
                return p;
            }
        }

        throw new InvalidOperationException("An argument of a constructor call does not match a parameter.");
    }

    private static void TranspileArgumentListAsBraces(ArgumentListSyntax? argumentList, ref TranspileContext context)
    {
        if (argumentList is { Arguments.Count: > 0 })
        {
            using (context.Emitter.Writer.Braces())
            {
                var first = true;
                foreach (var arg in argumentList.Arguments)
                {
                    if (!first)
                    {
                        context.Emitter.Writer
                            .Append(", ");
                    }

                    context.Visitor.Visit(arg);
                    first = false;
                }
            }
        }
        else
        {
            context.Emitter.Writer.Append("{}");
        }
    }
}
