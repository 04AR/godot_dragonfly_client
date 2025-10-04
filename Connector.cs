using Godot;
using System;
using System.Text.Json; // For JsonSerializer

public partial class Connector : Node
{
	[Export]
	public DFManager manager;

	// Called when the node enters the scene tree for the first time.
	public override async void _Ready()
	{
		// manager.RegisterAndConnect("username", "password");
		bool connected = await manager.ConnectGuest();
		if (!connected)
		{
			GD.PrintErr("Failed to connect as guest");
			return;
		}

		GD.Print("Connected as guest!");
		
		try
		{
			GD.Print("executing Lua script");
			// Example: add a user to a hash via Lua
			var resp = await manager.Call("addData", "myhash", "add", "user:123", "Anjal");
			
			var respJson = JsonSerializer.Serialize(resp);
			GD.Print("Response: " + respJson);

			if (resp.Status == "ok")
				GD.Print("Lua script executed successfully: ", resp.Result);
			else
				GD.PrintErr("Lua script error: ", resp.Error);
		}
		catch (Exception e)
		{
			GD.PrintErr("CallLuaScript failed: ", e.Message);
		}
		GD.Print("completed Ready");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
