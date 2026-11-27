namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Internal helper facilitating shared struct instantiation and brace-enclosed initialization logic.
/// </summary>
internal static class ObjectCreationHelper
{
    public static void TranspileObjectCreation(BaseObjectCreationExpressionSyntax node, ref TranspileContext context)
    {
        var typeSymbol = StructTypeSubstitution.SubstituteTypeParameters(
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

            if (SharedMemoryCreationLift.TryAppend(node, ref context))
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

        var constructor = context.Query.SemanticModel.GetSymbol(node) as IMethodSymbol ??
                          FindSubstitutedParameterlessConstructor(node, typeSymbol, ref context);
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

    // [WHY] new T() on a type parameter binds to no constructor (Roslyn's ITypeParameterObjectCreationOperation), and C#
    // calls the parameterless constructor of the type argument at run time (Activator.CreateInstance<T>), which runs the
    // field initializers and the body of a declared one. Inside a generic struct the type parameter is replaced by the
    // type argument of the construction being emitted, whose parameterless constructor is classified as if written.
    private static IMethodSymbol? FindSubstitutedParameterlessConstructor(BaseObjectCreationExpressionSyntax node,
        ITypeSymbol? typeSymbol, ref TranspileContext context)
    {
        if (typeSymbol is not INamedTypeSymbol { TypeKind: TypeKind.Struct } structType ||
            context.Query.SemanticModel.GetTypeSymbol(node) is not ITypeParameterSymbol)
        {
            return null;
        }

        return structType.InstanceConstructors
            .AsStackEnumerable()
            .FirstOrDefault(static c => c.Parameters.Length == 0);
    }

    private static StructCreationKind Classify(ITypeSymbol typeSymbol, IMethodSymbol? constructor,
        ref TranspileContext context, out string mappedFunction) =>
        StructCreationClassifier.Classify(typeSymbol, constructor, context.Query.SemanticModel.Compilation,
            context.Emitter.Dialect, out mappedFunction);

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
                    TranspileArgumentListAsBraces(constructor, argumentList, ref context);
                }
                else
                {
                    context.Emitter.Writer.Append(" = ");
                    AppendStructCreation(node, typeSymbol, constructor, argumentList, ref context);
                }

                context.Emitter.Writer.Append(';');
                context.Emitter.Writer.Append(' ');

                ObjectInitializerAssignments.Append(initializer, lineBreaks: false, ref context);

                context.Emitter.Writer.Append($"return {GSharpInternalNames.Lowering.AggregateTemp}; ");
            }

            context.Emitter.Writer.Append("()");
        }
    }

    internal static void TranspileConstructorCall(ITypeSymbol typeSymbol, IMethodSymbol constructor,
        ArgumentListSyntax? argumentList, ref TranspileContext context)
    {
        StructOrdering.RegisterUsedStructIfApplicable(typeSymbol, ref context);
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
            AppendMatchedArguments(constructor, argumentList?.Parent, matched, in sequence, ref context);
        }
    }

    // <argument or temporary of each parameter>, or for an omitted one the call site's value of a caller-info parameter
    // or the default
    private static void AppendMatchedArguments(IMethodSymbol constructor, SyntaxNode? creation,
        ArgumentSyntax?[] matched, in EvaluationOrder.CallSequence sequence, ref TranspileContext context,
        bool convertToParameter = false)
    {
        for (var i = 0; i < constructor.Parameters.Length; i++)
        {
            context.Emitter.Writer.AppendIf(i > 0, ", ");
            var parameter = constructor.Parameters[i];
            if (matched[i] is { } argument)
            {
                sequence.AppendArgument(argument, parameter, ref context, convertToParameter);
            }
            else
            {
                OmittedArgument.Plan(parameter, creation, context.Query.SemanticModel, visitsDefaultSyntax: false)
                    .Append(parameter, ref context);
            }
        }
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
                AppendMatchedArguments(constructor, argumentList?.Parent,
                    ArgumentMatchingHelper.MatchArgumentsToParameters(arguments, constructor),
                    in sequence, ref context, convertToParameter: true);
            }

            return;
        }

        if (argumentList is not { Arguments.Count: > 0 })
        {
            DefaultValue.AppendZero(typeSymbol, ref context);
            return;
        }

        context.Emitter.Dialect.AppendType(ref context, typeSymbol);
        TranspileArgumentListAsBraces(constructor, argumentList, ref context);
    }

    // Whether the arguments are written one per parameter, in the parameter order
    private static bool IsInParameterOrder(IMethodSymbol constructor, ArgumentListSyntax? argumentList)
    {
        var arguments = argumentList?.Arguments ?? default;
        var matched = ArgumentMatchingHelper.MatchArgumentsToParameters(arguments, constructor);
        var ordinals = new int[arguments.Count];
        for (var i = 0; i < ordinals.Length; i++)
        {
            ordinals[i] = Array.IndexOf(matched, arguments[i]);
        }

        return StructCreationClassifier.IsInParameterOrder(constructor.Parameters.Length, ordinals);
    }

    // Whether each written argument goes to a later parameter than the one before it: the call can pass the arguments
    // as written. Omitted parameters in between are filled with their defaults.
    private static bool IsWrittenInParameterOrder(SeparatedSyntaxList<ArgumentSyntax> arguments,
        ArgumentSyntax?[] matched)
    {
        var ordinals = new int[arguments.Count];
        for (var i = 0; i < ordinals.Length; i++)
        {
            ordinals[i] = IndexOfParameter(matched, arguments[i]);
        }

        return StructCreationClassifier.IsWrittenInParameterOrder(ordinals);
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

    // {<arguments in parameter order>}: an argument that C# converts implicitly to its parameter's primitive type is
    // written converted, since a braced list rejects a narrowing conversion (Vector2{1.5f,(float)i}, #897)
    private static void TranspileArgumentListAsBraces(IMethodSymbol? constructor, ArgumentListSyntax? argumentList,
        ref TranspileContext context)
    {
        if (argumentList is { Arguments.Count: > 0 })
        {
            using (context.Emitter.Writer.Braces())
            {
                for (var i = 0; i < argumentList.Arguments.Count; i++)
                {
                    context.Emitter.Writer.AppendIf(i > 0, ", ");
                    var argument = argumentList.Arguments[i];
                    if (constructor != null && i < constructor.Parameters.Length &&
                        constructor.Parameters[i].RefKind == RefKind.None)
                    {
                        ArithmeticConversionsEmitter.AppendArgument(argument, constructor.Parameters[i].Type, ref context);
                    }
                    else
                    {
                        context.Visitor.Visit(argument);
                    }
                }
            }
        }
        else
        {
            context.Emitter.Writer.Append("{}");
        }
    }
}

