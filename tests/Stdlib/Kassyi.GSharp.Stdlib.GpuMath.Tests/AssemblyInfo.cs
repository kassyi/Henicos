
// Important note on CUDA Stream Capture (CUDA Graph: GBI-31) and xUnit parallel test execution:
//
// BigNum captures through CapturedStreamGraph run in relaxed mode on a dedicated non-blocking stream
// (CU_STREAM_NON_BLOCKING), so they do not conflict with concurrent legacy default stream (Stream 0) work.
// However, tests that capture a workspace stream directly (for example CudaGraphExecutionTests) still record on the
// blocking workspace stream (CU_STREAM_DEFAULT), which fails with CUDA_ERROR_STREAM_CAPTURE_IMPLICIT and invalidates
// the capture (CUDA_ERROR_STREAM_CAPTURE_INVALIDATED) when another thread uses Stream 0, and tests that capture in a
// stricter mode also reject unsafe calls made during it.
//
// When xUnit executes test classes in parallel, other tests using auto-generated kernel wrappers or host array overloads
// may copy through the default stream concurrently, resulting in flaky test failures.
// Therefore, test parallelization is disabled for this test assembly to ensure deterministic and safe execution.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
