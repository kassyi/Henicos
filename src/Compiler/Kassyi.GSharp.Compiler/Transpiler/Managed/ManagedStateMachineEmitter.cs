namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Emits C# context classes and state machine methods for managed host execution.
/// </summary>
internal static class ManagedStateMachineEmitter
{
#pragma warning disable RS0030 // Final emission of a node; the text is not parsed, compared, or edited afterwards
    // Generated code is left-aligned (transpiler-emission §3), so the normalized source has no indentation
    internal static string ToSource(SyntaxNode node) => node.NormalizeWhitespace(indentation: "").ToFullString();
#pragma warning restore RS0030

    /// <summary>Writes the field type: a written type as written, <c>var</c> as the resolved type.</summary>
    public static void AppendFieldType(SourceWriter writer, ManagedContextField field)
    {
        if (field.DeclaredType.IsVar)
        {
            writer.AppendCsharpTypeName(field.ResolvedType);
        }
        else
        {
            // Without trivia: a comment above the original declaration would otherwise land inside the emitted one
            writer.Append(ToSource(field.DeclaredType.WithoutTrivia()));
        }
    }

    /// <param name="writer">The writer.</param>
    /// <param name="contextName">The class.</param>
    /// <param name="regularFields">The hoisted locals.</param>
    /// <param name="copiedParameters">The parameters that the class keeps.</param>
    /// <param name="callSites">The calls that may suspend, whose frames the class holds.</param>
    /// <param name="semanticModel">The model of the calls.</param>
    /// <param name="accessibility">The class's accessibility.</param>
    /// <param name="memberAccessibility">The accessibility of the fields other than the call frames.</param>
    /// <remarks>
    /// A call frame field is internal: the frame of a device function that is not public is an internal class.
    /// </remarks>
    public static void EmitContextClass(
        SourceWriter writer,
        string contextName,
        ImmutableArray<ManagedContextField> regularFields,
        ImmutableArray<IParameterSymbol> copiedParameters,
        IReadOnlyList<InvocationExpressionSyntax> callSites,
        SemanticModel semanticModel,
        string accessibility = "public",
        string memberAccessibility = "public")
    {
        writer.AppendLine($"{accessibility} class {contextName} {{");
        writer.AppendLine($"{memberAccessibility} int {GSharpInternalNames.StateMachine.State};");
        writer.AppendLine($"{memberAccessibility} uint {GSharpInternalNames.StateMachine.WarpExchange};");
        writer.AppendLine($"{memberAccessibility} bool {GSharpInternalNames.StateMachine.YieldedForGridSync};");

        foreach (var parameter in copiedParameters)
        {
            writer.Append($"{memberAccessibility} ");
            writer.AppendCsharpTypeName(parameter.Type);
            writer.AppendLine($" {parameter.Name.ToCSharpIdentifier()};");
        }

        foreach (var field in regularFields)
        {
            writer.Append($"{memberAccessibility} ");
            AppendFieldType(writer, field);
            writer.AppendLine($" {field.Name};");
        }

        // Each call that may suspend keeps the callee's frame, which is declared in the callee's class
        for (var i = 0; i < callSites.Count; i++)
        {
            var callee = semanticModel.GetSymbol(callSites[i]) as IMethodSymbol
                         ?? throw new InvalidOperationException(
                             "A call that may suspend does not resolve to a device function.");
            writer.Append("internal ");
            AppendDeviceFunctionMember(writer, callee, GSharpInternalNames.SuspendingCall.FrameTypePrefix);
            writer.AppendLine($" {GSharpInternalNames.SuspendingCall.CallFramePrefix}{i} = new();");
        }

        writer.AppendLine("}");
        writer.AppendLine();
    }

    /// <summary>Writes the qualified name of a member that G# emits next to <paramref name="function"/>.</summary>
    public static void AppendDeviceFunctionMember(SourceWriter writer, IMethodSymbol function, string prefix)
    {
        writer.AppendFullyQualifiedCsharpName(function.ContainingType);
        writer.Append($".{GlobalCppNames.GetKernelDerivedName(prefix, function)}");
    }

    public static string GetDeviceFunctionMember(IMethodSymbol function, string prefix)
    {
        using var writer = new SourceWriter();
        AppendDeviceFunctionMember(writer, function, prefix);
        return writer.ToString();
    }

