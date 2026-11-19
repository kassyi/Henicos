using Kassyi.GSharp.Compiler.Helpers;

namespace Kassyi.GSharp.Compiler.Aot;

/// <summary>The device code that a native module imports from other assemblies, in the order the module needs it.</summary>
/// <param name="Source">The structs (definitions, then <c>Dispose</c> definitions), then the functions (declarations, then definitions).</param>
/// <param name="Includes">The <c>#include</c> targets of the imported text.</param>
/// <param name="FullNames">The full name of each imported name that its assembly shortened, for the symbol file.</param>
/// <param name="ErrorSites">
/// The error sites of the <c>throw</c> statements of the imported functions, which the module registers with its kernels.
/// </param>
/// <param name="Calls">
/// The function names that each name of a placed record calls among the placed records (each name of a struct record
/// stands for all of its members), for the spill warnings (<see cref="DeviceImportAssembler.ExtendReach"/>).
/// </param>
internal readonly record struct ImportedDeviceCode(
    string Source,
    EquatableArray<string> Includes,
    EquatableArray<(string Name, string FullName)> FullNames,
    EquatableArray<ErrorSiteEntry> ErrorSites = default,
    EquatableArray<(string Name, string Callee)> Calls = default)
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
