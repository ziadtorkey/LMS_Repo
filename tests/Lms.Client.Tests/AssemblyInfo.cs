using Xunit;

// Tests set process-wide default cultures (see ClientTestContext), so they must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
