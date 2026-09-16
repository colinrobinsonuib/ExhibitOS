using System.Diagnostics;
using System.Net.Http;

namespace ExhibitOS.Core.Network;

public class HttpHealthProbes
{
    private readonly HttpClient _httpClient;

    public HttpHealthProbes(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public async Task<bool> WaitForReadinessAsync(
        string url,
        TimeSpan timeout,
        TimeSpan initialDelay,
        TimeSpan maxDelay,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var delay = initialDelay;

        while (stopwatch.Elapsed < timeout && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                // Any status code under 500 means server is responsive and accepting connections
                if ((int)response.StatusCode < 500)
                {
                    return true;
                }
            }
            catch (HttpRequestException)
            {
                // Server not accepting connections yet
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Request timeout, retry
            }

            var remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var currentWait = delay < remaining ? delay : remaining;
            await Task.Delay(currentWait, cancellationToken);

            // Exponential backoff
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 1.5, maxDelay.TotalMilliseconds));
        }

        return false;
    }

    public async Task<bool> CheckLivenessAsync(string url, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            return (int)response.StatusCode < 500;
        }
        catch
        {
            return false;
        }
    }
}
