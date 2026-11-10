#nullable enable
using System.Diagnostics;

namespace Kassyi.GSharp.Tools.CollectEnv.Tests;

/// <summary>Runs tools/collect-env/collect-env.cs as a process and collects its output.</summary>
internal static class CollectEnvToolRunner
{
    public static (int ExitCode, string Stdout, string Stderr) Run(
        string[] toolArgs,
        Action<IDictionary<string, string?>> configureEnvironment)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = RepositoryPaths.Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(RepositoryPaths.Combine("tools", "collect-env", "collect-env.cs"));
        startInfo.ArgumentList.Add("--");
        foreach (string arg in toolArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        configureEnvironment(startInfo.Environment);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("dotnet did not start.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("collect-env did not finish in 5 minutes.");
        }

        process.WaitForExit();
        return (process.ExitCode, stdout.Result, stderr.Result);
    }
}
