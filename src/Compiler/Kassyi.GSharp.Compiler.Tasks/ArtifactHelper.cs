using Kassyi.GSharp.Cryptography;

namespace Kassyi.GSharp.Compiler.Tasks;

public static class ArtifactHelper
{
    /// <summary>
    /// Diagnostic warning code emitted when an artifact is signed using the default development signing key.
    /// </summary>
    public const string DefaultSigningKeyWarningCode = "GS0002";

    /// <summary>
    /// Diagnostic error code emitted when GSHARP_SIGNING_KEY is set but is invalid.
    /// </summary>
    public const string InvalidSigningKeyErrorCode = "GS0003";

    public static bool ProcessArtifact(string outputFilePath, TaskLoggingHelper log)
    {
        if (!TryResolveSigningKey(log, out var keySeed))
        {
            return false;
        }

        var fileData = File.ReadAllBytes(outputFilePath);
        var signature = Ed25519.Sign(fileData, keySeed);

        var signedData = new byte[fileData.Length + Ed25519.SignatureSize];
        Buffer.BlockCopy(fileData, 0, signedData, 0, fileData.Length);
        Buffer.BlockCopy(signature, 0, signedData, fileData.Length, Ed25519.SignatureSize);
        File.WriteAllBytes(outputFilePath, signedData);
        return true;
    }

    private static bool TryResolveSigningKey(TaskLoggingHelper log, out byte[] keySeed)
    {
        var envKey = Environment.GetEnvironmentVariable("GSHARP_SIGNING_KEY");
        if (string.IsNullOrWhiteSpace(envKey))
        {
            log.LogWarning(
                subcategory: null,
                warningCode: DefaultSigningKeyWarningCode,
                helpKeyword: null,
                file: null,
                lineNumber: 0,
                columnNumber: 0,
                endLineNumber: 0,
                endColumnNumber: 0,
                message: "FATBIN artifact signed using the default G# development key. Do NOT use this binary in production environments; specify 'GSHARP_SIGNING_KEY' to sign with a private key.");
            keySeed = Ed25519.DefaultDevKeySeed.ToArray();
            return true;
        }

        var trimmed = envKey.AsSpan().Trim();
        if (ValidateAndParseSigningKey(trimmed, out keySeed, out var reason))
        {
            return true;
        }

        log.LogError(
            subcategory: null,
            errorCode: InvalidSigningKeyErrorCode,
            helpKeyword: null,
            file: null,
            lineNumber: 0,
            columnNumber: 0,
            endLineNumber: 0,
            endColumnNumber: 0,
            message: $"GSHARP_SIGNING_KEY is set but is not a valid signing key: {reason}. Expected 64 hex characters or 44 Base64 characters ending with '='.");
        keySeed = [];
        return false;
    }

    private static bool ValidateAndParseSigningKey(ReadOnlySpan<char> trimmed, out byte[] bytes, out string reason)
    {
        bytes = [];
        reason = string.Empty;

        if (trimmed.Length == 64)
        {
            if (TryParseHex(trimmed, out bytes))
            {
                return true;
            }

            reason = "invalid character";
            return false;
        }

        if (trimmed.Length == 44)
        {
            if (trimmed[43] == '=')
            {
                if (IsValidBase64Head(trimmed.Slice(0, 43)))
                {
                    bytes = Convert.FromBase64String(trimmed.ToString());
                    return true;
                }

                reason = "invalid character";
                return false;
            }

            reason = "expected 44 Base64 characters ending with '='";
            return false;
        }

        if (ContainsInvalidChar(trimmed))
        {
            reason = "invalid character";
            return false;
        }

        if (trimmed[trimmed.Length - 1] == '=' || HasChar(trimmed, '+') || HasChar(trimmed, '/'))
        {
            reason = "expected 44 Base64 characters ending with '='";
            return false;
        }

        reason = "expected 64 hex characters";
        return false;
    }

    private static bool IsValidBase64Head(ReadOnlySpan<char> span)
    {
        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];
            if (!(c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '+' or '/'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsInvalidChar(ReadOnlySpan<char> span)
    {
        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];
            if (!(c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '+' or '/' or '='))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasChar(ReadOnlySpan<char> span, char target)
    {
        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] == target)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseHex(ReadOnlySpan<char> hex, out byte[] bytes)
    {
        bytes = new byte[32];
        for (var i = 0; i < 32; i++)
        {
            var hi = HexVal(hex[i * 2]);
            var lo = HexVal(hex[i * 2 + 1]);
            if (hi < 0 || lo < 0)
            {
                bytes = [];
                return false;
            }

            bytes[i] = (byte)((hi << 4) | lo);
        }

        return true;
    }

    private static int HexVal(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    public static string GetCleanKernelFileName(string sourcePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(sourcePath);
        if (fileName.EndsWith(".g", StringComparison.OrdinalIgnoreCase))
        {
            fileName = fileName[..^2];
        }

        return fileName;
    }
}
