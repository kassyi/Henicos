namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// The single source of the names that the generated host C# uses for the native shadow structs, their marshaling
/// methods and the per-assembly module context. The side that declares a name and the side that refers to it both go
/// through this module, so a change to a naming rule happens in one place.
/// </summary>
internal static class AotHostNames
{
    /// <summary>The class that declares the native shadow structs and their marshaling methods.</summary>
    public const string AotContextsClassName = "AotContexts";

    /// <summary>The prefix of the method that copies a host struct to its native shadow struct.</summary>
    public const string MarshalMethodPrefix = "Marshal_";

    /// <summary>The prefix of the method that copies a native shadow struct back to its host struct.</summary>
    public const string UnmarshalMethodPrefix = "Unmarshal_";

    /// <summary>The prefix of the class that loads the fatbin module of one assembly.</summary>
    public const string ModuleContextPrefix = "GSharpModuleContext_";

    private const string ContextSuffix = "Context";

    /// <summary>
    /// Appends the C# path of the native shadow struct of <paramref name="symbol"/>:
    /// <c>AotContexts.{Name}</c>, or <c>{parent path}.{Name}</c> for a struct nested in a custom struct.
    /// </summary>
    public static ref readonly SourceWriter AppendAotStructPath(this in SourceWriter writer, ISymbol symbol)
    {
        if (GetEnclosingCustomStruct(symbol) is { } parentType)
        {
            writer.AppendAotStructPath(parentType);
            writer.Append('.');
        }
        else
        {
            writer.Append(AotContextsClassName).Append('.');
        }

        writer.Append(GetNativeStructName(symbol));
        return ref writer;
    }

    /// <summary>The name that the native shadow struct of <paramref name="symbol"/> declares: the type name without a trailing <c>Context</c>.</summary>
    public static string GetNativeStructName(ISymbol symbol) => TrimContextSuffix(symbol.Name);

    /// <summary>
    /// The custom structs that enclose <paramref name="structSymbol"/>, outermost first, which the native declaration
    /// reproduces as nested partial structs.
    /// </summary>
    public static Stack<INamedTypeSymbol> GetEnclosingCustomStructs(INamedTypeSymbol structSymbol)
    {
        var parents = new Stack<INamedTypeSymbol>();
        var parent = GetEnclosingCustomStruct(structSymbol);
        while (parent is not null)
        {
            parents.Push(parent);
            parent = GetEnclosingCustomStruct(parent);
        }

        return parents;
    }

    /// <summary>Appends the key of a struct in the marshaling method names: <c>Parent_Child</c> for a nested type.</summary>
    public static ref readonly SourceWriter AppendMarshalTypeKey(this in SourceWriter writer, ISymbol symbol)
    {
        if (symbol.ContainingType is { } parentType)
        {
            writer.AppendMarshalTypeKey(parentType);
            writer.Append('_');
        }

        writer.Append(symbol.Name);
        return ref writer;
    }

    /// <summary>Appends <c>Marshal_{key}</c>.</summary>
    public static ref readonly SourceWriter AppendMarshalMethodName(this in SourceWriter writer, ISymbol symbol)
    {
        writer.Append(MarshalMethodPrefix).AppendMarshalTypeKey(symbol);
        return ref writer;
    }

    /// <summary>Appends <c>Unmarshal_{key}</c>.</summary>
    public static ref readonly SourceWriter AppendUnmarshalMethodName(this in SourceWriter writer, ISymbol symbol)
    {
        writer.Append(UnmarshalMethodPrefix).AppendMarshalTypeKey(symbol);
        return ref writer;
    }

    /// <summary>Appends <c>AotContexts.Marshal_{key}</c>, the way a kernel wrapper calls it.</summary>
    public static ref readonly SourceWriter AppendQualifiedMarshalMethodName(this in SourceWriter writer, ISymbol symbol)
    {
        writer.Append(AotContextsClassName).Append('.').AppendMarshalMethodName(symbol);
        return ref writer;
    }

    /// <summary>Appends <c>AotContexts.Unmarshal_{key}</c>, the way a kernel wrapper calls it.</summary>
    public static ref readonly SourceWriter AppendQualifiedUnmarshalMethodName(this in SourceWriter writer, ISymbol symbol)
    {
        writer.Append(AotContextsClassName).Append('.').AppendUnmarshalMethodName(symbol);
        return ref writer;
    }

    /// <summary>The name of the module context class of an assembly.</summary>
    public static string GetModuleContextName(string assemblyName) => ModuleContextPrefix + ToSafeIdentifier(assemblyName);

    /// <summary>
    /// Converts an assembly name or namespace into a safe C# identifier segment by replacing '.' with '_'.
    /// </summary>
    public static string ToSafeIdentifier(string name)
    {
        if (name.IndexOf('.') < 0)
        {
            return name;
        }

        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] == '.')
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    private static INamedTypeSymbol? GetEnclosingCustomStruct(ISymbol symbol) =>
        symbol.ContainingType is { } parentType && CudaLayoutRules.IsCustomStruct(parentType) ? parentType : null;

    private static string TrimContextSuffix(string name) =>
        name.EndsWith(ContextSuffix) ? name[..^ContextSuffix.Length] : name;
}
