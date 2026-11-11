namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>How a module (kernel) launcher passes a parameter to the kernel.</summary>
internal enum ModuleKernelParameterKind
{
    /// <summary>An unmanaged value, passed by the address of a local copy.</summary>
    Value,

    /// <summary>A pointer or function pointer, passed by the address of a local copy.</summary>
    Pointer,

    /// <summary>
    /// A one-dimensional array of unmanaged elements, staged through a device buffer and passed as its device pointer.
    /// </summary>
    Array,
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
/// <param name="TypeName">The type without whitespace inside it (<c>float[ ]</c> becomes <c>float[]</c>).</param>
/// <param name="ElementTypeName">The element type of an <see cref="ModuleKernelParameterKind.Array"/>.</param>
/// <param name="Reason">Why the launcher cannot pass the parameter, or <see langword="null"/> when it can.</param>
internal readonly record struct ModuleParameterType(
    ModuleKernelParameterKind Kind,
    ModuleLaunchSetting LaunchSetting,
    string TypeName,
    string? ElementTypeName,
    string? Reason);

/// <summary>
/// Decides which parameters a module (kernel) launcher can pass, for <c>[CudaImport]</c> methods and YAML
/// <c>moduleName</c> bindings alike.
/// </summary>
/// <remarks>
/// A module launcher passes a kernel argument by the address of a local copy, and stages a one-dimensional array of
/// unmanaged elements through <c>GpuBuffer&lt;T&gt;</c>. A reference would reach the kernel as a host address, and a
/// <c>ref</c> or <c>out</c> argument would never be written back, so the types outside that set are rejected. A YAML
/// type is read with the C# parser; a class named by the user cannot be told from a struct without the compilation, so
/// the launcher takes the address of such a value through a method constrained to unmanaged types, and a class fails
/// to compile there.
/// </remarks>
internal static class ModuleKernelParameterTypes
{
    public const string ManagedTypeReason =
        "a reference, or a value that contains one, would reach the kernel as a host address. " +
        "A kernel takes unmanaged values, pointers and one-dimensional arrays of unmanaged elements.";

    public const string ManagedElementReason =
        "the elements are references or contain them, which would reach the kernel as host addresses. " +
        "A kernel takes one-dimensional arrays of unmanaged elements.";

    public const string ByReferenceReason =
        "'ref', 'out', 'in' and 'scoped' parameters are not supported yet; the launcher would pass a copy and never " +
        "write the kernel's value back. Pass a one-element array instead.";

    public const string NullableValueReason =
        "Nullable<T> has no counterpart in a kernel. Pass the value and a separate flag instead.";

    public const string NullableArrayReason =
        "nullable arrays are not supported yet. Use a non-nullable array.";

    public const string MultidimensionalArrayReason =
        "multidimensional arrays are not supported yet. Flatten the data into a one-dimensional array.";

    public const string JaggedArrayReason =
        "jagged arrays are not supported. Flatten the data into a one-dimensional array.";

    public const string PointerElementReason =
        "arrays of pointers are not supported; a device buffer holds unmanaged values.";

    public const string ParamsReason = "'params' needs a one-dimensional array type.";

    public const string ReservedNameReason = "names starting with '__gsharp_' are reserved for the generated launcher.";

    public const string StreamTypeReason =
        "a parameter named 'stream' sets the stream of the launch and must be an IntPtr (nint).";

    public const string DimensionTypeReason =
        "a parameter with this name sets the launch configuration and must convert implicitly to uint " +
        "(uint, ushort, byte or char).";

    public const string NotATypeReason = "it is not a C# type.";

    private const string GlobalPrefix = "global::";
    private const string SystemPrefix = "System.";
    private const string ReservedPrefix = "__gsharp_";

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

    /// <summary>Gets whether <paramref name="name"/> could collide with a local of the generated launcher.</summary>
    public static bool IsReservedName(string name) => name.StartsWith(ReservedPrefix, StringComparison.Ordinal);

