using System;
using System.Collections.Generic;
using System.IO;
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
        logOut.WriteLine("========================================");
        logOut.WriteLine("BENCHMARK COMPARISON & OVERHEAD SUMMARY");
        logOut.WriteLine("========================================");
        logOut.WriteLine(string.Format("{0,-30} | {1,10} | {2,18} | {3,18} | {4,12} | {5,10}",
            "Benchmark Name", "CPU (ms)", "Native CUDA (Mean)", "G# CUDA (Mean)", "G#/Native Ratio", "Speedup vs CPU"));
        logOut.WriteLine(new string('-', 120));

        foreach (var r in results)
        {
            if (r.Success)
            {
                double ratio = r.NativeStats.Mean > 0 ? r.GsharpStats.Mean / r.NativeStats.Mean : 1.0;
                double speedup = r.GsharpStats.Mean > 0 ? r.CpuMeanMs / r.GsharpStats.Mean : 1.0;
                logOut.WriteLine(string.Format("{0,-30} | {1,10:F2} | {2,15:F2} ms | {3,15:F2} ms | {4,11:F2}x | {5,9:F1}x",
                    r.Name, r.CpuMeanMs, r.NativeStats.Mean, r.GsharpStats.Mean, ratio, speedup));
            }
            else
            {
                logOut.WriteLine(string.Format("{0,-30} | {1,10:F2} | {2,18} | {3,18} | {4,12} | {5,10}",
                    r.Name, r.CpuMeanMs, "FAIL", "FAIL", "N/A", "N/A"));
            }
        }
        logOut.WriteLine("========================================");
    }
}
