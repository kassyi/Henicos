#!/usr/bin/env dotnet
#:property PublishAot=true
#:property InvariantGlobalization=true
#:property JsonSerializerIsReflectionEnabledByDefault=false
#:property TargetFramework=net10.0
#:property Nullable=enable
#:property ImplicitUsings=enable
#:property GenerateDocumentationFile=false
#:include ReportModel.cs
#:include CommandLine.cs
#:include Collector.cs
#:include Privacy.cs
#:include ProcessProbe.cs
#:include HardwareProbe.cs
#:include GpuProbe.cs
#:include ToolchainProbe.cs
#:include CheckoutProbe.cs
#:include MarkdownReport.cs

// collect-env: prints the hardware and software environment of a G# bug report as Markdown (default) or JSON.
//
// Needs the .NET SDK 10.0.300 or later: older SDKs reject #:include ("Unrecognized directive 'include'").
//
// Usage:
//   dotnet run tools/collect-env/collect-env.cs [-- --output <file>] [--json] [--help]
//   collect-env-win-x64.exe | collect-env-linux-x64   (NativeAOT build, see below)
//
// Publish (NativeAOT cannot cross-compile; build each target on its own OS):
//   dotnet publish tools/collect-env/collect-env.cs -r win-x64      (Windows)
//   dotnet publish tools/collect-env/collect-env.cs -r linux-x64    (Linux / WSL; needs clang and zlib headers)
//
// Rules: no reflection (System.Text.Json source generator), no WMI / PowerShell (registry on Windows), no project
// references (it must run when the G# build or the .NET SDK is broken). A missing tool is reported as "not found".
// Privacy: no user name, host name, serial numbers or absolute paths under the user profile; CUDA_PATH is only
// reported as set or unset. The MSVC query repeats MsvcToolchain.ResolveVcvarsEnvironment (src/Compiler/
// Kassyi.GSharp.Compiler.Tasks/Toolchains/MsvcToolchain.cs); keep both in sync.

using System.Text.Json;

CommandLineOptions options = CommandLine.Parse(args);
if (options.ExitCode is { } exitCode)
{
    return exitCode;
}

ReportModel model = Collector.Collect();
string text = options.Json
    ? JsonSerializer.Serialize(model, ReportJsonContext.Default.ReportModel) + "\n"
    : MarkdownReport.ToMarkdown(model);
return CommandLine.WriteReport(text, options.OutputPath);
