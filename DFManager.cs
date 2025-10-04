using Godot;
using System;
using System.Threading.Tasks;
using DFClient.WebSocket;
using DFClient.Auth;
using DFClient.Helpers;

public partial class DFManager : Node
{
	[Export]
	public string Addr = "http://localhost:8080";
	private AuthClient _auth;
	private WSClient _ws;
	private Helper _helper;

	public string Token { get; private set; }

	public override void _Ready()
	{
		_auth = new AuthClient(Addr); // your server
		_ws = new WSClient();
		_helper = new Helper(_ws);
	}

	public async Task<bool> ConnectGuest()
	{
		try
		{
			// Guest login
			Token = await _auth.GuestAsync();
			GD.Print("Logged in as guest, token: " + Token);

			await _ws.ConnectAsync(Addr, Token, "/ws");
			return true;
		}
		catch (Exception e)
		{
			GD.PrintErr("Auth/connect failed: " + e.Message);
			return false;
		}
	}

	public async Task<bool> RegisterAndConnect(string username, string password)
	{
		try
		{
			Token = await _auth.RegisterAsync(username, password);
			if (string.IsNullOrEmpty(Token))
			{
				Token = await _auth.LoginAsync(username, password);
			}

			if (string.IsNullOrEmpty(Token))
			{
				GD.PrintErr("Failed to obtain token");
				return false;
			}

			await _ws.ConnectAsync(Addr, Token, "/ws");
			return true;
		}
		catch (Exception e)
		{
			GD.PrintErr("Auth/connect failed: " + e.Message);
			return false;
		}
	}

	public async Task<ServerResponse> CreateLobby(string name, string owner)
		=> await _helper.CreateLobbyAsync(name, owner);

	public async Task<ServerResponse> JoinLobby(string name, string user)
		=> await _helper.JoinLobbyAsync(name, user);

	public async Task<ServerResponse> SpawnPlayer(string user, string lobby, Godot.Vector2 pos)
	{
		var state = new { x = pos.X, y = pos.Y, hp = 100 };
		return await _helper.PlayerSpawnAsync(user, lobby, state);
	}

	public async Task<ServerResponse> SyncState(string lobby)
		=> await _helper.SyncStateAsync(lobby);

	// Generic extender
	public async Task<ServerResponse> Call(string action, params object[] args)
		=> await _helper.CallAsync(action, args);
}
