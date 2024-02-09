using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Reflection.Metadata.Ecma335;
using System.Text;

namespace ShowdownSharp
{
    public class Client
    {
        private string url;
        private string websocketUrl => $"ws://{url}:{port}/showdown/websocket";
        private int port;
        private ClientWebSocket client;
        public TaskCompletionSource<bool> isReady;
        public ClientData clientData;
        private BlockingCollection<string> messageQueue;
        private BlockingCollection<string> messageInbox;

#if DEBUG
        public Client(string url = "localhost", int port = 8000)
        {
            this.url = url;
            this.port = port;
            client = new ClientWebSocket();

            if (clientData == null)
            {
                clientData = new ClientData();
                clientData.challstr = "";
                string[] accountData = File.ReadAllText(System.IO.Directory.GetCurrentDirectory() + "\\..\\..\\..\\account.txt").Split("|");
                clientData.username = accountData[0];
                clientData.password = accountData[1];
                clientData.chatrooms = new Dictionary<string, Chat>();
                clientData.battles = new Dictionary<string, Battle>();
            }

            messageQueue = new BlockingCollection<string>();
            messageInbox = new BlockingCollection<string>();
            isReady = new TaskCompletionSource<bool>();
        }
#endif

        public Client(string username, string password, string url = "localhost", int port = 8000)
        {
            this.url = url;
            this.port = port;
            client = new ClientWebSocket();

            if (clientData == null)
            {
                clientData = new ClientData();
                clientData.challstr = "";
                clientData.username = username;
                clientData.password = password;
                clientData.chatrooms = new Dictionary<string, Chat>();
                clientData.battles = new Dictionary<string, Battle>();
            }

            messageQueue = new BlockingCollection<string>();
            messageInbox = new BlockingCollection<string>();
            isReady = new TaskCompletionSource<bool>();
        }

        ~Client()
        {
            Close();
        }

        private async Task Initialize()
        {
            await client.ConnectAsync(new Uri(websocketUrl), CancellationToken.None);
        }

        private async Task Close()
        {
            if (client != null)
            {
                await client.CloseAsync(WebSocketCloseStatus.Empty, "", CancellationToken.None);
                client.Dispose();
            }
        }

        public async Task Run()
        {
            try
            {
                await Initialize();

                var sendTask = Task.Run(async () => await SendMessagesAsync(client));
                var receiveTask = Task.Run(async () => await ReceiveMessagesAsync(client));
                var inputTask = Task.Run(async () => await ParseInput());
                var responseTask = Task.Run(async () => await ParseResponses());

                // Wait for either task to complete
                await Task.WhenAny(sendTask, receiveTask, inputTask, responseTask);
#if DEBUG
                Console.WriteLine($"{sendTask.IsCompleted} {receiveTask.IsCompleted} {inputTask.IsCompleted} {responseTask.IsCompleted}");
#endif

                await Close();
            }
            catch (Exception ex) { Console.WriteLine(ex.ToString()); } 
        }

        private async Task ParseInput()
        {
            while (client.State == WebSocketState.Open)
            {
                string input = Console.ReadLine();
                if (input == "exit")
                {
                    break;
                }
#if DEBUG
                messageQueue.Add(input);
#endif
            }
        }

        private async Task ParseResponses()
        {
            while (client.State == WebSocketState.Open) // TODO write better parsing
            {
                string response = messageInbox.Take(); // this auto blocks when there's nothing
                string[] responseLines = response.Split('\n'); // get it line by line
                string roomId = "lobby"; // default roomId since it doesn't specify the room when it's lobby
                foreach (string line in responseLines)
                {
                    if (string.IsNullOrEmpty(line)) continue; // skip empty lines and don't mess with it
                    if (line.StartsWith(">"))
                    {
                        roomId = line.Substring(1);
                        continue;
                    }
                    if (line.StartsWith("|") && !line.StartsWith("||"))
                    {
                        string[] parts = line.Split("|"); // split the message
                        if (parts[1] == "init") // just skip the entire response if it's room init
                        {
                            break;
                        }
                        if (parts[1] == "c:") // in case of a chat message
                        {
                            ChatMessageData data = new ChatMessageData(ChatMessageType.UserSendMessage, parts[3], string.Join("|", parts, 4, parts.Length - 4), long.Parse(parts[2]));
                            Task.Run(() => clientData.chatrooms[roomId].HandleMessage(data));
                        }
                        if (parts[1] == "challstr") // handle challstr
                        {
                            clientData.challstr = string.Join("|", parts, 2, parts.Length - 2); // save challstr
                            string assertion = (string)JObject.Parse((await GetLogin()).Substring(1))["assertion"];
                            messageQueue.Add($"|/trn {clientData.username},0,{assertion}");
                            continue;
                        }
                        if (parts[1] == "updatesearch") // we'll get it when we're logged in
                        {
                            // TODO parse update search
                            isReady.TrySetResult(true);
                            continue;
                        }
#if DEBUG
                        Console.WriteLine(response);
#endif
                        continue;
                    }
#if DEBUG
                    Console.WriteLine(line);
#endif
                    continue;
                }
            }
        }


