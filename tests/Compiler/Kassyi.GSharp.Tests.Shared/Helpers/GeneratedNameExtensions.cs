using Kassyi.GSharp.Naming;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>Reads generated C++ with renamed user identifiers translated back to their C# names.</summary>
/// <remarks>
/// For tests that assert the structure of the emitted code rather than its naming. Naming itself is covered by
/// <c>CppIdentifierRenameTests</c> and the snapshots.
/// </remarks>
public static class GeneratedNameExtensions
{
    public static string WithCSharpNames(this string generatedSource) => GSharpNameEncoding.DecodeText(generatedSource);
}
