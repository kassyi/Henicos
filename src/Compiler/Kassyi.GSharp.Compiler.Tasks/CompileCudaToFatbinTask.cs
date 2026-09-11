using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Kassyi.GSharp.Compiler.Tasks;

public class CompileCudaToFatbinTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public ITaskItem[] CudaSources { get; set; } = [];

    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    public string TargetArchitectures { get; set; } =
        "compute_50,sm_50;compute_70,sm_70;compute_80,sm_80;compute_86,sm_86";

    public string NvccPath { get; set; } = "nvcc";

    [Output]
    public ITaskItem[] OutputFatbins { get; set; } = [];

    public override bool Execute()
    {
        if (CudaSources.Length == 0)
        {
            return true;
        }

        if (!Directory.Exists(OutputDirectory))
        {
            Directory.CreateDirectory(OutputDirectory);
        }

        var outputItems = new List<ITaskItem>();
        var architectures = TargetArchitectures.Split([';'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var sourceItem in CudaSources)
        {
            var taskItem = ProcessSourceItem(sourceItem, architectures);
            if (taskItem == null)
            {
                return false;
            }

            outputItems.Add(taskItem);
        }

        OutputFatbins = [.. outputItems];
        return true;
    }

    private ITaskItem? ProcessSourceItem(ITaskItem sourceItem, string[] architectures)
    {
        var sourcePath = sourceItem.GetMetadata("FullPath");
        var fileName = GetCleanKernelFileName(sourcePath);
        var outputFatbinPath = Path.Combine(OutputDirectory, $"{fileName}.fatbin");
        var keyFilePath = sourcePath + ".key";

        if (!CompileWithNvcc(sourcePath, outputFatbinPath, architectures))
        {
            return null;
        }

        var fatbinHash = ProcessFatbinArtifact(outputFatbinPath, keyFilePath);
        if (fatbinHash == null)
        {
            return null;
        }

        var taskItem = new TaskItem(outputFatbinPath);
        taskItem.SetMetadata("Hash", fatbinHash);
        taskItem.SetMetadata("LogicalName", $"Kassyi.GSharp.Kernels.{Path.GetFileName(outputFatbinPath)}");
        return taskItem;
    }

    private bool CompileWithNvcc(string sourcePath, string outputFatbinPath, string[] architectures)
    {
        var args = BuildNvccArguments(sourcePath, outputFatbinPath, architectures);
        Log.LogMessage(MessageImportance.Normal, $"Compiling CUDA source: {sourcePath}");

        if (!RunNvccProcess(args))
        {
            return false;
        }

        if (File.Exists(outputFatbinPath))
        {
            return true;
        }

        Log.LogError($"nvcc succeeded but output fatbin was not found at '{outputFatbinPath}'.");
        return false;
    }

    private string BuildNvccArguments(string sourcePath, string outputFatbinPath, string[] architectures)
    {
        var args = new StringBuilder();
        args.Append("--fatbin ");
        foreach (var arch in architectures)
        {
            var parts = arch.Split(',');
            if (parts.Length == 2)
            {
                args.Append($"-gencode arch={parts[0]},code={parts[1]} ");
            }
        }

        args.Append($"-o \"{outputFatbinPath}\" \"{sourcePath}\"");
        return args.ToString();
    }

    private bool RunNvccProcess(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = NvccPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            Log.LogError(
                $"Failed to start nvcc at '{NvccPath}'. Please ensure CUDA Toolkit is installed and nvcc is in PATH.");
            return false;
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode == 0)
        {
            return true;
        }

        Log.LogError($"nvcc compilation failed with exit code {process.ExitCode}.");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Log.LogMessage(MessageImportance.High, output);
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            Log.LogError(error);
        }

        return false;
    }

    private string? ProcessFatbinArtifact(string outputFatbinPath, string keyFilePath)
    {
        var fatbinData = File.ReadAllBytes(outputFatbinPath);

        if (File.Exists(keyFilePath))
        {
            Log.LogMessage(MessageImportance.Normal, $"Encrypting fatbin using key from '{keyFilePath}'");
            var rawKey = File.ReadAllBytes(keyFilePath);

            if (rawKey.Length != 32)
            {
                Log.LogError($"Invalid AES key length in '{keyFilePath}'. Expected 32 bytes, got {rawKey.Length}.");
                return null;
            }

            var fatbinHash = ComputeSha256Hex(fatbinData);
            var encryptedData = EncryptAes256Cbc(fatbinData, rawKey);

            File.WriteAllBytes(outputFatbinPath, encryptedData);
            File.WriteAllText(outputFatbinPath + ".hash", fatbinHash);
            return fatbinHash;
        }

        Log.LogMessage(MessageImportance.Low, $"No .key file found at '{keyFilePath}'. Skipping encryption.");
        var plaintextHash = ComputeSha256Hex(fatbinData);
        File.WriteAllText(outputFatbinPath + ".hash", plaintextHash);
        return plaintextHash;
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

    private static string ComputeSha256Hex(byte[] data)
    {
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
    }

    private static string GetCleanKernelFileName(string sourcePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(sourcePath);
        if (fileName.EndsWith(".g", StringComparison.OrdinalIgnoreCase))
        {
            fileName = fileName.Substring(0, fileName.Length - 2);
        }

        return fileName;
    }
}