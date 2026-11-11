using Hangfire;
using Lms.Infrastructure.Imports;
using Lms.Infrastructure.Jobs;

namespace Lms.UnitTests.Imports;

public sealed class ImportQueueTests
{
    [Theory]
    [InlineData(typeof(ValidateImportJob))]
    [InlineData(typeof(CommitImportJob))]
    public void Import_jobs_run_on_the_imports_queue(Type job)
    {
        var queue = job.GetMethod("RunAsync")!.GetCustomAttributes(typeof(QueueAttribute), true).Cast<QueueAttribute>().Single();

        Assert.Equal(JobQueues.Imports, queue.Queue);
    }

    [Fact]
    public void The_server_listens_to_every_queue_a_job_uses()
    {
        Assert.Contains(JobQueues.Default, JobQueues.All);
        Assert.Contains(JobQueues.Imports, JobQueues.All);
    }
}
