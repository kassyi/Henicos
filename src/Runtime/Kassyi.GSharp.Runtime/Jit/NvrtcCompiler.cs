using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Runtime.Jit;

/// <summary>Compiles in-memory CUDA C++ source text directly to PTX bytecode via the NVIDIA Runtime Compilation (NVRTC) API.</summary>
public static class NvrtcCompiler
{
    public static byte[] CompileToPtx(string cuSource, string kernelName, string[] options)
    {
        var prog = IntPtr.Zero;
        try
        {
            NvrtcException.ThrowIfError(
                NvrtcApi.nvrtcCreateProgram(out prog, cuSource, $"{kernelName}.cu", 0, null, null),
                "Failed to create NVRTC program.");

            var compileResult = NvrtcApi.nvrtcCompileProgram(prog, options.Length, options);

            // Get log (warnings or errors)
            NvrtcApi.nvrtcGetProgramLogSize(prog, out var logSize);
            if (logSize > 1)
            {
                var logBytes = new byte[logSize];
                NvrtcApi.nvrtcGetProgramLog(prog, logBytes);
                var log = Encoding.UTF8.GetString(logBytes).TrimEnd('\0');

                if (compileResult != NvrtcResult.Success)
                {
                    throw new Exception($"NVRTC Compilation Failed:\n{log}");
                }

                Logger.Debug("NvrtcCompiler", $"[NVRTC Log]:\n{log}");
            }

            NvrtcException.ThrowIfError(compileResult, "Compilation failed (no log available).");

            // Get PTX
            NvrtcException.ThrowIfError(
                NvrtcApi.nvrtcGetPTXSize(prog, out var ptxSize),
                "Failed to get PTX size.");

            var ptxBytes = new byte[ptxSize];
            NvrtcException.ThrowIfError(
                NvrtcApi.nvrtcGetPTX(prog, ptxBytes),
                "Failed to get PTX data.");

            return ptxBytes;
        }
        finally
        {
            if (prog != IntPtr.Zero)
            {
                NvrtcApi.nvrtcDestroyProgram(ref prog);
            }
        }
    }
}
