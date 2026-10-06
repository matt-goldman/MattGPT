using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Services;

/// <summary>
/// Wakes <see cref="ChatSessionLifecycleService"/> early when sessions have just been completed
/// (e.g. the user started a new chat), so they are embedded promptly rather than at the next
/// scheduled sweep. Notifications coalesce: any number before a sweep cause one sweep.
/// </summary>
public sealed class ChatSessionMemorySignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    /// <summary>Requests a sweep as soon as possible.</summary>
    public void Notify() => _channel.Writer.TryWrite(true);

    /// <summary>Waits until <see cref="Notify"/> is called or <paramref name="timeout"/> elapses.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await _channel.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timed out: time for the scheduled sweep.
        }
    }
}

/// <summary>
/// Background sweep for the chat session lifecycle (ADR-013): completes sessions idle past
/// <see cref="ChatSessionOptions.IdleTimeout"/> and stores pending sessions in memory, every
/// <see cref="ChatSessionOptions.SweepInterval"/> or sooner when signalled. The idle timeout is the
/// safety net for sessions the user simply leaves, since navigating away isn't reliably detectable
/// in Blazor Server. The first sweep runs at startup, which backfills sessions that predate this.
/// </summary>
public class ChatSessionLifecycleService(
    ChatSessionMemorySignal signal,
    IServiceProvider serviceProvider,
    IOptions<ChatSessionOptions> options,
    ILogger<ChatSessionLifecycleService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // ChatSessionMemoryService is scoped (scoped repositories, LLM and embedding services).
                using var scope = serviceProvider.CreateScope();
                var memory = scope.ServiceProvider.GetRequiredService<ChatSessionMemoryService>();
                await memory.SweepAsync(retryErrors: false, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Typically the database isn't reachable yet at startup; the next sweep retries.
                logger.LogError(ex, "Chat session memory sweep failed.");
            }

            await signal.WaitAsync(options.Value.SweepInterval, stoppingToken);
        }
    }
}
