namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>How a module (kernel) launcher passes a parameter to the kernel.</summary>
internal enum ModuleKernelParameterKind
{
    /// <summary>An unmanaged value, passed by the address of a local copy.</summary>
    Value,

    /// <summary>A pointer or function pointer, passed by the address of a local copy.</summary>
    Pointer,

    /// <summary>
    /// A one-dimensional array of unmanaged elements, staged through a device buffer and passed as its device pointer;
    /// null is passed as a null pointer when the array is declared nullable, and rejected otherwise.
    /// </summary>
    Array,

    /// <summary>
    /// A multidimensional array of unmanaged elements, staged through one device buffer in row-major order (the order
    /// in which C# stores the elements) and passed as its device pointer; null is handled as for
    /// <see cref="Array"/>.
    /// </summary>
    MultidimensionalArray,

    /// <summary>
    /// A <c>ref</c>, <c>out</c> or <c>in</c> unmanaged value, staged through a one-element device buffer and passed as
    /// its device pointer.
    /// </summary>
    ByReference,
}

/// <summary>The launch setting that a parameter of a module launcher supplies instead of a kernel argument.</summary>
internal enum ModuleLaunchSetting
{
    None,
    GridDimX,
    GridDimY,
    GridDimZ,
    BlockDimX,
    BlockDimY,
    BlockDimZ,
    SharedMemBytes,
    Stream,
}

/// <summary>A parameter type of a YAML module binding, read once for validation and for the launcher.</summary>
/// <param name="Kind">How the launcher passes the parameter; meaningful when <paramref name="Reason"/> is null.</param>
/// <param name="LaunchSetting">
/// The launch setting the parameter supplies, or <see cref="ModuleLaunchSetting.None"/>.
/// </param>
/// <param name="TypeName">
/// The type without its modifiers and without whitespace inside it (<c>float[ ]</c> becomes <c>float[]</c>).
/// </param>
/// <param name="ElementTypeName">The element type of an array.</param>
/// <param name="Reason">Why the launcher cannot pass the parameter, or <see langword="null"/> when it can.</param>
/// <param name="RefKind">
/// <see cref="RefKind.Ref"/>, <see cref="RefKind.Out"/> or <see cref="RefKind.In"/> (also for <c>ref readonly</c>)
/// for a parameter passed by reference, otherwise <see cref="RefKind.None"/>.
/// </param>
/// <param name="AllowsNull">Whether an array is written nullable (<c>float[]?</c>, <c>float[,]?</c>).</param>
internal readonly record struct ModuleParameterType(
    ModuleKernelParameterKind Kind,
    ModuleLaunchSetting LaunchSetting,
    string TypeName,
    string? ElementTypeName,
    string? Reason,
    RefKind RefKind = RefKind.None,
    bool AllowsNull = false);

/// <summary>
/// Decides which parameters a module (kernel) launcher can pass, for <c>[CudaImport]</c> methods and YAML
/// <c>moduleName</c> bindings alike.
/// </summary>
/// <remarks>
/// A module launcher passes a kernel argument by the address of a local copy, and stages an array of unmanaged elements
/// and a <c>ref</c>, <c>out</c> or <c>in</c> unmanaged value through <c>GpuBuffer&lt;T&gt;</c>, copying it back after
/// the launch. A reference would reach the kernel as a host address, so the types outside that set are rejected. A YAML
/// type is read with the C# parser; a class named by the user cannot be told from a struct without the compilation, so
/// the launcher takes the address of such a value through a method constrained to unmanaged types, and a class fails
/// to compile there.
/// </remarks>
internal static class ModuleKernelParameterTypes
{
    public const string ManagedTypeReason =
        "a reference, or a value that contains one, would reach the kernel as a host address. " +
        "A kernel takes unmanaged values (also by 'ref', 'out' or 'in'), pointers and arrays of unmanaged elements.";

    public const string ManagedElementReason =
        "the elements are references or contain them, which would reach the kernel as host addresses. " +
        "A kernel takes arrays of unmanaged elements.";

    public const string ByReferenceReason =
        "'ref', 'out' and 'in' pass an unmanaged value through a one-element device buffer; a pointer or an array " +
        "cannot be passed that way. Pass it by value.";

