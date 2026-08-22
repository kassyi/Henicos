using System;
using System.Diagnostics;
using System.IO.Abstractions.TestingHelpers;
using Xunit;
using Kassyi.GSharp;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Transpiler.Pipeline;

namespace Kassyi.GSharp.Tests
{
    public class JitCompilerTests
    {
        [Fact]
        public void CompileAndLoad_ShouldCleanUpFiles_ExceptCachedDll_OnSuccess()
        {
            var mockFs = new MockFileSystem();
            mockFs.AddDirectory(mockFs.Path.GetTempPath());
            
            // Mock environment setup to avoid real execution
            mockFs.AddFile(@"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe", new MockFileData(""));
            mockFs.AddFile(@"C:\VC\Auxiliary\Build\vcvars64.bat", new MockFileData(""));
            mockFs.AddFile(@"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.3\bin\nvcc.exe", new MockFileData(""));

            NvccCompiler.FileSystem = mockFs;
            JitLoader.FileSystem = mockFs;
            BackendCompiler.FileSystem = mockFs;
            
            Environment.SetEnvironmentVariable("ProgramFiles(x86)", @"C:\Program Files (x86)");
            
            NvccCompiler.GetVswhereOutputFunc = () => @"C:\VS";
            mockFs.AddFile(@"C:\VS\VC\Auxiliary\Build\vcvars64.bat", new MockFileData(""));
            
            // Mock process runner that fakes vswhere and nvcc
            NvccCompiler.ProcessRunner = psi =>
            {
                if (psi.FileName.Contains("vswhere.exe"))
                {
                    // This is just a fake, we intercepted vswhere manually in GetVcvarsPath
                }
                else if (psi.FileName == "cmd.exe")
                {
                    // Mock nvcc compilation: just create the dll file
                    var files = mockFs.Directory.GetFiles(mockFs.Path.Combine(mockFs.Path.GetTempPath(), "KassyiGSharpJit", "Cache"));
                    foreach(var f in files)
                    {
                        if (f.EndsWith(".txt"))
                        {
                            var content = mockFs.File.ReadAllText(f);
                            int oIndex = content.IndexOf("-o\r\n") + 4;
                            if (oIndex < 4) oIndex = content.IndexOf("-o\n") + 3;
                            int endIdx = content.IndexOf("\n", oIndex);
                            string dllPath = content.Substring(oIndex, endIdx - oIndex).Trim();
                            mockFs.AddFile(dllPath, new MockFileData("dummy dll content"));
                        }
                    }
                }
            };
            
            // Mock JitLoader
            JitLoader.LoadLibraryFunc = path => new IntPtr(1234);
            JitLoader.GetExportFunc = (h, name) => new IntPtr(5678);
            JitLoader.FreeLibraryFunc = h => { };

            var source = new TranspiledSource(
                Metadata: new GpuKernelMetadata(null!, "", "", "test_func"),
                CudaCode: "__global__ void test() {}",
                SourceHash: "dummyhash123"
            );

            var binary = BackendCompiler.Compile(source);
            using (var kernel = JitLoader.Load<Action>(binary))
            {
                Assert.NotNull(kernel);
                
                // .cu file should be deleted even on success
                var files = mockFs.Directory.GetFiles(mockFs.Path.Combine(mockFs.Path.GetTempPath(), "KassyiGSharpJit", "Cache"));
                Assert.DoesNotContain(files, f => f.EndsWith(".cu"));
                
                // options .txt and .bat should be deleted
                Assert.DoesNotContain(files, f => f.EndsWith(".txt"));
                Assert.DoesNotContain(files, f => f.EndsWith(".bat"));
                
                // .dll should still exist while loaded
                Assert.Contains(files, f => f.EndsWith(".dll"));
            }
            
            // After dispose, DLL should STILL be cached!
            var afterFiles = mockFs.Directory.GetFiles(mockFs.Path.Combine(mockFs.Path.GetTempPath(), "KassyiGSharpJit", "Cache"));
            Assert.Contains(afterFiles, f => f.EndsWith(".dll"));
        }

        [Fact]
        public void CompileAndLoad_ShouldCleanUpFiles_OnCompilationFailure()
        {
            var mockFs = new MockFileSystem();
            mockFs.AddDirectory(mockFs.Path.GetTempPath());
            
            mockFs.AddFile(@"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe", new MockFileData(""));
            mockFs.AddFile(@"C:\VC\Auxiliary\Build\vcvars64.bat", new MockFileData(""));
            mockFs.AddFile(@"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.3\bin\nvcc.exe", new MockFileData(""));

            NvccCompiler.FileSystem = mockFs;
            JitLoader.FileSystem = mockFs;
            BackendCompiler.FileSystem = mockFs;
            
            Environment.SetEnvironmentVariable("ProgramFiles(x86)", @"C:\Program Files (x86)");
            
            NvccCompiler.GetVswhereOutputFunc = () => @"C:\VS";
            mockFs.AddFile(@"C:\VS\VC\Auxiliary\Build\vcvars64.bat", new MockFileData(""));
            
            // Mock process runner that fakes compilation failure (does not create DLL)
            NvccCompiler.ProcessRunner = psi =>
            {
                if (psi.FileName == "cmd.exe")
                {
                    throw new Exception("Process failed"); // Simulate compilation error
                }
            };
            
            var source = new TranspiledSource(
                Metadata: new GpuKernelMetadata(null!, "", "", "test_func"),
                CudaCode: "__global__ void test() {}",
                SourceHash: "dummyhash123"
            );

            Assert.Throws<Exception>((Action)(() => 
                BackendCompiler.Compile(source)
            ));
            
            // Verify cleanup on failure
            var files = mockFs.Directory.GetFiles(mockFs.Path.Combine(mockFs.Path.GetTempPath(), "KassyiGSharpJit", "Cache"));
            Assert.DoesNotContain(files, f => f.EndsWith(".cu"));
            Assert.DoesNotContain(files, f => f.EndsWith(".txt"));
            Assert.DoesNotContain(files, f => f.EndsWith(".bat"));
            Assert.DoesNotContain(files, f => f.EndsWith(".dll"));
        }
    }
}
