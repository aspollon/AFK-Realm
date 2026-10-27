using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace CoAInstaller.Web
{
    /// <summary>
    /// What happens goes to every open page at once, as server-sent events: the state of the server, the lines of a running
    /// task, the console output. A page that connects gets the current state first.
    /// </summary>
    static class Events
    {
        class Client { public Channel<string> Queue = Channel.CreateBounded<string>(new BoundedChannelOptions(4000) { FullMode = BoundedChannelFullMode.DropOldest }); }

        static readonly List<Client> clients = new List<Client>();
        static readonly object gate = new object();

        /// <summary>When the last page disconnected (or the program started); for ending the program when nobody looks.</summary>
        public static DateTime LastSeen = DateTime.UtcNow;
        public static int Count { get { lock (gate) return clients.Count; } }

        public static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        /// <summary>Sends an event to every page.</summary>
        public static void Send(string type, object data)
        {
            string text = "event: " + type + "\ndata: " + JsonSerializer.Serialize(data, Json).Replace("\n", "\\n") + "\n\n";
            Client[] all;
            lock (gate) all = clients.ToArray();
            foreach (var c in all) c.Queue.Writer.TryWrite(text);
        }

        /// <summary>The event stream of one page (GET /api/events).</summary>
        public static async Task Stream(HttpContext context, Func<IEnumerable<KeyValuePair<string, object>>> initial)
        {
            context.Response.Headers["Content-Type"] = "text/event-stream";
            context.Response.Headers["Cache-Control"] = "no-cache";
            context.Response.Headers["X-Accel-Buffering"] = "no";
            var client = new Client();
            lock (gate) clients.Add(client);
            LastSeen = DateTime.UtcNow;
            try
            {
                foreach (var kv in initial())
                    client.Queue.Writer.TryWrite("event: " + kv.Key + "\ndata: " + JsonSerializer.Serialize(kv.Value, Json).Replace("\n", "\\n") + "\n\n");
                var token = context.RequestAborted;
                // A comment now and then keeps proxies and the browser from closing a quiet stream.
                using (var timer = new Timer(_ => client.Queue.Writer.TryWrite(": ping\n\n"), null, 15000, 15000))
                {
                    while (!token.IsCancellationRequested)
                    {
                        string message = await client.Queue.Reader.ReadAsync(token);
                        await context.Response.WriteAsync(message, Encoding.UTF8, token);
                        await context.Response.Body.FlushAsync(token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
            finally
            {
                lock (gate) clients.Remove(client);
                LastSeen = DateTime.UtcNow;
            }
        }
    }
}
