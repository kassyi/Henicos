namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>The launchers that the generator writes for a kernel.</summary>
internal enum LauncherKind
{
    /// <summary><c>Launch{Name}</c>, with the kernel's own parameter types.</summary>
    Managed,

    /// <summary>
    /// The untyped <c>Launch{Name}Ptr</c> (<c>Launch{MemberKey}Ptr</c>, private, for an overloaded kernel): arrays as
    /// <c>GSharpArrayProxy</c>, <c>ref</c> / <c>out</c> as <c>IntPtr</c>.
    /// </summary>
    Pointer,

    /// <summary>The <c>Launch{Name}Ptr</c> of an overloaded kernel, with <c>GpuBuffer&lt;T&gt;</c> where it can.</summary>
    TypedPointer
}

/// <summary>One parameter of a generated launcher (<see cref="LauncherSignatures.GetParameter"/>).</summary>
/// <param name="Modifier"><c>ref</c>, <c>out</c> or empty.</param>
/// <param name="TypeName">The type as the launcher writes it.</param>
/// <param name="SignatureTypeName">
/// The type as C# compares the signatures of two methods, spelled as <c>GetFullyQualifiedCsharpName</c> spells the
/// kernel's own parameter types: <c>ref </c> for <c>ref</c> and <c>out</c> alike, no nullable reference annotation, and
/// <c>Nullable&lt;T&gt;</c> for a nullable value type.
/// </param>
/// <param name="DefaultValue">The default value as C#, or <see langword="null"/>.</param>
internal readonly record struct LauncherParameter(
    string Modifier,
    string TypeName,
    string SignatureTypeName,
    string? DefaultValue)
{
    // [<modifier> ]<type> <name>[ = <default>]
    public void WriteTo(SourceWriter writer, string name)
    {
        writer.AppendIf(Modifier.Length > 0, $"{Modifier} ");
        writer.Append($"{TypeName} {name}");
        writer.AppendIf(DefaultValue is not null, $" = {DefaultValue}");
    }
}

/// <summary>
/// A generated member of one kernel that another kernel of the class also generates (GSH0059), which C# would reject
/// as a duplicate (<see cref="LauncherSignatures.FindConflicts"/>).
/// </summary>
/// <param name="Earlier">The index of the kernel that keeps the member, or -1 when there is no conflict.</param>
/// <param name="Member">The member as <c>Name(type, type)</c>, or empty.</param>
internal readonly record struct LauncherConflict(int Earlier, string Member)
{
    public static readonly LauncherConflict None = new(-1, string.Empty);

    public bool IsConflict => Earlier >= 0;
}

/// <summary>
/// Decides the parameter types of the launchers of a kernel, which the launchers are written with and which the check
/// for duplicate launchers (GSH0059) compares, so that the two cannot disagree.
/// </summary>
internal static class LauncherSignatures
{
    /// <summary>The parameter that the launcher <paramref name="kind"/> takes for <paramref name="shape"/>.</summary>
    public static LauncherParameter GetParameter(in KernelParameterShape shape, LauncherKind kind)
    {
        if (kind == LauncherKind.TypedPointer && shape.BufferElementTypeName is { } element)
        {
            var buffer = $"{HostTypeNames.GpuBuffer}<{element}>";
            return new LauncherParameter(
                string.Empty,
                TakesNullBuffer(shape) ? buffer + "?" : buffer,
                buffer,
                shape.HasExplicitDefault ? "null" : null
            );
        }

        var explicitDefault = shape.HasExplicitDefault ? "default" : null;
        switch (kind == LauncherKind.Managed ? PointerArgumentKind.Value : PointerLaunchPlan.Classify(shape))
        {
            case PointerArgumentKind.Array:
                return Plain(HostTypeNames.ArrayProxy, explicitDefault);
            case PointerArgumentKind.NullableArray:
                // Compared as GetFullyQualifiedCsharpName spells a kernel's own Nullable<GSharpArrayProxy> parameter
                return new LauncherParameter(
                    string.Empty,
                    HostTypeNames.ArrayProxy + "?",
                    $"global::System.Nullable<{HostTypeNames.ArrayProxy}>",
                    explicitDefault
                );
            case PointerArgumentKind.Reference:
                return Plain(HostTypeNames.IntPtr, explicitDefault);
            case PointerArgumentKind.ShadowStruct:
                return Plain(
                    shape.AotStructPath ?? throw new InvalidOperationException(
                        $"The struct parameter '{shape.Name}' has no AotContexts path."),
                    shape.DefaultValueLiteral
                );
            default:
            {
                // A pointer launcher passes a ref / out parameter as an IntPtr (Reference), so only Launch{Name} gets here with one
                var modifier = shape.RefKind switch
                {
                    RefKind.Ref => "ref",
                    RefKind.Out => "out",
                    _ => string.Empty
                };
                var signature = modifier.Length > 0 ? "ref " + shape.TypeName : shape.TypeName;
                return new LauncherParameter(modifier, shape.DeclaredTypeName, signature, shape.DefaultValueLiteral);
            }
        }
    }

