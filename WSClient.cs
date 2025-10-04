using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace DFClient.WebSocket
{
	public class ClientMessage
	{
		public string Id { get; set; }
		public string Type { get; set; } = "action";
		public string Action { get; set; }
		public object[] Args { get; set; }
	}

	public class ServerResponse
	{
		public string Id { get; set; }
		public string Type { get; set; }
		public string Status { get; set; }
		public object Result { get; set; }
		public string Error { get; set; }
	}

	public class WSClient : IDisposable
	{
		private ClientWebSocket _ws;
		private readonly ConcurrentDictionary<string, TaskCompletionSource<ServerResponse>> _pending = new();
		private CancellationTokenSource _receiveCts;
		private readonly object _lock = new(); // For synchronizing access to _ws and _receiveCts

		// Event for server-pushed messages (non-response)
		public event Action<string> OnEvent;

		/// <summary>Connects to the server WebSocket URL (e.g., ws://host:8080/ws) with token query param.</summary>
		public async Task ConnectAsync(string url, string token, string path = "/ws")
		{
			lock (_lock)
			{
				if (_ws != null)
				{
					throw new InvalidOperationException("WebSocket already connected. Call CloseAsync first.");
				}

				_ws = new ClientWebSocket();
				_receiveCts = new CancellationTokenSource();
			}

			var newUrl = ToWebSocketUrl(url, path);
			var uri = new Uri($"{newUrl}?token={token}");
			await _ws.ConnectAsync(uri, CancellationToken.None);

			// Start receive loop
			_ = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token));
		}

		/// <summary>Sends an action and awaits the response tied to the returned request id.</summary>
		public async Task<ServerResponse> CallAsync(string action, params object[] args)
		{
			lock (_lock)
			{
				if (_ws == null || _ws.State != WebSocketState.Open)
					throw new InvalidOperationException("WebSocket not connected.");
			}

			// Validate args for non-ping actions
			if (action != "ping" && (args == null || args.Length == 0 || args[0] is not string || string.IsNullOrEmpty((string)args[0])))
			{
				throw new ArgumentException("First argument must be a non-empty string (hash key) for non-ping actions.");
			}

			var id = Guid.NewGuid().ToString();
			var tcs = new TaskCompletionSource<ServerResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
			_pending[id] = tcs;

			var msg = new ClientMessage
			{
				Id = id,
				Type = "action",
				Action = action,
				Args = args
			};

			var json = JsonSerializer.Serialize(msg);
			var bytes = Encoding.UTF8.GetBytes(json);
			var segment = new ArraySegment<byte>(bytes);

			await _ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);

			// Add timeout (e.g., 30 seconds)
			using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
			try
			{
				return await tcs.Task.WaitAsync(timeoutCts.Token);
			}
			catch (OperationCanceledException)
			{
				_pending.TryRemove(id, out _);
				throw new TimeoutException($"No response received for request {id} within 30 seconds.");
			}
		}

		private async Task ReceiveLoopAsync(CancellationToken token)
		{
			var buffer = new byte[8192];

			try
			{
				while (!token.IsCancellationRequested && _ws != null && _ws.State == WebSocketState.Open)
				{
					using var ms = new MemoryStream();
					WebSocketReceiveResult result;
					do
					{
						var seg = new ArraySegment<byte>(buffer);
						result = await _ws.ReceiveAsync(seg, token);
						if (result.Count > 0)
							ms.Write(buffer, 0, result.Count);
					} while (!result.EndOfMessage);

					if (result.MessageType == WebSocketMessageType.Close)
					{
						await CloseAsync();
						break;
					}

					ms.Seek(0, SeekOrigin.Begin);
					var msg = Encoding.UTF8.GetString(ms.ToArray());

					// Try parse as ServerResponse
					ServerResponse resp = null;
					try
					{
						resp = JsonSerializer.Deserialize<ServerResponse>(msg);
					}
					catch (JsonException ex)
					{
						OnEvent?.Invoke(JsonSerializer.Serialize(new { type = "parse_error", message = $"Failed to parse server message: {ex.Message}" }));
						continue;
					}

					if (resp != null && !string.IsNullOrEmpty(resp.Id))
					{
						// Match pending request
						if (_pending.TryRemove(resp.Id, out var tcs))
						{
							tcs.TrySetResult(resp);
						}
						else
						{
							// Unmatched response treated as event
							OnEvent?.Invoke(msg);
						}
					}
					else
					{
						// Server event or malformed response
						OnEvent?.Invoke(msg);
					}
				}
			}
			catch (OperationCanceledException)
			{
				// Expected during shutdown
			}
			catch (Exception ex)
			{
				// Fail all pending requests
				lock (_lock)
				{
					foreach (var kv in _pending)
					{
						kv.Value.TrySetException(new Exception($"Connection error: {ex.Message}"));
					}
					_pending.Clear();
				}
				OnEvent?.Invoke(JsonSerializer.Serialize(new { type = "error", message = ex.Message }));
			}
		}

		/// <summary>Closes the WebSocket and cancels background receive loop.</summary>
		public async Task CloseAsync()
		{
			lock (_lock)
			{
				if (_receiveCts != null)
				{
					_receiveCts.Cancel();
					_receiveCts.Dispose();
					_receiveCts = null;
				}

				if (_ws != null)
				{
					try
					{
						if (_ws.State == WebSocketState.Open)
							_ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "client closing", CancellationToken.None).GetAwaiter().GetResult();
					}
					catch { /* ignore */ }

					_ws.Dispose();
					_ws = null;
				}

				// Fail pending requests
				foreach (var kv in _pending)
				{
					kv.Value.TrySetException(new Exception("Connection closed"));
				}
				_pending.Clear();
			}
		}

		public void Dispose()
		{
			lock (_lock)
			{
				CloseAsync().GetAwaiter().GetResult();
			}
		}

		public static string ToWebSocketUrl(string url, string path = "/ws")
		{
			if (string.IsNullOrWhiteSpace(url))
				throw new ArgumentException("URL cannot be null or empty", nameof(url));

			var uri = new Uri(url);

			// Map scheme
			string scheme = uri.Scheme switch
			{
				"http" => "ws",
				"https" => "wss",
				_ => throw new NotSupportedException($"Scheme '{uri.Scheme}' is not supported")
			};

			// Build WebSocket URL
			var builder = new UriBuilder(uri)
			{
				Scheme = scheme,
				Path = path
			};

			return builder.Uri.ToString();
		}
	}
}
