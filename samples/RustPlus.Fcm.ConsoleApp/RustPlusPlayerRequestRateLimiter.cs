public sealed class RustPlusPlayerRequestRateLimiter
{
    private const double Capacity = 25;
    private const double TokensPerSecond = 3;
    private readonly SemaphoreSlim gate = new(1, 1);
    private double availableTokens = Capacity;
    private DateTimeOffset lastRefillAtUtc = DateTimeOffset.UtcNow;

    public async Task<(double AvailableTokens, double Capacity)> GetBudgetAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            RefillTokens(DateTimeOffset.UtcNow);
            return (availableTokens, Capacity);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AcquireAsync(double tokenCost, CancellationToken cancellationToken = default)
    {
        if (tokenCost <= 0 || tokenCost > Capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(tokenCost));
        }

        while (true)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                RefillTokens(DateTimeOffset.UtcNow);

                if (availableTokens >= tokenCost)
                {
                    availableTokens -= tokenCost;
                    return;
                }

                var waitTime = TimeSpan.FromSeconds((tokenCost - availableTokens) / TokensPerSecond);
                await Task.Delay(waitTime, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }
    }

    private void RefillTokens(DateTimeOffset now)
    {
        var elapsedSeconds = Math.Max(0, (now - lastRefillAtUtc).TotalSeconds);
        availableTokens = Math.Min(Capacity, availableTokens + (elapsedSeconds * TokensPerSecond));
        lastRefillAtUtc = now;
    }
}