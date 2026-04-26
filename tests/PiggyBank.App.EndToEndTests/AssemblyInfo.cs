// xUnit parallelises test CLASSES by default. E2E tests each launch a
// real WPF app process and drive it via UI Automation — running them
// concurrently means two apps fight for keyboard focus, produce
// non-deterministic failures, and can visibly show two windows on
// screen simultaneously. Force sequential execution at the assembly
// level so every E2E test has exclusive input.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true, MaxParallelThreads = 1)]
