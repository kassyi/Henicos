using System;

namespace Kassyi.Math.GpuBigInt
{
    public class GpuContext : IDisposable
    {
        public int MaxWords { get; }
        private bool _disposed = false;

        public GpuContext(int maxWords)
        {
            MaxWords = maxWords;
            if (!NativeMethods.GpuBigInt_Init(maxWords))
            {
                throw new InvalidOperationException("Failed to initialize GPU context.");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                NativeMethods.GpuBigInt_Cleanup();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }

        ~GpuContext()
        {
            Dispose();
        }
    }
}
