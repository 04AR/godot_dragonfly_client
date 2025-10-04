using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using DFClient.WebSocket;

namespace DFClient.Helpers
{
	public class Helper
	{
		private readonly WSClient _ws;

		public Helper(WSClient ws) => _ws = ws;

		// Generic
		public Task<ServerResponse> CallAsync(string action, params object[] args)
			=> _ws.CallAsync(action, args);

		// Helpers
		public Task<ServerResponse> CreateLobbyAsync(string name, string ownerId)
			=> CallAsync("create_lobby", name, ownerId);

		public Task<ServerResponse> JoinLobbyAsync(string name, string userId)
			=> CallAsync("join_lobby", name, userId);

		public Task<ServerResponse> GetLobbyAsync(string name)
			=> CallAsync("get_lobby", name);

		public Task<ServerResponse> PlayerSpawnAsync(string userId, string lobby, object state)
			=> CallAsync("player_spawn", userId, lobby, JsonSerializer.Serialize(state));

		public Task<ServerResponse> ReplicateStateAsync(string lobby, string userId, object state)
			=> CallAsync("replicate_state", lobby, userId, JsonSerializer.Serialize(state));

		public Task<ServerResponse> SyncStateAsync(string lobby)
			=> CallAsync("sync_state", lobby);
	}
}