    // A parameter without a modifier whose written type is the one C# compares
    private static LauncherParameter Plain(string typeName, string? defaultValue) =>
        new(string.Empty, typeName, typeName, defaultValue);

    /// <summary>
    /// Whether the typed launcher takes a <c>GpuBuffer&lt;T&gt;?</c> for <paramref name="shape"/>: a parameter with a
    /// default value, or a nullable array (<c>T[]?</c>, <c>Memory&lt;T&gt;?</c>), which a null buffer passes as null.
    /// </summary>
    public static bool TakesNullBuffer(in KernelParameterShape shape) =>
        shape.HasExplicitDefault ||
        shape.IsOptional && PointerLaunchPlan.IsProxy(PointerLaunchPlan.Classify(shape));

    /// <summary>The parameter types of the launcher <paramref name="kind"/> as C# compares them: <c>type, ref type</c>.</summary>
    public static string FormatParameterTypes(EquatableArray<KernelParameterShape> parameters, LauncherKind kind)
    {
        using var writer = new SourceWriter();
        for (var i = 0; i < parameters.Length; i++)
        {
            writer.AppendIf(i > 0, ", ");
            writer.Append(GetParameter(parameters[i], kind).SignatureTypeName);
        }

        return writer.ToString();
    }

    /// <summary>
    /// Finds, for each kernel of one class, a generated method that an earlier kernel already has with the same
    /// parameter types (GSH0059): one of its launchers, or its warmup method, which shares the member key of every
    /// per-kernel field. The kernels are taken by name, then by overload ordinal, so the result does not depend on the
    /// order in which they are given; a kernel with a conflict claims no member.
    /// </summary>
    /// <returns>One entry per kernel, in the order given.</returns>
    public static ImmutableArray<LauncherConflict> FindConflicts(IReadOnlyList<KernelHostModel> kernels)
    {
        var nameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new (string Name, int Ordinal, int Index)[kernels.Count];
        for (var i = 0; i < kernels.Count; i++)
        {
            nameCounts.TryGetValue(kernels[i].Name, out var count);
            nameCounts[kernels[i].Name] = count + 1;
            order[i] = (kernels[i].Name, kernels[i].OverloadOrdinal, i);
        }

        Array.Sort(order, static (x, y) =>
            string.CompareOrdinal(x.Name, y.Name) is var byName and not 0 ? byName : x.Ordinal.CompareTo(y.Ordinal));

        var owners = new Dictionary<string, int>(StringComparer.Ordinal);
        var conflicts = new LauncherConflict[kernels.Count];
        var members = new string[MaxMembers];
        foreach (var (name, _, index) in order)
        {
            var count = GetMembers(kernels[index], nameCounts[name] > 1, members);
            conflicts[index] = LauncherConflict.None;
            for (var m = 0; m < count; m++)
            {
                if (owners.TryGetValue(members[m], out var earlier))
                {
                    conflicts[index] = new LauncherConflict(earlier, members[m]);
                    break;
                }
            }

            if (conflicts[index].IsConflict)
            {
                continue;
            }

            for (var m = 0; m < count; m++)
            {
                owners.Add(members[m], index);
            }
        }

        return ImmutableArray.Create(conflicts);
    }

    private const int MaxMembers = 4;

    // Launch{Name}, the pointer launchers and WarmupFunctionPointer{key}(), each as Name(type, type)
    private static int GetMembers(in KernelHostModel kernel, bool isOverloaded, string[] members)
    {
        var memberKey = KernelHostNames.GetMemberKey(kernel, isOverloaded);
        var count = 0;
        members[count++] = $"{KernelHostNames.GetLaunchName(kernel)}({kernel.ManagedParameterTypes})";
        if (isOverloaded)
        {
            var typedName = KernelHostNames.GetTypedPointerLaunchName(kernel);
            members[count++] = $"{typedName}({kernel.TypedPointerParameterTypes})";
        }

        members[count++] = $"{KernelHostNames.GetPointerLaunchName(memberKey)}({kernel.PointerParameterTypes})";
        members[count++] = $"{KernelHostNames.GetWarmupMethodName(memberKey)}()";
        return count;
    }
}