    public const string LaunchSettingByReferenceReason =
        "a parameter with this name sets the launch configuration and must be passed by value.";

    public const string ModifiersReason =
        "the modifiers are not 'ref', 'out', 'in', 'ref readonly' (each optionally after 'scoped') or 'params'.";

    public const string NullableValueReason =
        "Nullable<T> has no counterpart in a kernel. Pass the value and a separate flag instead.";

    public const string JaggedArrayReason =
        "jagged arrays are not supported. Flatten the data into a one-dimensional array.";

    public const string PointerElementReason =
        "arrays of pointers are not supported; a device buffer holds unmanaged values.";

    public const string ParamsReason = "'params' needs a one-dimensional array type.";

    public const string AutoLayoutReason =
        "it has LayoutKind.Auto (a ValueTuple, DateTime, or a struct with [StructLayout(LayoutKind.Auto)] or a field " +
        "of one), so the runtime may reorder its fields and the kernel would read them at other offsets.";

    public const string RefStructReason =
        "a ref struct (Span<T>, ReadOnlySpan<T>, ...) cannot leave the stack of the launcher, so no kernel can " +
        "receive it.";

    public const string StreamTypeReason =
        "a parameter named 'stream' sets the stream of the launch and must be an IntPtr (nint).";

    public const string DimensionTypeReason =
        "a parameter with this name sets the launch configuration and must convert implicitly to uint " +
        "(uint, ushort, byte or char).";

    public const string NotATypeReason = "it is not a C# type.";

    private const string GlobalPrefix = "global::";
    private const string SystemPrefix = "System.";

    /// <summary>Gets the launch setting a parameter named <paramref name="name"/> supplies.</summary>
    public static ModuleLaunchSetting GetLaunchSetting(string name) => name switch
    {
        "gridDimX" => ModuleLaunchSetting.GridDimX,
        "gridDimY" => ModuleLaunchSetting.GridDimY,
        "gridDimZ" => ModuleLaunchSetting.GridDimZ,
        "blockDimX" => ModuleLaunchSetting.BlockDimX,
        "blockDimY" => ModuleLaunchSetting.BlockDimY,
        "blockDimZ" => ModuleLaunchSetting.BlockDimZ,
        "sharedMemBytes" => ModuleLaunchSetting.SharedMemBytes,
        "stream" => ModuleLaunchSetting.Stream,
        _ => ModuleLaunchSetting.None,
    };

    /// <summary>Reads the type of a YAML module binding parameter.</summary>
    /// <param name="name">The parameter name, which selects a launch setting.</param>
    /// <param name="type">The C# type as written in the YAML definition, with its modifiers.</param>
    public static ModuleParameterType Parse(string name, string type)
    {
        var text = type.AsSpan().Trim().ToString();
        var launchSetting = GetLaunchSetting(name);
        var typeStart = 0;
        if (!TryReadModifiers(text, ref typeStart, out var refKind, out var isScoped, out var isParams))
        {
            return Unsupported(text, launchSetting, ModifiersReason);
        }

        var typeText = text[typeStart..];
        var syntax = SyntaxFactory.ParseTypeName(typeText);
        if (syntax.ContainsDiagnostics || syntax.FullSpan.Length != typeText.Length)
        {
            return Unsupported(text, launchSetting, NotATypeReason);
        }

        var typeName = GetText(syntax);
        if (refKind != RefKind.None)
        {
            return ClassifyByReference(syntax, typeName, launchSetting, refKind);
        }

        if (isScoped)
        {
            // [WHY] 'scoped' without 'ref', 'out' or 'in' applies only to a ref struct
            return Unsupported(typeName, launchSetting, RefStructReason);
        }

        var result = Classify(syntax, typeName);
        if (isParams && result is not { Reason: null, Kind: ModuleKernelParameterKind.Array })
        {
            return result with { Reason = result.Reason ?? ParamsReason };
        }

        return launchSetting == ModuleLaunchSetting.None || result.Reason is not null
            ? result
            : result with { LaunchSetting = launchSetting, Reason = GetLaunchSettingReason(launchSetting, typeName) };
    }

