namespace PaxPanel;

/// <summary>Runs produce→publish on a background thread every period. Slow ticks are skipped, not queued.</summary>
public sealed class SensorLoop : IDisposable
{
    readonly CancellationTokenSource _cts = new();

    public Task Completion { get; }

    public SensorLoop(Func<DateTime, string> produce, Action<string> publish, TimeSpan period)
    {
        Completion = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(period);
            try
            {
                do
                {
                    try
                    {
                        publish(produce(DateTime.Now));
                    }
                    catch (Exception e)
                    {
                        Log.Error("Update loop", e);
                    }
                } while (await timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        });
    }

    public void Dispose() => _cts.Cancel();
}
