using System.Collections.Concurrent;
using MattGPT.Contracts.Models;

namespace MattGPT.ApiService.Services;

/// <summary>
/// In-memory store for import job state. Registered as a singleton.
/// </summary>
public class ImportJobStore
{
    private readonly ConcurrentDictionary<string, ImportJob> _jobs = new();

    private volatile string? _latestEmbedJobId;

    public ImportJob CreateJob()
    {
        var job = new ImportJob();
        _jobs[job.JobId] = job;
        return job;
    }

    /// <summary>
    /// Creates a job for a standalone embedding run (no import phase). The import fields stay at
    /// their defaults and <see cref="ImportJob.Status"/> is marked complete; only the embedding
    /// phase is tracked. The job is recorded as the latest embed job so the UI can resume polling
    /// it after a page reload.
    /// </summary>
    public ImportJob CreateEmbedJob()
    {
        var job = new ImportJob { Status = ImportJobStatus.Complete };
        _jobs[job.JobId] = job;
        _latestEmbedJobId = job.JobId;
        return job;
    }

    public ImportJob? GetJob(string jobId) =>
        _jobs.TryGetValue(jobId, out var job) ? job : null;

    /// <summary>Returns the most recently started embed job, or <c>null</c> if none has run.</summary>
    public ImportJob? GetLatestEmbedJob() =>
        _latestEmbedJobId is { } id ? GetJob(id) : null;
}
