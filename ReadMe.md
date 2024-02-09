# ShowdownSharp
ShowdownSharp is a C# library for communicating using the Pokemon Showdown Protocol.

### Features
 * Creation of clients
 * Automatic login
 * Joining chatrooms
 * Reacting when a message is said


### Installation
Clone the repository somewhere on your computer
```sh
git clone https://github.com/Octoham/ShowdownSharp.git
```
Go into the directory
```sh
cd ShowdownSharp
```
Build with the Release configuration
```sh
dotnet build -c Release
```
Then, simply add the .dll file as a dependency to any C# project

### Usage
Creating a client is simple. Here is an example for a fully functioning client that only takes 23 lines of code
```cs
using ShowdownSharp;

public class Program
{
    public static async Task Main()
    {
    Client client = new Client("username", "password"); // This will initialize a client for a locally hosted server by default
	var task = Task.Run(() => client.Run()); // This starts the client
	await client.isReady.Task; // This waits for the client to be ready

	// EXAMPLE
	Chat lobbyRoom = client.JoinRoom("lobby"); // This makes the client join a room
	lobbyRoom.OnMessageReceive += HandleMessages; // This runs the HandleMessage function everytime the client receives a message in the room

	await task; // This simply waits for the client to finish before exiting
	}

	public static void HandleMessages(object sender, ChatMessageData data)
	{
		// EXAMPLE
		Console.WriteLine($"{data.user} just said {data.message}");
	}
}
```