    // The type without whitespace inside it, as the launcher writes it back (float [ ] becomes float[])
    private static string GetText(TypeSyntax syntax) =>
#pragma warning disable RS0030 // A YAML binding names its types only as text, which the launcher writes back as C#
        syntax.NormalizeWhitespace(indentation: string.Empty, elasticTrivia: false).ToString();
#pragma warning restore RS0030

    // Why a parameter named for <launchSetting> cannot set it, or null when its type is the one the launch takes
    private static string? GetLaunchSettingReason(ModuleLaunchSetting launchSetting, string typeName) =>
        (launchSetting, GetSystemTypeName(typeName)) switch
        {
            (ModuleLaunchSetting.Stream, "nint" or "IntPtr") => null,
            (ModuleLaunchSetting.Stream, _) => StreamTypeReason,
            (_, "uint" or "ushort" or "byte" or "char" or "UInt32" or "UInt16" or "Byte" or "Char") => null,
            _ => DimensionTypeReason,
        };

    private static ModuleParameterType Unsupported(string typeName, ModuleLaunchSetting launchSetting, string reason) =>
        new(ModuleKernelParameterKind.Value, launchSetting, typeName, null, reason);

    // Reads the modifiers in front of the type in an order C# accepts: 'params', or an optional 'scoped' followed by
    // 'ref', 'ref readonly', 'out' or 'in'. False when the words are modifiers in another order.
    private static bool TryReadModifiers(string text, ref int position, out RefKind refKind, out bool isScoped,
        out bool isParams)
    {
        const string ScopedModifier = "scoped";
        var modifiers = string.Empty;
        while (TryReadModifier(text, ref position, out var word))
        {
            modifiers = modifiers.Length == 0 ? word : $"{modifiers} {word}";
        }

        isScoped = modifiers == ScopedModifier || modifiers.StartsWith(ScopedModifier + " ", StringComparison.Ordinal);
        var rest = isScoped ? modifiers[ScopedModifier.Length..].TrimStart() : modifiers;
        isParams = !isScoped && rest == "params";
        refKind = rest switch
        {
            "ref" => RefKind.Ref,
            "out" => RefKind.Out,
            "in" or "ref readonly" => RefKind.In,
            _ => RefKind.None,
        };
        return rest.Length == 0 || isParams || refKind != RefKind.None;
    }

    // A 'ref', 'out' or 'in' parameter: an unmanaged value staged through a one-element device buffer
    private static ModuleParameterType ClassifyByReference(TypeSyntax syntax, string typeName,
        ModuleLaunchSetting launchSetting, RefKind refKind)
    {
        if (launchSetting != ModuleLaunchSetting.None)
        {
            return Unsupported(typeName, launchSetting, LaunchSettingByReferenceReason);
        }

        var reason = syntax is PointerTypeSyntax or FunctionPointerTypeSyntax or ArrayTypeSyntax
            or NullableTypeSyntax { ElementType: ArrayTypeSyntax }
            ? ByReferenceReason
            : GetValueProblem(syntax);
        return new ModuleParameterType(ModuleKernelParameterKind.ByReference, ModuleLaunchSetting.None, typeName, null,
            reason, refKind);
    }

    // Reads one modifier word at <position> and moves past it and the whitespace after it
    private static bool TryReadModifier(string text, ref int position, out string modifier)
    {
        var end = position;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
        {
            end++;
        }

        modifier = text[position..end];
        if (end == text.Length || modifier is not ("ref" or "out" or "in" or "scoped" or "params" or "readonly"))
        {
            return false;
        }

        while (end < text.Length && char.IsWhiteSpace(text[end]))
        {
            end++;
        }

        position = end;
        return true;
    }

    private static ModuleParameterType Classify(TypeSyntax syntax, string typeName)
    {
        switch (syntax)
        {
            case PointerTypeSyntax or FunctionPointerTypeSyntax:
                return new ModuleParameterType(ModuleKernelParameterKind.Pointer, ModuleLaunchSetting.None, typeName,
                    null, null);
            case NullableTypeSyntax { ElementType: ArrayTypeSyntax array }:
                return ClassifyArray(array, typeName) with { AllowsNull = true };
            case ArrayTypeSyntax array:
                return ClassifyArray(array, typeName);
            default:
                return GetValueProblem(syntax) is { } reason
                    ? Unsupported(typeName, ModuleLaunchSetting.None, reason)
                    : new ModuleParameterType(ModuleKernelParameterKind.Value, ModuleLaunchSetting.None, typeName,
                        null, null);
        }
    }

