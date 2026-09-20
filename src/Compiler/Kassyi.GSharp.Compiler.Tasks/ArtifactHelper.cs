using System.Security.Cryptography;

namespace Kassyi.GSharp.Compiler.Tasks;

internal static class ArtifactHelper
{
    public static bool ProcessArtifact(string outputFilePath, TaskLoggingHelper log)
    {
        var fileData = File.ReadAllBytes(outputFilePath);
        var hashBytes = ComputeSha256(fileData);

        var plainFinalData = new byte[fileData.Length + 32];
        Buffer.BlockCopy(fileData, 0, plainFinalData, 0, fileData.Length);
        Buffer.BlockCopy(hashBytes, 0, plainFinalData, fileData.Length, 32);
        File.WriteAllBytes(outputFilePath, plainFinalData);
        return true;
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
