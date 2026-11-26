namespace Crawler.Core.Domain;

public enum JobStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Canceled
}

public static class JobStatusExtensions
{
    public static bool IsTerminal(this JobStatus status) =>
        status is JobStatus.Completed or JobStatus.Failed or JobStatus.Canceled;
}
