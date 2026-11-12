using Kassyi.GSharp.Compiler.Helpers;

namespace Kassyi.GSharp.Compiler.Aot;

/// <summary>The device code that a native module imports from other assemblies, in the order the module needs it.</summary>
/// <param name="Source">The structs (definitions, then <c>Dispose</c> definitions), then the functions (declarations, then definitions).</param>
/// <param name="Includes">The <c>#include</c> targets of the imported text.</param>
/// <param name="FullNames">The full name of each imported name that its assembly shortened, for the symbol file.</param>
internal readonly record struct ImportedDeviceCode(
    string Source,
    EquatableArray<string> Includes,
    EquatableArray<(string Name, string FullName)> FullNames)
{
    /// <summary>The full name of <paramref name="name"/>, when it is an imported name that was shortened.</summary>
    public string? FindFullName(string name)
    {
        foreach (var (shortened, full) in FullNames)
        {
            if (string.Equals(shortened, name, StringComparison.Ordinal))
            {
                return full;
            }
        }

        return null;
    }
}
