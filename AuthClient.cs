using System.Net.Http.Json;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace DFClient.Auth
{
	public class AuthClient
	{
		private readonly HttpClient _http;

		public AuthClient(string baseUrl)
		{
			_http = new HttpClient { BaseAddress = new Uri(baseUrl) };
		}

		public async Task<string> RegisterAsync(string username, string password)
		{
			var resp = await _http.PostAsJsonAsync("/register", new { username, password });
			resp.EnsureSuccessStatusCode();
			var json = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
			return json["token"]; // JWT
		}

		public async Task<string> LoginAsync(string username, string password)
		{
			var resp = await _http.PostAsJsonAsync("/login", new { username, password });
			resp.EnsureSuccessStatusCode();
			var json = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
			return json["token"];
		}

		public async Task<string> GuestAsync()
		{
			var resp = await _http.PostAsync("/guest", null); // no body
			resp.EnsureSuccessStatusCode();
			var json = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
			return json["token"];
		}
	}
}
