using System.Collections.Concurrent;
using MattGPT.ApiService.Models;

namespace MattGPT.ApiService.Services;

/// <summary>
/// In-memory store for import job state. Registered as a singleton.
/// </summary>
public class ImportJobStore
{
    private readonly ConcurrentDictionary<string, ImportJob> _jobs = new();

    public ImportJob CreateJob()
    {
        var job = new ImportJob();
        _jobs[job.JobId] = job;
        return job;
    }

    public ImportJob? GetJob(string jobId) =>
        _jobs.TryGetValue(jobId, out var job) ? job : null;
}
