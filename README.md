# RimBridgeServer

RimBridgeServer lets you control RimWorld from outside the game. This is useful for testing mods automatically or building tools that work with RimWorld.

## What does it do?

This mod creates a connection point (called a "server") inside RimWorld. Other programs can connect to this server to:

- Check if the game is running
- Pause or unpause the game  
- Get information about the current game
- Control the game remotely

This is especially helpful for:
- **Mod developers** who want to test their mods automatically
- **AI tools** that need to interact with RimWorld
- **External programs** that want to read game data

## Features

- **Easy to use**: Works automatically when you start RimWorld
- **Safe**: Only accepts connections from your own computer
- **Simple**: Uses a standard protocol that many tools understand
- **Compatible**: Works with RimWorld 1.6

## How to get started

### Installation

1. Download the mod and put it in your RimWorld Mods folder
2. Enable the mod in RimWorld
3. Start the game

That's it! The server starts automatically when RimWorld loads.

### How to connect

When RimBridgeServer starts, it will show a message in the RimWorld log that looks like this:

```
[RimBridge] Server running on port 5174
[RimBridge] Connection token: abc123...
```

Your external program needs:
- **Port number**: Usually 5174 (but check the log to be sure)
- **Token**: The random text shown in the log (for security)
- **Address**: Always 127.0.0.1 (your own computer only)

### Working with GABS

If you use [GABS](https://github.com/pardeike/GABS) (an AI gaming environment), RimBridgeServer will automatically configure itself. No extra setup needed!

## Available commands

Your external program can send these commands to RimBridgeServer:

### Basic commands
- **ping** - Test if the connection is working (responds with "pong")

### Game control
- **get_game_info** - Get information about the current game
- **pause_game** - Pause or unpause the game

More commands may be added in future versions.

## For developers

### How it works

RimBridgeServer uses a communication protocol called GABP (Game Agent Bridge Protocol). This is a standard way for programs to talk to games.

The basic steps are:
1. Connect to the server using TCP (a network connection type)
2. Say "hello" with your security token
3. Ask for a list of available commands
4. Send commands and receive responses
5. Optionally, listen for events from the game

For complete details about the protocol, see the [GABP specification](https://github.com/pardeike/GABS).

### Building the mod

Requirements:
- .NET SDK
- RimWorld installed

Steps:
1. Clone this repository
2. Run `dotnet build` in the main folder
3. The built mod will be in the `1.6/Assemblies/` folder

**Tip**: Set the `RIMWORLD_MOD_DIR` environment variable to automatically copy the built mod to your RimWorld Mods folder.

### Project structure

```
About/          - Mod information for RimWorld
1.6/Assemblies/ - Built mod files
Source/         - Source code
lib/            - External libraries
.vscode/        - Visual Studio Code settings
```

## License

This project uses the MIT License. See the `LICENSE` file for details.

## Dependencies

This mod includes [Lib.GAB](https://github.com/pardeike/Lib.GAB), which provides the GABP protocol implementation.