    // Out parameters become ref: a suspended call returns before it assigns them, which C# rejects for out parameters
    public static void EmitDeviceStateMachineMethod(
        SourceWriter writer,
        string frameName,
        string stateMachineName,
        IMethodSymbol methodSymbol,
        ImmutableArray<IParameterSymbol> copiedParameters,
        ImmutableArray<ManagedContextField> localInits,
        string bodySource,
        string accessibility,
        bool isUnsafe)
    {
        writer.Append($"{accessibility} static ");
        writer.AppendIf(isUnsafe, "unsafe ");
        writer.Append($"bool {stateMachineName}(");
        foreach (var p in methodSymbol.Parameters)
        {
            writer.Append(p.RefKind switch
            {
                RefKind.Ref or RefKind.Out => "ref ",
                RefKind.In => "in ",
                RefKind.RefReadOnlyParameter => "ref readonly ",
                _ => ""
            });
            writer.AppendCsharpTypeName(p.Type);
            writer.Append(IsCopied(copiedParameters, p)
                ? $" {GSharpInternalNames.SuspendingCall.CopiedParameterPrefix}{p.Name}, "
                : $" {p.Name.ToCSharpIdentifier()}, ");
        }

        writer.Append($"{frameName} {GSharpInternalNames.StateMachine.Context}");
        if (!methodSymbol.ReturnsVoid)
        {
            writer.Append(", ref ");
            writer.AppendCsharpTypeName(methodSymbol.ReturnType);
            writer.Append($" {GSharpInternalNames.SuspendingCall.Result}");
        }

        writer.AppendLine(") {");
        if (localInits.Length > 0)
        {
            foreach (var localInit in localInits)
            {
                AppendFieldType(writer, localInit);
                writer.Append($" {localInit.Name}");
                if (localInit.Declarator.Initializer is { } initializer)
                {
                    writer.Append($" {ToSource(initializer.WithoutTrivia())}");
                }

                writer.AppendLine(";");
            }
        }

        if (copiedParameters.Length > 0)
        {
            writer.AppendLine(
                $"if ({GSharpInternalNames.StateMachine.Context}.{GSharpInternalNames.StateMachine.State} == 0) {{");
            foreach (var parameter in copiedParameters)
            {
                writer.AppendLine(
                    $"{GSharpInternalNames.StateMachine.Context}.{parameter.Name.ToCSharpIdentifier()} = {GSharpInternalNames.SuspendingCall.CopiedParameterPrefix}{parameter.Name};");
            }

            writer.AppendLine("}");
        }

        writer.AppendLine(bodySource);
        writer.AppendLine(
            "throw new global::System.InvalidOperationException(\"The device function state machine reached an unknown state.\");");
        writer.AppendLine("}");
        writer.AppendLine();
    }

    private static bool IsCopied(ImmutableArray<IParameterSymbol> copiedParameters, IParameterSymbol parameter) =>
        copiedParameters
            .AsStackEnumerable()
            .Any(parameter, static (copied, target) => copied.IsEqualTo(target));

    public static void EmitStateMachineMethod(
        SourceWriter writer,
        string contextName,
        string stateMachineName,
        IMethodSymbol methodSymbol,
        bool hasAutoResult,
        ImmutableArray<ManagedContextField> localInits,
        string bodySource)
    {
        EmitStateMachineSignature(writer, contextName, stateMachineName, methodSymbol, hasAutoResult);
        writer.AppendLine(") {");

        foreach (var localInit in localInits)
        {
            AppendFieldType(writer, localInit);
            writer.Append($" {localInit.Name}");
            if (localInit.Declarator.Initializer is { } initializer)
            {
                writer.Append($" {ToSource(initializer.WithoutTrivia())}");
            }

            writer.AppendLine(";");
        }

        writer.AppendLine(bodySource);
        writer.AppendLine("}");
    }

    // Out parameters become ref: a suspended call returns before it assigns them, and resumed calls must keep previous writes
    private static void EmitStateMachineSignature(
        SourceWriter writer,
        string contextName,
        string stateMachineName,
        IMethodSymbol methodSymbol,
        bool hasAutoResult)
    {
        writer.Append($"public static unsafe void {stateMachineName}(");
        foreach (var p in methodSymbol.Parameters)
        {
            writer.Append(p.RefKind switch
            {
                RefKind.Ref or RefKind.Out => "ref ",
                RefKind.In => "in ",
                RefKind.RefReadOnlyParameter => "ref readonly ",
                _ => ""
            });
            writer.AppendCsharpTypeName(p.Type);
            writer.Append($" {p.Name.ToCSharpIdentifier()}, ");
        }

        writer.Append($"{contextName} {GSharpInternalNames.StateMachine.Context}");

        if (hasAutoResult)
        {
            writer.Append(", ref ");
            writer.AppendCsharpTypeName(methodSymbol.ReturnType);
            writer.Append(" " + GSharpInternalNames.ReturnValue.Buffer);
        }
    }
}
