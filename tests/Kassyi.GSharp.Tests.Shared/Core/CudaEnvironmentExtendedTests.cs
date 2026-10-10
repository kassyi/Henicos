#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

[Collection(TestCollections.GlobalStateSequential)]
public class CudaEnvironmentExtendedTests : IDisposable
{
    public void Dispose()
    {
        CudaEnvironment.Resolver = new DefaultCudaPathResolver();
    }

    private class MockResolver : DefaultCudaPathResolver
    {
        public Dictionary<string, string?> EnvVars { get; } = new();
        public HashSet<string> ExistingDirs { get; } = new();
        public Dictionary<string, string[]> SubDirs { get; } = new();
        public bool MockIsWindows { get; set; } = false;
        public bool MockIsLinux { get; set; } = false;

        protected override string? GetEnvironmentVariable(string name) => EnvVars.TryGetValue(name, out var v) ? v : null;
        protected override bool DirectoryExists(string path) => ExistingDirs.Contains(path);
        protected override string[] GetDirectories(string path, string searchPattern) => SubDirs.TryGetValue(path, out var dirs) ? dirs : [];
        protected override bool IsWindows() => MockIsWindows;
        protected override bool IsLinux() => MockIsLinux;
    }

    [Fact]
    public void Windows_EmptySubdirs_ReturnsNull()
    {
        var mock = new MockResolver { MockIsWindows = true };
        var baseDir = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA";
        mock.ExistingDirs.Add(baseDir);
        mock.SubDirs[baseDir] = [];

        CudaEnvironment.Resolver = mock;
        Assert.Null(CudaEnvironment.GetCudaPath());
    }

    [Fact]
    public void Windows_InvalidVersionFormat_FallbackToFirst()
    {
        var mock = new MockResolver { MockIsWindows = true };
        var baseDir = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA";
        mock.ExistingDirs.Add(baseDir);
        // "vTest" fails Version.TryParse. Should be stored if bestPath == null.
        mock.SubDirs[baseDir] = [Path.Combine(baseDir, "vTest"), Path.Combine(baseDir, "vAnotherInvalid")];

        CudaEnvironment.Resolver = mock;
        Assert.Equal(Path.Combine(baseDir, "vTest"), CudaEnvironment.GetCudaPath());
    }

    [Fact]
    public void Linux_NoCudaDir_ReturnsNull()
    {
        var mock = new MockResolver { MockIsLinux = true };
        // Don't add /usr/local/cuda to ExistingDirs
        
        CudaEnvironment.Resolver = mock;
        Assert.Null(CudaEnvironment.GetCudaPath());
    }
    
    [Fact]
    public void UnknownOS_ReturnsNull()
    {
        var mock = new MockResolver { MockIsLinux = false, MockIsWindows = false };
        
        CudaEnvironment.Resolver = mock;
        Assert.Null(CudaEnvironment.GetCudaPath());
    }

    [Fact]
    public void Windows_BinPath_WithoutX64_ReturnsBin()
    {
        var mock = new MockResolver { MockIsWindows = true };
        mock.EnvVars["CUDA_PATH"] = @"C:\Cuda";
        mock.ExistingDirs.Add(@"C:\Cuda");
        mock.ExistingDirs.Add(@"C:\Cuda\bin"); // Note: no x64

        CudaEnvironment.Resolver = mock;
        Assert.Equal(@"C:\Cuda\bin", CudaEnvironment.GetCudaBinPath());
    }

    [Fact]
    public void BinPath_NoCudaPath_ReturnsNull()
    {
        var mock = new MockResolver { MockIsWindows = true };
        // Empty EnvVars, empty ExistingDirs
        CudaEnvironment.Resolver = mock;
        Assert.Null(CudaEnvironment.GetCudaBinPath());
    }

    [Fact]
    public void BinPath_NoBinFolder_ReturnsNull()
    {
        var mock = new MockResolver { MockIsWindows = true };
        mock.EnvVars["CUDA_PATH"] = @"C:\Cuda";
        mock.ExistingDirs.Add(@"C:\Cuda");
        // Don't add bin directory

        CudaEnvironment.Resolver = mock;
        Assert.Null(CudaEnvironment.GetCudaBinPath());
    }
}
