using Kassyi.GSharp.Cryptography;

namespace Kassyi.GSharp.Compiler.Tasks;

public static class ArtifactHelper
{
    /// <summary>
    /// Diagnostic warning code emitted when an artifact is signed using the default development signing key.
    /// </summary>
    public const string DefaultSigningKeyWarningCode = "GS0002";

    public static bool ProcessArtifact(string outputFilePath, TaskLoggingHelper log)
    {
        var fileData = File.ReadAllBytes(outputFilePath);
        var keySeed = ResolveSigningKey(log);
        var signature = Ed25519.Sign(fileData, keySeed);

        var signedData = new byte[fileData.Length + Ed25519.SignatureSize];
        Buffer.BlockCopy(fileData, 0, signedData, 0, fileData.Length);
        Buffer.BlockCopy(signature, 0, signedData, fileData.Length, Ed25519.SignatureSize);
        File.WriteAllBytes(outputFilePath, signedData);
        return true;
    }

    private static ReadOnlySpan<byte> ResolveSigningKey(TaskLoggingHelper log)
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
            return Ed25519.DefaultDevKeySeed;
        }

        try
        {
            var trimmed = envKey.AsSpan().Trim();
            if (trimmed.Length == 64)
            {
                var key = new byte[32];
                for (var i = 0; i < 32; i++)
                {
                    key[i] = Convert.ToByte(trimmed.Slice(i * 2, 2).ToString(), 16);
                }

                return key;
            }

            var base64Key = Convert.FromBase64String(trimmed.ToString());
            if (base64Key.Length == 32)
            {
                return base64Key;
            }

            log.LogWarning(
                subcategory: null,
                warningCode: DefaultSigningKeyWarningCode,
                helpKeyword: null,
                file: null,
                lineNumber: 0,
                columnNumber: 0,
                endLineNumber: 0,
                endColumnNumber: 0,
                message: "GSHARP_SIGNING_KEY environment variable is invalid (must be 32 bytes hex or base64). Falling back to default development signing key.");
        }
        catch (Exception ex)
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
                message: $"Failed to parse GSHARP_SIGNING_KEY: {ex.Message}. Falling back to default development signing key.");
        }

        return Ed25519.DefaultDevKeySeed;
    }

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
