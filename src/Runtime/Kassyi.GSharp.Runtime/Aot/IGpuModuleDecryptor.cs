namespace Kassyi.GSharp.Runtime.Aot;

using System;

/// <summary>
/// Defines an interface for injecting external (commercial/proprietary) decryption logic 
/// into the open-source GSharp AOT loader.
/// </summary>
public interface IGpuModuleDecryptor
{
    /// <summary>
    /// Decrypts the encrypted PTX module and loads it into the CUDA driver.
    /// The implementation is responsible for allocating memory, decrypting, loading via cuModuleLoadDataEx,
    /// and securely zeroing the memory immediately after loading (Ephemeral JIT Decryption).
    /// </summary>
    /// <param name="encryptedFatbin">The encrypted fatbin payload containing the PTX module.</param>
    /// <param name="kernelName">The target kernel name to extract from the module.</param>
    /// <returns>A tuple containing the loaded CUmodule and CUfunction pointers.</returns>
    (IntPtr Module, IntPtr Function) DecryptAndLoad(ReadOnlySpan<byte> encryptedFatbin, string kernelName);
}