    private static ModuleParameterType ClassifyArray(ArrayTypeSyntax array, string typeName)
    {
        var element = array.ElementType;
        if (array.RankSpecifiers.Count > 1 || element is NullableTypeSyntax { ElementType: ArrayTypeSyntax })
        {
            return Unsupported(typeName, ModuleLaunchSetting.None, JaggedArrayReason);
        }

        var kind = array.RankSpecifiers[0].Rank > 1
            ? ModuleKernelParameterKind.MultidimensionalArray
            : ModuleKernelParameterKind.Array;
        var reason = element is PointerTypeSyntax or FunctionPointerTypeSyntax
            ? PointerElementReason
            : GetValueProblem(element);
        return new ModuleParameterType(kind, ModuleLaunchSetting.None, typeName, GetText(element),
            reason == ManagedTypeReason ? ManagedElementReason : reason);
    }

    // Why a value of <syntax> (not a pointer or an array) cannot be passed, from what its name alone tells: the
    // reference types, Nullable<T>, the auto-layout and ref struct types of the System namespace, and tuple syntax.
    // Any other named type is passed; a class among them fails to compile in the launcher (see remarks).
    private static string? GetValueProblem(TypeSyntax syntax)
    {
        switch (syntax)
        {
            case PredefinedTypeSyntax predefined:
                return predefined.Keyword.Kind() switch
                {
                    SyntaxKind.VoidKeyword => NotATypeReason,
                    SyntaxKind.StringKeyword or SyntaxKind.ObjectKeyword => ManagedTypeReason,
                    _ => null,
                };
            case NullableTypeSyntax nullable:
                return GetValueProblem(nullable.ElementType) == ManagedTypeReason
                    ? ManagedTypeReason
                    : NullableValueReason;
            case TupleTypeSyntax:
                return AutoLayoutReason;
            case NameSyntax name:
                return GetSystemSimpleName(name, out var arity) switch
                {
                    "dynamic" or "String" or "Object" or "Array" when arity == 0 => ManagedTypeReason,
                    "Nullable" when arity == 1 => NullableValueReason,
                    "ValueTuple" or "DateTime" or "DateTimeOffset" => AutoLayoutReason,
                    "Span" or "ReadOnlySpan" when arity == 1 => RefStructReason,
                    _ => null,
                };
            default:
                return NotATypeReason;
        }
    }

    // The name of a type of the System namespace as the launcher file reads it (it starts with 'using System;'):
    // "Nullable" for Nullable<int>, System.Nullable<int> and global::System.Nullable<int>; null for another namespace
    private static string? GetSystemSimpleName(NameSyntax name, out int arity)
    {
        var simple = name switch
        {
            SimpleNameSyntax simpleName => simpleName,
            QualifiedNameSyntax qualified when IsSystemNamespace(qualified.Left) => qualified.Right,
            _ => null,
        };
        arity = simple is GenericNameSyntax generic ? generic.TypeArgumentList.Arguments.Count : 0;
        return simple?.Identifier.ValueText;
    }

    private static bool IsSystemNamespace(NameSyntax name) => name switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "System",
        AliasQualifiedNameSyntax aliased => aliased.Alias.Identifier.ValueText == "global" &&
                                            aliased.Name.Identifier.ValueText == "System",
        _ => false,
    };

    // The name without 'global::' and 'System.' (System.UInt32 and UInt32 name the same type in the launcher file)
    private static string GetSystemTypeName(string typeName)
    {
        var name = typeName.StartsWith(GlobalPrefix, StringComparison.Ordinal)
            ? typeName[GlobalPrefix.Length..]
            : typeName;
        return name.StartsWith(SystemPrefix, StringComparison.Ordinal) ? name[SystemPrefix.Length..] : name;
    }
}
