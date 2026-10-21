
// Important note on CUDA Stream Capture (CUDA Graph: GBI-31) and xUnit parallel test execution:
//
// While a stream capture is actively running via cuStreamBeginCapture (e.g., in GBI_31_CudaGraphExecutionTests),
// any operation targeting the default stream (Stream 0) or any synchronous CUDA driver API that enforces
// device-wide implicit synchronization (such as cuMemAlloc / cuMemFree) invoked on another thread within the same
// CUDA context violates CUDA capture constraints.
//
// In such cases, the interfering call fails with CUDA_ERROR_STREAM_CAPTURE_IMPLICIT, and the active stream capture
// is immediately invalidated with CUDA_ERROR_STREAM_CAPTURE_INVALIDATED.
//
// When xUnit executes test classes in parallel, other tests using auto-generated kernel wrappers or host array overloads
// may perform implicit allocations or default stream copies concurrently, resulting in flaky test failures.
// Therefore, test parallelization is disabled for this test assembly to ensure deterministic and safe execution.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