        // just for the http login
        private async Task<string> GetLogin()
        {
            string loginUrl = "https://play.pokemonshowdown.com/api/login";
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    // Define the data you want to send in the request body (as a string)
                    string requestBody = $"name={clientData.username}&pass={clientData.password}&challstr={clientData.challstr}";

                    // Convert the string to a ByteArrayContent
                    var content = new StringContent(requestBody, Encoding.UTF8, "application/x-www-form-urlencoded");

                    // Make the POST request over HTTPS
                    HttpResponseMessage response = await client.PostAsync(loginUrl, content);

                    // Check if the request was successful (status code in the 2xx range)
                    if (response.IsSuccessStatusCode)
                    {
                        string responseData = await response.Content.ReadAsStringAsync();
                        return responseData;
                    }
                    else
                    {
                        Console.WriteLine($"POST request for login failed. Status Code: {response.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            return null;
        }

        // to allow the chats and battles to do stuff
        internal void AddMessage(string message)
        {
            messageQueue.Add(message);
        }

        // to make a new chatroom
        public Chat JoinRoom(string name)
        {
            Chat chat = new Chat(this, name);
            clientData.chatrooms.Add(name, chat);
            messageQueue.Add($"|/join {name}");
            return chat;
        }

        // higher-level classes to do this stuff
        private async Task SendMessagesAsync(ClientWebSocket client)
        {
            while (client.State == WebSocketState.Open)
            {
                string message = messageQueue.Take();// this auto blocks when there's nothing

                // Send the user's message to the server
                await SendMessageAsync(client, message);

            }
        }
        private async Task ReceiveMessagesAsync(ClientWebSocket client)
        {
            while (client.State == WebSocketState.Open)
            {
                // Receive messages from the server
                string receivedMessage = await ReceiveMessageAsync(client);

                messageInbox.Add(receivedMessage);
            }
        }

        // lower-level classes to do this stuff
        private static async Task SendMessageAsync(ClientWebSocket client, string message)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            await client.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        private static async Task<string> ReceiveMessageAsync(ClientWebSocket client)
        {
            var buffer = new byte[16182]; // waaay too big but idk computers have memory now
            var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            return Encoding.UTF8.GetString(buffer, 0, result.Count);
        }
    }

    public class ClientData
    {
        internal string challstr;
        internal string username;
        public string Username => username;
        internal string password;
        internal Dictionary<string, Battle> battles;
        internal Dictionary<string, Chat> chatrooms;
    } // do this later
    public class Chat
    {
        private string roomId;
        public string RoomId => roomId;
        private Client client;
        public Client Client => client;
        public delegate void ChatMessageHandler(object sender, ChatMessageData data);
        public event ChatMessageHandler OnMessageReceive;
        public void HandleMessage(ChatMessageData data)
        {
            OnMessageReceive?.Invoke(this, data);
            return;
        }
        internal Chat(Client client, string roomId)
        {
            this.client = client;
            this.roomId = roomId;
        }
        public void SendMessage(string message)
        {
            client.AddMessage($"{roomId}|{message}");
        }
    }
    public enum ChatMessageType { None, UserJoin, UserLeave, UserChangeName, UserSendMessage, BattleStart}
    public class ChatMessageData
    {
        public ChatMessageType type;
        public string? user;
        public string? message;
        public long timestamp;
        public ChatMessageData(ChatMessageType type = ChatMessageType.None, string? user = null, string? message = null, long timestamp = 0)
        {
            this.type = type;
            this.user = user;
            this.message = message;
            this.timestamp = timestamp;
        }
    }
    public class Battle : Chat
    {
        public Battle(Client client, string roomId) : base(client, roomId)
        {

        }
    }
    public class Trainer
    {

    }
    public class Pokemon
    {

    }
}