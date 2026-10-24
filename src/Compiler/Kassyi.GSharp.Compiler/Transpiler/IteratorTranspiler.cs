namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Emits C++ declarations and definitions for <c>[GpuDeviceFunction]</c> iterators
/// (<see cref="MethodEmissionKind.Iterator"/>).
/// </summary>
internal static class IteratorTranspiler
{
    /// <summary>
    /// Writes the declaration section of an iterator device function: its value-type enumerator <c>struct</c> and the
    /// forward declaration of the factory function.
    /// </summary>
    public static void WriteSignature(
        in MethodEmissionPlan plan,
        IMethodSymbol methodSymbol,
        LoweredIterator lowered,
        ref TranspileContext context)
    {
        MethodSignatureTranspiler.FailUnsupportedParameterTypes(methodSymbol, ref context);

        var structName = GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.Iterator.TypePrefix, methodSymbol);
        WriteIteratorStruct(structName, plan.Declaration.Identifier.ValueText, methodSymbol, lowered, ref context);
        WriteFactorySignature(in plan, methodSymbol, structName, ref context);
    }

    /// <summary>
    /// Writes the definition section of an iterator device function: <c>MoveNext()</c> and the factory function body.
    /// </summary>
    public static void WriteSource(
        in MethodEmissionPlan plan,
        IMethodSymbol methodSymbol,
        LoweredIterator lowered,
        ref TranspileContext context)
    {
        var structName = GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.Iterator.TypePrefix, methodSymbol);

        WriteMoveNextDefinition(structName, methodSymbol, lowered, ref context);
        context.Emitter.Writer.AppendLine();
        WriteFactoryDefinition(in plan, methodSymbol, structName, lowered, ref context);
    }

    private static void WriteIteratorStruct(
        string structName,
        string methodName,
        IMethodSymbol methodSymbol,
        LoweredIterator lowered,
        ref TranspileContext context)
    {
        var elementType = lowered.ElementType;
        StructTranspiler.RegisterUsedStructIfApplicable(elementType, ref context);

        context.Emitter.Writer.AppendLine($"struct {structName} {{");
        context.Emitter.Writer.AppendLine($"int {GSharpInternalNames.StateMachine.State} = 0;");

        context.Emitter.Dialect.AppendType(ref context, elementType);
        context.Emitter.Writer.AppendLine($" {GSharpInternalNames.Iterator.Current} = {{}};");

        foreach (var parameter in methodSymbol.Parameters)
        {
            WriteParameterField(parameter, lowered.AssignedParameters, ref context);
        }

        foreach (var field in lowered.Fields)
        {
            if (HoistedFieldTypeResolver.TryResolveIteratorField(
                    field,
                    context.Query.SemanticModel,
                    out var nestedIterator))
            {
                var nestedStructName = GlobalCppNames.GetKernelDerivedName(
                    GSharpInternalNames.Iterator.TypePrefix,
                    nestedIterator);
                context.Emitter.Writer.Append($"{nestedStructName} ");
                CppNameResolver.AppendDeclaredName(field, field.Identifier, ref context);
                context.Emitter.Writer.AppendLine(";");
                continue;
            }

            var fieldType = HoistedFieldTypeResolver.Resolve(field, context.Query.SemanticModel, methodName);
            StructTranspiler.RegisterUsedStructIfApplicable(fieldType, ref context);
            AppendFieldType(fieldType, ref context);
            context.Emitter.Writer.Append(' ');
            CppNameResolver.AppendDeclaredName(field, field.Identifier, ref context);
            context.Emitter.Writer.AppendLine(";");
        }

        var qualifier = MethodSignatureTranspiler.GetDeviceMemberQualifier(methodSymbol, context.Emitter.Dialect);
        context.Emitter.Writer.AppendLine($"{qualifier}bool MoveNext();");

        context.Emitter.Writer.Append(qualifier);
        context.Emitter.Dialect.AppendType(ref context, elementType);
        context.Emitter.Writer.AppendLine($" Current() const {{ return {GSharpInternalNames.Iterator.Current}; }}");

        WriteGetEnumerator(structName, qualifier, methodSymbol, lowered.AssignedParameters, ref context);

        context.Emitter.Writer.AppendLine("};");
    }

    private static void WriteParameterField(
        IParameterSymbol parameter,
        IReadOnlyList<string> assignedParameters,
        ref TranspileContext context)
    {
        var paramType = parameter.Type.UnwrapNullableArrayLike();
        StructTranspiler.RegisterUsedStructIfApplicable(paramType, ref context);

        var cppName = CppNameResolver.Resolve(parameter, parameter.Name, context.Emitter.Dialect);
        var isReadOnly = parameter.HasAttribute(KnownAttributes.GpuReadOnly);
        AppendFieldType(paramType, ref context, isReadOnly);
        context.Emitter.Writer.Append(' ').AppendCppName(cppName).AppendLine(";");

        if (!assignedParameters.AsStackEnumerable()
            .Any(parameter.Name, static (candidate, target) => candidate == target))
        {
            return;
        }

        AppendFieldType(paramType, ref context, isReadOnly);
        context.Emitter.Writer
            .Append($" {GSharpInternalNames.Iterator.InitialParameterPrefix}")
            .AppendCppName(cppName)
            .AppendLine(";");
    }

    private static void AppendFieldType(
        ITypeSymbol? typeSymbol,
        ref TranspileContext context,
        bool isReadOnly = false)
    {
        typeSymbol = typeSymbol.UnwrapNullableArrayLike();
        if (typeSymbol.TryGetArrayLikeElementType(out var elemType))
        {
            context.Emitter.Writer.Append("GSharpArray<");
            context.Emitter.Writer.AppendIf(isReadOnly, "const ");
            context.Emitter.Dialect.AppendType(ref context, elemType);
            context.Emitter.Writer.Append(">");
            return;
        }

        context.Emitter.Writer.AppendIf(isReadOnly, "const ");
        context.Emitter.Dialect.AppendType(ref context, typeSymbol);
    }

    private static void WriteGetEnumerator(
        string structName,
        string qualifier,
        IMethodSymbol methodSymbol,
        IReadOnlyList<string> assignedParameters,
        ref TranspileContext context)
    {
        context.Emitter.Writer.AppendLine($"{qualifier}{structName} GetEnumerator() const");
        using (context.Emitter.Writer.Scope())
        {
            var temp = GSharpInternalNames.Lowering.AggregateTemp;
            context.Emitter.Writer.AppendLine($"{structName} {temp}{{}};");
            foreach (var parameter in methodSymbol.Parameters)
            {
                var cppName = CppNameResolver.Resolve(parameter, parameter.Name, context.Emitter.Dialect);
                if (assignedParameters.AsStackEnumerable()
                    .Any(parameter.Name, static (candidate, target) => candidate == target))
                {
                    context.Emitter.Writer.Append($"{temp}.").AppendCppName(cppName)
                        .Append($" = {GSharpInternalNames.Iterator.InitialParameterPrefix}").AppendCppName(cppName)
                        .AppendLine(";");
                    context.Emitter.Writer.Append($"{temp}.{GSharpInternalNames.Iterator.InitialParameterPrefix}")
                        .AppendCppName(cppName)
                        .Append($" = {GSharpInternalNames.Iterator.InitialParameterPrefix}").AppendCppName(cppName)
                        .AppendLine(";");
                }
                else
                {
                    context.Emitter.Writer.Append($"{temp}.").AppendCppName(cppName).Append(" = ")
                        .AppendCppName(cppName).AppendLine(";");
                }
            }

            context.Emitter.Writer.AppendLine($"return {temp};");
        }
    }

    private static void WriteMoveNextDefinition(
        string structName,
        IMethodSymbol methodSymbol,
        LoweredIterator lowered,
        ref TranspileContext context)
    {
        var qualifier = MethodSignatureTranspiler.GetDeviceMemberQualifier(methodSymbol, context.Emitter.Dialect);
        context.Emitter.Writer.AppendLine($"{qualifier}bool {structName}::MoveNext()");
        using (context.Emitter.Writer.Scope())
        {
            var ctx = GSharpInternalNames.StateMachine.Context;
            var state = GSharpInternalNames.StateMachine.State;
            var current = GSharpInternalNames.Iterator.Current;
            context.Emitter.Writer.AppendLine($"auto* {ctx} = this;");
            context.Emitter.Writer.AppendLine($"#define {state} {ctx}->{state}");
            context.Emitter.Writer.AppendLine($"#define {current} {ctx}->{current}");

            context.Visitor.Visit(lowered.MoveNextBody);

            context.Emitter.Writer.AppendLine($"#undef {current}");
            context.Emitter.Writer.AppendLine($"#undef {state}");
            context.Emitter.Writer.AppendLine("return false;");
        }
    }

    private static void WriteFactorySignature(
        in MethodEmissionPlan plan,
        IMethodSymbol methodSymbol,
        string structName,
        ref TranspileContext context)
    {
        var methodName = CppNameResolver.IsEmittedDeviceFunction(methodSymbol, context.Emitter.Dialect)
            ? GlobalCppNames.GetFunctionName(methodSymbol)
            : plan.Declaration.Identifier.Text;
        var qualifier = MethodSignatureTranspiler.GetDeviceMemberQualifier(methodSymbol, context.Emitter.Dialect);

        context.Emitter.Writer.Append($"{qualifier}{structName} {methodName}");
        using (context.Emitter.Writer.Parentheses())
        {
            MethodSignatureTranspiler.WriteParameters(
                plan.Declaration.ParameterList.Parameters,
                isDevice: true,
                ref context);
        }
    }

    private static void WriteFactoryDefinition(
        in MethodEmissionPlan plan,
        IMethodSymbol methodSymbol,
        string structName,
        LoweredIterator lowered,
        ref TranspileContext context)
    {
        WriteFactorySignature(in plan, methodSymbol, structName, ref context);
        context.Emitter.Writer.AppendLine();
        using (context.Emitter.Writer.Scope())
        {
            var temp = GSharpInternalNames.Lowering.AggregateTemp;
            context.Emitter.Writer.AppendLine($"{structName} {temp}{{}};");
            foreach (var parameter in methodSymbol.Parameters)
            {
                var cppName = CppNameResolver.Resolve(parameter, parameter.Name, context.Emitter.Dialect);
                context.Emitter.Writer.Append($"{temp}.").AppendCppName(cppName).Append(" = ")
                    .AppendCppName(cppName).AppendLine(";");

                if (lowered.AssignedParameters.AsStackEnumerable()
                    .Any(parameter.Name, static (candidate, target) => candidate == target))
                {
                    context.Emitter.Writer.Append($"{temp}.{GSharpInternalNames.Iterator.InitialParameterPrefix}")
                        .AppendCppName(cppName).Append(" = ")
                        .AppendCppName(cppName).AppendLine(";");
                }
            }

            context.Emitter.Writer.AppendLine($"return {temp};");
        }
    }
}
