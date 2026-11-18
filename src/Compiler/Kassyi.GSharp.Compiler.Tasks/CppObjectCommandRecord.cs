using System.Security.Cryptography;

namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>
/// Builds and hashes the command record stored next to each compiled CPU native object file so unchanged
/// translation units skip recompilation when their source, shared module header, compiler, and flags match.
/// </summary>
internal static class CppObjectCommandRecord
{
    public const string FileExtension = ".record";
    public const string RecordDescription = "C++ object command";
    public const string MissingHeaderSentinel = "none";
    public const string MissingUnitSentinel = "missing";

    public static string Build(
        string architecture,
        CompilerIdentity compiler,
        string compileArguments,
        string unitSourceHash,
        string headerSourceHash) =>
        $"version=1\narch={architecture}\ncompiler={compiler.Path}\n" +
        $"compiler-version={compiler.Version}\nargs={compileArguments}\n" +
        $"unit={unitSourceHash}\nheader={headerSourceHash}";

    public static string GetModuleHeaderPath(string unitSourcePath, string moduleStem)
    {
        var sourceDir = Path.GetDirectoryName(unitSourcePath) ?? string.Empty;
        return Path.Combine(sourceDir, moduleStem + ".g.hpp");
    }

    public static string ComputeFileSha256Hex(string? filePath, string missingSentinel)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return missingSentinel;
        }

        using var sha256 = SHA256.Create();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var digest = sha256.ComputeHash(stream);

        const string HexDigits = "0123456789abcdef";
        var hex = new char[digest.Length * 2];
        for (var i = 0; i < digest.Length; i++)
        {
            hex[i * 2] = HexDigits[digest[i] >> 4];
            hex[i * 2 + 1] = HexDigits[digest[i] & 0xF];
        }

        return new string(hex);
    }
}
