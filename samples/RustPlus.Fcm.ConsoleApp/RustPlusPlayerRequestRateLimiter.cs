public sealed class RustPlusPlayerRequestRateLimiter
{
    private const double Capacity = 25;
    private const double TokensPerSecond = 3;
    private readonly SemaphoreSlim gate = new(1, 1);
    private double availableTokens = Capacity;
    private DateTimeOffset lastRefillAtUtc = DateTimeOffset.UtcNow;

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
                var now = DateTimeOffset.UtcNow;
                var elapsedSeconds = (now - lastRefillAtUtc).TotalSeconds;
                availableTokens = Math.Min(Capacity, availableTokens + (elapsedSeconds * TokensPerSecond));
                lastRefillAtUtc = now;

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
}