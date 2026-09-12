using System.Security.Cryptography;
using Microsoft.Build.Utilities;
using Microsoft.Build.Framework;

namespace Kassyi.GSharp.Compiler.Tasks;

internal static class ArtifactHelper
{
    public static bool ProcessArtifact(string outputFilePath, string keyFilePath, TaskLoggingHelper log)
    {
        var fileData = File.ReadAllBytes(outputFilePath);
        var hashBytes = ComputeSha256(fileData);

        if (File.Exists(keyFilePath))
        {
            log.LogMessage(MessageImportance.Normal, $"Encrypting artifact using key from '{keyFilePath}'");
            var rawKey = File.ReadAllBytes(keyFilePath);

            if (rawKey.Length != 32)
            {
                log.LogError($"Invalid AES key length in '{keyFilePath}'. Expected 32 bytes, got {rawKey.Length}.");
                return false;
            }

            var encryptedData = EncryptAes256Cbc(fileData, rawKey);
            var finalData = new byte[encryptedData.Length + 32];
            Buffer.BlockCopy(encryptedData, 0, finalData, 0, encryptedData.Length);
            Buffer.BlockCopy(hashBytes, 0, finalData, encryptedData.Length, 32);
            File.WriteAllBytes(outputFilePath, finalData);
            return true;
        }

        log.LogMessage(MessageImportance.Low, $"No .key file found at '{keyFilePath}'. Skipping encryption.");
        var plainFinalData = new byte[fileData.Length + 32];
        Buffer.BlockCopy(fileData, 0, plainFinalData, 0, fileData.Length);
        Buffer.BlockCopy(hashBytes, 0, plainFinalData, fileData.Length, 32);
        File.WriteAllBytes(outputFilePath, plainFinalData);
        return true;
    }

    private static byte[] EncryptAes256Cbc(byte[] plainData, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        var iv = aes.IV;

        using var ms = new MemoryStream();
        ms.Write([.. "GSHPENC1"u8], 0, 8);
        ms.Write(iv, 0, iv.Length);

        using (var encryptor = aes.CreateEncryptor())
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        {
            cs.Write(plainData, 0, plainData.Length);
            cs.FlushFinalBlock();
        }

        return ms.ToArray();
    }

    private static byte[] ComputeSha256(byte[] data)
    {
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(data);
    }

    public static string GetCleanKernelFileName(string sourcePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(sourcePath);
        if (fileName.EndsWith(".g", StringComparison.OrdinalIgnoreCase))
        {
            fileName = fileName.Substring(0, fileName.Length - 2);
        }

        return fileName;
    }
}
