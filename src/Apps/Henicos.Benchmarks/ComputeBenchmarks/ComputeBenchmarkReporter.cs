using System.Text.Json;

namespace Henicos.Benchmarks.ComputeBenchmarks;

public static class ComputeBenchmarkReporter
{
    public static void ReportJson(List<BenchmarkResult> results, TextWriter logOut)
    {
        var json = JsonSerializer.Serialize(results,
            new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
        );
        logOut.WriteLine(json);
    }

    public static void ReportTable(List<BenchmarkResult> results, TextWriter logOut)
    {
        logOut.WriteLine("\n");
        logOut.WriteLine("===================================================");
        logOut.WriteLine("BENCHMARK EXECUTION TIME & SPEEDUP SUMMARY");
        logOut.WriteLine("===================================================");
        logOut.WriteLine(string.Format("{0,-26} | {1,8} | {2,11} | {3,9} | {4,7} | {5,7}",
            "Benchmark", "CPU (ms)", "Native (ms)", "G# (ms)", "Ratio", "Speedup"));
        logOut.WriteLine(new string('-', 80));

        foreach (var r in results)
        {
            var name = FormatName(r.Name);
            if (r.Success)
            {
                double ratio = r.NativeStats.Mean > 0 ? r.GsharpStats.Mean / r.NativeStats.Mean : 1.0;
                double speedup = r.GsharpStats.Mean > 0 ? r.CpuMeanMs / r.GsharpStats.Mean : 1.0;
                logOut.WriteLine(string.Format("{0,-26} | {1,8:F2} | {2,8:F2} ms | {3,6:F2} ms | {4,6:F2}x | {5,6:F1}x",
                    name, r.CpuMeanMs, r.NativeStats.Mean, r.GsharpStats.Mean, ratio, speedup));
            }
            else
            {
                logOut.WriteLine(string.Format("{0,-26} | {1,8:F2} | {2,11} | {3,9} | {4,7} | {5,7}",
                    name, r.CpuMeanMs, "FAIL", "FAIL", "N/A", "N/A"));
            }
        }
        logOut.WriteLine("===================================================");

        logOut.WriteLine("\n");
        logOut.WriteLine("===================================================");
        logOut.WriteLine("KERNEL HARDWARE RESOURCES & INSTRUCTION SUMMARY");
        logOut.WriteLine("===================================================");
        logOut.WriteLine(string.Format("{0,-26} | {1,13} | {2,15} | {3,15}",
            "Benchmark", "Regs (Nat/G#)", "Spill (Nat/G#)", "SASS (Nat/G#)"));
        logOut.WriteLine(new string('-', 80));

        foreach (var r in results)
        {
            var name = FormatName(r.Name);
            var nativeRegs = r.NativeAttributes.HasValue ? r.NativeAttributes.Value.NumRegs.ToString() : "-";
            var gsharpRegs = r.GsharpAttributes.HasValue ? r.GsharpAttributes.Value.NumRegs.ToString() : "-";
            var regsStr = $"{nativeRegs} / {gsharpRegs}";

            var nativeSpill = r.NativeAttributes.HasValue ? r.NativeAttributes.Value.LocalSizeBytes.ToString() : "-";
            var gsharpSpill = r.GsharpAttributes.HasValue ? r.GsharpAttributes.Value.LocalSizeBytes.ToString() : "-";
            var spillStr = $"{nativeSpill} / {gsharpSpill} B";

            var nativeInst = r.NativeInstructionCount >= 0 ? r.NativeInstructionCount.ToString() : "-";
            var gsharpInst = r.GsharpInstructionCount >= 0 ? r.GsharpInstructionCount.ToString() : "-";
            var instStr = $"{nativeInst} / {gsharpInst}";

            logOut.WriteLine(string.Format("{0,-26} | {1,13} | {2,15} | {3,15}",
                name, regsStr, spillStr, instStr));
        }
        logOut.WriteLine("================================================================================");
    }

    private static string FormatName(string name) =>
        name.Replace("Compression Function ", "").Trim();
}
