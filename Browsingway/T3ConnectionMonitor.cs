using System.Net.Http;

namespace Browsingway;

internal sealed record T3ConnectionStatus(string Url, bool? IsReachable, string? Error)
{
	public static T3ConnectionStatus Checking(string url) => new(url, null, null);
}

internal sealed class T3ConnectionMonitor : IDisposable
{
	private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(2) };
	private readonly CancellationTokenSource _cancellation = new();
	private readonly Timer _timer;
	private T3ConnectionStatus _status;
	private int _checking;

	public T3ConnectionMonitor(string url)
	{
		_status = T3ConnectionStatus.Checking(url);
		_timer = new Timer(_ => _ = Check(), null, TimeSpan.Zero, TimeSpan.FromSeconds(10));
	}

	public T3ConnectionStatus Status => Volatile.Read(ref _status);

	public void SetUrl(string url)
	{
		Volatile.Write(ref _status, T3ConnectionStatus.Checking(url));
		_ = Check();
	}

	public void Retry() => _ = Check();

	public void Dispose()
	{
		_timer.Dispose();
		_cancellation.Cancel();
		_client.Dispose();
	}

	private async Task Check()
	{
		if (Interlocked.Exchange(ref _checking, 1) != 0) return;

		string url = Status.Url;
		try
		{
			if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
			{
				Volatile.Write(ref _status, new T3ConnectionStatus(url, false, "The URL must use HTTP or HTTPS."));
				return;
			}

			using HttpRequestMessage request = new(HttpMethod.Get, uri);
			using HttpResponseMessage response = await _client.SendAsync(
				request,
				HttpCompletionOption.ResponseHeadersRead,
				_cancellation.Token);
			if (Status.Url == url)
				Volatile.Write(ref _status, new T3ConnectionStatus(url, true, null));
		}
		catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or ObjectDisposedException)
		{
			if (!_cancellation.IsCancellationRequested && Status.Url == url)
				Volatile.Write(ref _status, new T3ConnectionStatus(url, false, exception.Message));
		}
		finally
		{
			Interlocked.Exchange(ref _checking, 0);
		}
	}
}