    /// <summary>Reads the type of a YAML module binding parameter.</summary>
    /// <param name="name">The parameter name, which selects a launch setting.</param>
    /// <param name="type">The C# type as written in the YAML definition, with its modifiers.</param>
    public static ModuleParameterType Parse(string name, string type)
    {
        var text = type.Trim();
        var launchSetting = GetLaunchSetting(name);
        if (IsReservedName(name))
        {
            return Unsupported(text, launchSetting, ReservedNameReason);
        }

        var isParams = false;
        var isByReference = false;
        var typeStart = 0;
        while (TryReadModifier(text, ref typeStart, out var modifier))
        {
            isParams |= modifier == "params";
            isByReference |= modifier != "params";
        }

        var typeText = text[typeStart..];
        var syntax = SyntaxFactory.ParseTypeName(typeText);
        if (syntax.ContainsDiagnostics || syntax.FullSpan.Length != typeText.Length)
        {
            return Unsupported(text, launchSetting, NotATypeReason);
        }

        var typeName = syntax.NormalizeWhitespace(indentation: string.Empty, elasticTrivia: false).ToString();
        if (isByReference)
        {
            return Unsupported(typeName, launchSetting, ByReferenceReason);
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

    // Why a parameter named for <launchSetting> cannot set it, or null when its type is the one the launch takes
    private static string? GetLaunchSettingReason(ModuleLaunchSetting launchSetting, string typeName) =>
        launchSetting == ModuleLaunchSetting.Stream
            ? IsStreamTypeName(typeName) ? null : StreamTypeReason
            : IsDimensionTypeName(typeName) ? null : DimensionTypeReason;

    private static ModuleParameterType Unsupported(string typeName, ModuleLaunchSetting launchSetting, string reason) =>
        new(ModuleKernelParameterKind.Value, launchSetting, typeName, null, reason);

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
            case NullableTypeSyntax { ElementType: ArrayTypeSyntax }:
                return Unsupported(typeName, ModuleLaunchSetting.None, NullableArrayReason);
            case NullableTypeSyntax nullable:
                return Unsupported(typeName, ModuleLaunchSetting.None,
                    IsReferenceTypeName(nullable.ElementType) ? ManagedTypeReason : NullableValueReason);
            case ArrayTypeSyntax array:
                return ClassifyArray(array, typeName);
            case PredefinedTypeSyntax when IsVoid(syntax):
                return Unsupported(typeName, ModuleLaunchSetting.None, NotATypeReason);
            case PredefinedTypeSyntax or NameSyntax or TupleTypeSyntax when IsReferenceTypeName(syntax):
                return Unsupported(typeName, ModuleLaunchSetting.None, ManagedTypeReason);
            case PredefinedTypeSyntax or NameSyntax or TupleTypeSyntax:
                return new ModuleParameterType(ModuleKernelParameterKind.Value, ModuleLaunchSetting.None, typeName,
                    null, null);
            default:
                return Unsupported(typeName, ModuleLaunchSetting.None, NotATypeReason);
        }
    }

    private static ModuleParameterType ClassifyArray(ArrayTypeSyntax array, string typeName)
    {
        if (array.RankSpecifiers.Count > 1)
        {
            return Unsupported(typeName, ModuleLaunchSetting.None, JaggedArrayReason);
        }

        if (array.RankSpecifiers[0].Rank > 1)
        {
            return Unsupported(typeName, ModuleLaunchSetting.None, MultidimensionalArrayReason);
        }

        var element = array.ElementType;
        var reason = element switch
        {
            PointerTypeSyntax or FunctionPointerTypeSyntax => PointerElementReason,
            NullableTypeSyntax nullable when IsReferenceTypeName(nullable.ElementType) => ManagedElementReason,
            NullableTypeSyntax => NullableValueReason,
            PredefinedTypeSyntax or NameSyntax or TupleTypeSyntax when IsReferenceTypeName(element) =>
                ManagedElementReason,
            PredefinedTypeSyntax or NameSyntax or TupleTypeSyntax => null,
            _ => NotATypeReason,
        };

        var elementTypeName = element.NormalizeWhitespace(indentation: string.Empty, elasticTrivia: false).ToString();
        return new ModuleParameterType(ModuleKernelParameterKind.Array, ModuleLaunchSetting.None, typeName,
            elementTypeName, reason);
    }

    private static bool IsVoid(TypeSyntax syntax) =>
        syntax is PredefinedTypeSyntax predefined && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword);

    // The reference types a name alone identifies; any other class fails to compile in the launcher (see remarks)
    private static bool IsReferenceTypeName(TypeSyntax syntax)
    {
        if (syntax is PredefinedTypeSyntax predefined)
        {
            return predefined.Keyword.Kind() is SyntaxKind.StringKeyword or SyntaxKind.ObjectKeyword;
        }

        // [WHY] The launcher file starts with 'using System;', so the names without 'System.' are the same types
        return GetSystemTypeName(syntax.ToString()) is "dynamic" or "String" or "Object" or "Array";
    }

    private static bool IsDimensionTypeName(string typeName) =>
        GetSystemTypeName(typeName) is "uint" or "ushort" or "byte" or "char"
            or "UInt32" or "UInt16" or "Byte" or "Char";

    private static bool IsStreamTypeName(string typeName) => GetSystemTypeName(typeName) is "nint" or "IntPtr";

    // The name without 'global::' and 'System.' (System.UInt32 and UInt32 name the same type in the launcher file)
    private static string GetSystemTypeName(string typeName)
    {
        var name = typeName.StartsWith(GlobalPrefix, StringComparison.Ordinal)
            ? typeName[GlobalPrefix.Length..]
            : typeName;
        return name.StartsWith(SystemPrefix, StringComparison.Ordinal) ? name[SystemPrefix.Length..] : name;
    }
}
