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
/// A member that one kernel declares or makes G# generate and that the class already has (GSH0059), which C# would
/// reject as a duplicate (<see cref="LauncherSignatures.FindConflicts"/>).
/// </summary>
/// <param name="Earlier">
/// The index of the kernel that keeps the member, <see cref="ClassMember"/> for a member that G# generates in every
/// kernel class, or -1 when there is no conflict.
/// </param>
/// <param name="Member">
/// The member as <c>Name(type, type)</c> for a method, its name otherwise; empty without a conflict.
/// </param>
internal readonly record struct LauncherConflict(int Earlier, string Member)
{
    /// <summary>
    /// <see cref="Earlier"/> of a member that G# generates in every kernel class
    /// (<see cref="LauncherSignatures.ClassMemberNames"/>).
    /// </summary>
    public const int ClassMember = -2;

    public static readonly LauncherConflict None = new(-1, string.Empty);

    public bool IsConflict => Earlier != None.Earlier;

    /// <summary>
    /// Whether the member is one that G# generates in every kernel class, which leaving the kernel out does not remove.
    /// </summary>
    public bool IsClassMember => Earlier == ClassMember;
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
    /// The parameter types of the kernel's own method as C# compares overloads: <c>ref </c> for every by-reference
    /// parameter (<c>ref</c>, <c>out</c>, <c>in</c>, <c>ref readonly</c> alike, CS0663).
    /// </summary>
    public static string FormatOwnParameterTypes(EquatableArray<KernelParameterShape> parameters)
    {
        using var writer = new SourceWriter();
        for (var i = 0; i < parameters.Length; i++)
        {
            writer.AppendIf(i > 0, ", ");
            writer.AppendIf(parameters[i].RefKind != RefKind.None, "ref ");
            writer.Append(parameters[i].TypeName);
        }

        return writer.ToString();
    }

    /// <summary>
    /// Finds, for each kernel of one class, a member that the kernel declares or makes G# generate and that the class
    /// already has (GSH0059). The table holds the methods by name and C# parameter types (the launchers, the warmup
    /// method, whose key also names every per-kernel field, and each kernel's own method), and the members that collide
    /// with anything of their name (<c>{key}_Source</c> with <paramref name="emitsSource"/>, and the members that G#
    /// generates in every kernel class, <see cref="ClassMemberNames"/>). The kernels' own methods and the class members
    /// are always there. The generated members of the kernels are taken by name, then by overload ordinal, so the
    /// result does not depend on the order in which the kernels are given, and a kernel with a conflict claims nothing.
    /// </summary>
    /// <returns>One entry per kernel, in the order given.</returns>
    public static ImmutableArray<LauncherConflict> FindConflicts(IReadOnlyList<KernelHostModel> kernels,
        bool emitsSource)
    {
        var nameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new (string Name, int Ordinal, int Index)[kernels.Count];
        var conflicts = new LauncherConflict[kernels.Count];
        var table = new MemberTable();
        foreach (var name in ClassMemberNames)
        {
            table.Add(LauncherMember.NameOnly(name), LauncherConflict.ClassMember);
        }

        for (var i = 0; i < kernels.Count; i++)
        {
            var kernel = kernels[i];
            nameCounts.TryGetValue(kernel.Name, out var count);
            nameCounts[kernel.Name] = count + 1;
            order[i] = (kernel.Name, kernel.OverloadOrdinal, i);
            // A kernel named like a member of every kernel class collides with it, whatever G# leaves out
            var own = LauncherMember.Method(kernel.Name, kernel.OwnParameterTypes);
            conflicts[i] = table.TryFind(own, i, out var found) ? found : LauncherConflict.None;
            table.Add(own, i);
        }

        Array.Sort(order, static (x, y) =>
            string.CompareOrdinal(x.Name, y.Name) is var byName and not 0 ? byName : x.Ordinal.CompareTo(y.Ordinal));

        var members = new LauncherMember[MaxMembers];
        foreach (var (name, _, index) in order)
        {
            if (conflicts[index].IsConflict)
            {
                continue;
            }

            var count = GetMembers(kernels[index], nameCounts[name] > 1, emitsSource, members);
            for (var m = 0; m < count && !conflicts[index].IsConflict; m++)
            {
                if (table.TryFind(members[m], index, out var found))
                {
                    conflicts[index] = found;
                }
            }

            for (var m = 0; m < count && !conflicts[index].IsConflict; m++)
            {
                table.Add(members[m], index);
            }
        }

        return ImmutableArray.Create(conflicts);
    }

    /// <summary>
    /// The members that G# generates in every class with kernels: the class <c>AotContexts</c> and the error site
    /// table.
    /// </summary>
    public static readonly ImmutableArray<string> ClassMemberNames =
        ImmutableArray.Create(AotHostNames.AotContextsClassName, KernelHostNames.ErrorSitesFieldName);

    private const int MaxMembers = 5;

    // Launch{Name}, the pointer launchers and WarmupFunctionPointer{key}(), each as Name(type, type); {key}_Source
    private static int GetMembers(in KernelHostModel kernel, bool isOverloaded, bool emitsSource,
        LauncherMember[] members)
    {
        var memberKey = KernelHostNames.GetMemberKey(kernel, isOverloaded);
        var count = 0;
        members[count++] = LauncherMember.Method(KernelHostNames.GetLaunchName(kernel), kernel.ManagedParameterTypes);
        if (isOverloaded)
        {
            members[count++] = LauncherMember.Method(
                KernelHostNames.GetTypedPointerLaunchName(kernel),
                kernel.TypedPointerParameterTypes
            );
        }

        members[count++] = LauncherMember.Method(
            KernelHostNames.GetPointerLaunchName(memberKey),
            kernel.PointerParameterTypes
        );
        members[count++] = LauncherMember.Method(KernelHostNames.GetWarmupMethodName(memberKey), string.Empty);
        if (emitsSource)
        {
            members[count++] = LauncherMember.NameOnly(KernelHostNames.GetSourceConstantName(memberKey));
        }

        return count;
    }

    // A member of the table: a method, which collides with a method of the same name and parameter types, or a field,
    // constant or nested type, which collides with any member of its name
    private readonly record struct LauncherMember(string Name, string? ParameterTypes)
    {
        public static LauncherMember Method(string name, string parameterTypes) => new(name, parameterTypes);

        public static LauncherMember NameOnly(string name) => new(name, null);

        public bool IsMethod => ParameterTypes is not null;

        // Name(type, type) for a method, the name alone otherwise
        public string Display => IsMethod ? $"{Name}({ParameterTypes})" : Name;
    }

    // The members of one class with their owners: a kernel index, or LauncherConflict.ClassMember
    private sealed class MemberTable
    {
        private readonly Dictionary<string, int> _methods = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _methodNames = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _nameOnly = new(StringComparer.Ordinal);

        public bool TryFind(LauncherMember member, int self, out LauncherConflict conflict)
        {
            // A method that meets a method is shown with its parameter types; anything else collides by name alone
            if (member.IsMethod && FindOwner(_methods, member.Display, self) is { } methodOwner)
            {
                conflict = new LauncherConflict(methodOwner, member.Display);
                return true;
            }

            var owner = FindOwner(_nameOnly, member.Name, self) ??
                        (member.IsMethod ? null : FindOwner(_methodNames, member.Name, self));
            conflict = owner is { } found ? new LauncherConflict(found, member.Name) : LauncherConflict.None;
            return owner is not null;
        }

        public void Add(LauncherMember member, int owner)
        {
            if (!member.IsMethod)
            {
                AddFirst(_nameOnly, member.Name, owner);
                return;
            }

            AddFirst(_methods, member.Display, owner);
            AddFirst(_methodNames, member.Name, owner);
        }

        // The first owner of a key stays: overloads of one kernel name share their method name
        private static void AddFirst(Dictionary<string, int> members, string key, int owner)
        {
            if (!members.ContainsKey(key))
            {
                members.Add(key, owner);
            }
        }

        // A kernel never collides with itself: its own method and its generated members have different names
        private static int? FindOwner(Dictionary<string, int> members, string key, int self) =>
            members.TryGetValue(key, out var owner) && owner != self ? owner : null;
    }
}
