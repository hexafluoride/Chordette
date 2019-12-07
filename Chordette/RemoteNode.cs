using NLog;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Chordette
{
    public delegate void RemoteNodeMessageHandler(object sender, RemoteNodeMessageEventArgs e);
    public delegate void OnRemoteNodeDisconnect(object sender, RemoteNodeDisconnectingEventArgs e);

    public class RemoteNodeDisconnectingEventArgs : EventArgs
    {
        public bool LongTerm { get; set; }
        public bool OurRequest { get; set; }

        public RemoteNodeDisconnectingEventArgs(bool our_request, bool long_term)
        {
            LongTerm = long_term;
            OurRequest = our_request;
        }
    }

    public class RemoteNodeMessageEventArgs : EventArgs
    {
        public string Method { get; set; }
        public byte[] RequestID { get; set; }
        public byte[] Node { get; set; }
        public byte[] Parameter { get; set; }

        public RemoteNodeMessageEventArgs(string method, byte[] source, byte[] request_id, byte[] parameter)
        {
            Method = method;
            RequestID = request_id;
            Node = source;
            Parameter = parameter;
        }
    }

    public class RemoteNode : INode
    {
        public int KeySize => SelfNode?.KeySize ?? 0;
        public static Random Random = new Random();

        public byte[] Successor { get => RequestCached("get_successor"); }
        public byte[] Predecessor { get => RequestCached("get_predecessor"); }
        public byte[] ID { get; private set; }
        public INode SelfNode { get; set; }

        public static long SentBytes { get; set; }
        public static long SentMessages { get; set; }
        public static long ReceivedMessages { get; set; }

        public DateTime ConnectionTime { get; set; }
        public DateTime LastMessage { get; set; }

        public Socket Connection { get; set; }

        private ConcurrentDictionary<byte[], string> Methods = new ConcurrentDictionary<byte[], string>(new StructuralEqualityComparer()); // used for debugging purposes only
        private ConcurrentDictionary<byte[], ManualResetEvent> Waiters = new ConcurrentDictionary<byte[], ManualResetEvent>(new StructuralEqualityComparer());
        private ConcurrentDictionary<byte[], byte[]> Replies = new ConcurrentDictionary<byte[], byte[]>(new StructuralEqualityComparer());

        public event RemoteNodeMessageHandler FallbackMessageHandler;
        private HandlerDictionary<string, RemoteNodeMessageHandler> MessageHandlers = new HandlerDictionary<string, RemoteNodeMessageHandler>();
        
        private Thread ReceiveThread { get; set; }
        private CancellationTokenSource DisconnectCanceller = new CancellationTokenSource();
        private ManualResetEvent DisconnectedEvent = new ManualResetEvent(false);

        public bool Disconnected { get; set; }
        public event OnRemoteNodeDisconnect DisconnectEvent;

        private ConcurrentDictionary<string, byte[]> RequestCache = new ConcurrentDictionary<string, byte[]>();
        private ConcurrentDictionary<string, DateTime> CacheTimes = new ConcurrentDictionary<string, DateTime>();

        private byte[] RequestCached(string method, int timeout = 5000)
        {
            var cache_invalid =
                !RequestCache.ContainsKey(method) ||
                (DateTime.UtcNow - CacheTimes[method]).TotalMilliseconds > timeout;

            if (cache_invalid)
            {
                RequestCache[method] = Request(method);
                CacheTimes[method] = DateTime.UtcNow;
            }

            return RequestCache[method];
        }

        private static Logger Logger = LogManager.GetCurrentClassLogger();

        protected void Log(string msg)
        {
#if DEBUG
            lock (Extensions.GlobalPrintLock)
            {
                Console.Write($"{DateTime.UtcNow.ToString("HH:mm:ss.ffff")} [");
                SelfNode.ID.Print();
                Console.Write(":");
                ID.Print();
                Console.WriteLine($"] {msg}");
            }
#else
            Logger.Debug($"[{SelfNode.ID.ToUsefulString(true)}:{ID.ToUsefulString(true)}] {msg}");
#endif
        }

        public RemoteNode(INode self_node, Socket socket)
        {
            Connection = socket;
            SelfNode = self_node;

            MessageHandlers.Add("find_successor", (s, e) => { Reply(e.RequestID, SelfNode.FindSuccessor(e.Parameter)); });
            MessageHandlers.Add("closest_preceding_finger", (s, e) => 
            {
                (var finger_number, var finger_id) = SelfNode.ClosestPrecedingFinger(e.Parameter);
                Reply(e.RequestID, BitConverter.GetBytes(finger_number).Concat(finger_id).ToArray());
            });
            MessageHandlers.Add("notify_forwards", (s, e) => { SelfNode.NotifyForwards(e.Parameter); });
            MessageHandlers.Add("notify_backwards", (s, e) => { SelfNode.NotifyBackwards(e.Parameter); });
            MessageHandlers.Add("get_successor", (s, e) => { Reply(e.RequestID, SelfNode.Successor); });
            MessageHandlers.Add("get_predecessor", (s, e) => { Reply(e.RequestID, SelfNode.Predecessor); });
            MessageHandlers.Add("get_id", (s, e) => { Reply(e.RequestID, SelfNode.ID); });
            MessageHandlers.Add("disconnect", (s, e) =>
            {
                bool temporary = e.Parameter.Length > 0 ? e.Parameter[0] == 0 : false;
                Disconnect(temporary, message: false);
            });
            MessageHandlers.Add("ping", (s, e) => { Reply(e.RequestID, new byte[0]); });
            MessageHandlers.Add("get_peers", (s, e) => 
            {
                int max = Math.Min(16, e.Parameter.Length == 0 ? int.MaxValue : e.Parameter[0]);
                var network = (SelfNode as Node).Network;

                var all_peers = network.Nodes;
                var some_peers = all_peers.ShuffleIterator(Random).Take(max).Select(peer => peer.Key).ToList();
                var id_len = KeySize;
                var blob_len = some_peers.Count * id_len;
                var blob = new byte[blob_len];

                for (int i = 0; i < some_peers.Count; i++)
                {
                    Array.Copy(some_peers[i], 0, blob, i * id_len, id_len);
                }

                Reply(e.RequestID, blob);
            });
        }

        public void AddMessageHandler(string msg, RemoteNodeMessageHandler handler) => MessageHandlers.Add(msg, handler);

        public void Start()
        {
            // TODO: Add timeouts to stop malicious peers from stalling us forever
            ReceiveThread = new Thread(ReceiveLoop);
            ReceiveThread.Start();
            
            ID = Request("get_id");
            ConnectionTime = DateTime.UtcNow;

            if (ID == null)
                Disconnect(false);
        }

        public void Disconnect(bool temporary, bool message = true)
        {
            if (message)
                Invoke("disconnect", new byte[] { (byte)(temporary ? 0 : 1) }); // let our peer know that we're going to disconnect

            try
            {
                Disconnected = true; // we later use this as a flag to unblock all waiting calls to Request() by returning null early
                Connection.Close();

                foreach (var pair in Waiters)
                    pair.Value.Set(); // unblock the threads waiting for a reply

                DisconnectCanceller.Cancel(); // cancel all remaining pending IO

                // just for good measure
                Methods.Clear();
                Waiters.Clear();
                Replies.Clear();
                MessageHandlers.Clear();
                CacheTimes.Clear();
                RequestCache.Clear();

                Log($"ending connection, temporary={temporary}, message={message}");
            }
            catch (Exception ex)
            {
                Log($"{ex.GetType()} while disconnecting: {ex.Message}");
            }
            finally
            { 
                DisconnectEvent?.Invoke(this, new RemoteNodeDisconnectingEventArgs(message, !temporary));
            }
        }

        private bool SendRawMessage(MemoryStream stream) => SendRawMessage(stream.ToArray());
        private bool SendRawMessage(byte[] message)
        {
            if (Disconnected)
                return false;

            try
            {
                lock (Connection)
                {
                    var task = Connection.SendAsync(message, SocketFlags.None);
                    task.Wait(DisconnectCanceller.Token);

                    if (task.IsCanceled)
                        return false;
                }

                SentBytes += message.Length;
                SentMessages++;
                LastMessage = DateTime.UtcNow;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Invoke(string method, byte[] parameter = default)
        {
            parameter = parameter ?? new byte[0];

            using (var request_ms = new MemoryStream())
            using (var request_builder = new BinaryWriter(request_ms))
            {
                request_builder.Write((byte)0xFE);
                request_builder.Write(new byte[4]);
                request_builder.Write(method.Length);
                request_builder.Write(Encoding.ASCII.GetBytes(method));
                request_builder.Write(parameter.Length);
                request_builder.Write(parameter);


                Log($"sending invocation to {method} with {parameter.Length}-byte param {parameter.ToUsefulString(true)}");
                SendRawMessage(request_ms);
            }
        }

        public byte[] Request(string method, byte[] parameter = default)
        {
            if (Disconnected)
                return null;

            parameter = parameter ?? new byte[0];

            var request_id = new byte[4];
            Random.NextBytes(request_id);

            var flag = new ManualResetEvent(false);

            Waiters[request_id] = flag;
            Methods[request_id] = method;
            
            using (var request_ms = new MemoryStream())
            using (var request_builder = new BinaryWriter(request_ms))
            {
                request_builder.Write((byte)0xFE); // request
                request_builder.Write(request_id);
                request_builder.Write(method.Length);
                request_builder.Write(Encoding.ASCII.GetBytes(method));
                request_builder.Write(parameter.Length);
                request_builder.Write(parameter);

                Log($"sending request to {method}(0x{request_id.ToUsefulString()}) with {parameter.Length}-byte param {parameter.ToUsefulString(true)}");
                SendRawMessage(request_ms);
            }

            // TODO: Add timeouts to stop malicious peers from stalling us forever
            if (Disconnected || !flag.WaitOne(500))
            {
                Replies.ForceRemove(request_id);
                Waiters.ForceRemove(request_id);
                Methods.ForceRemove(request_id);

                if (Disconnected)
                    Log($"aborting waiting call {method}(0x{request_id.ToUsefulString()}) as we're disconnecting");
                else
                    Log($"call to {method} with ID {request_id.ToUsefulString()} timed out");

                return null;
            }

            try
            {
                var reply = Replies[request_id];

                Replies.ForceRemove(request_id);
                Waiters.ForceRemove(request_id);
                Methods.ForceRemove(request_id);

                return reply;
            }
            catch
            {
                return null;
            }
        }

        public byte[][] GetCandidatePeers(int max = 16)
        {
            var blob = Request("get_peers", new byte[] { (byte)max });
            var id_len = (SelfNode as Node).KeySize;
            var id_count = blob.Length / id_len;

            var ret = new byte[id_count][];

            for (int i = 0; i < id_count; i++)
            {
                ret[i] = new byte[id_len];
                Array.Copy(blob, i * id_len, ret[i], 0, id_len);
            }

            return ret;
        }

        public void Reply(byte[] request_id, byte[] result)
        {
            result = result ?? new byte[0];

            using (var response_ms = new MemoryStream())
            using (var response_builder = new BinaryWriter(response_ms))
            {
                response_builder.Write((byte)0xFD); // response
                response_builder.Write(request_id);
                response_builder.Write(result.Length);
                response_builder.Write(result);

                SendRawMessage(response_ms);
            }
        }

        private void ReceiveLoop()
        {
            var stream = new NetworkStream(Connection);
            var binary = new BinaryReader(stream);

            while (Connection.Connected && !Disconnected)
            {
                try
                {
                    var message_type = binary.ReadByte();

                    if (message_type == 0xFE) // handle request
                    {
                        var request_id = binary.ReadBytes(4);
                        var method_len = binary.ReadInt32();
                        method_len = Math.Max(0, Math.Min(1024, method_len));

                        var method = Encoding.ASCII.GetString(binary.ReadBytes(method_len));
                        var param_len = binary.ReadInt32();
                        param_len = Math.Max(0, Math.Min(65536, param_len));
                        
                        var parameter = binary.ReadBytes(param_len);

                        if (MessageHandlers.ContainsKey(method))
                        {
                            var handlers = MessageHandlers[method];

                            Task.Run(() => handlers.ForEach(func => func(this, new RemoteNodeMessageEventArgs(method, ID, request_id, parameter)))).ConfigureAwait(false);
                        }
                        else
                        {
                            Task.Run(() => FallbackMessageHandler?.Invoke(this, new RemoteNodeMessageEventArgs(method, ID, request_id, parameter))).ConfigureAwait(false);
                        }

                        Log($"received call to {method}(0x{request_id.ToUsefulString()}) with {param_len}-byte param {parameter.ToUsefulString(true)} (handler: {(MessageHandlers.ContainsKey(method) ? "YES" : "NO")})");
                        ReceivedMessages++;
                        LastMessage = DateTime.UtcNow;
                    }
                    else if (message_type == 0xFD) // handle response
                    {
                        var request_id = binary.ReadBytes(4);
                        var result_len = binary.ReadInt32();
                        result_len = Math.Max(0, Math.Min(65536, result_len));

                        var result = binary.ReadBytes(result_len);

                        if (!Waiters.ContainsKey(request_id))
                        {
                            Log($"received unexpected reply to non-existent request {request_id.ToUsefulString()}");
                            continue;
                        }

                        Log($"received {result_len}-byte reply to {Methods[request_id]}(0x{request_id.ToUsefulString(true)})");
                        ReceivedMessages++;
                        LastMessage = DateTime.UtcNow;

                        Replies[request_id] = result;
                        Waiters[request_id].Set();
                    }
                    else
                        Log($"received invalid message type of 0x{message_type:X2}");
                }
                catch (Exception ex)
                {
                    Log($"{ex.GetType()} thrown in ReceiveLoop(): {ex.Message}");

                    if (!Connection.Connected)
                        Disconnect(false, false);
                }
            }

            Log($"exiting ReceiveLoop");
        }

        public (int, byte[]) ClosestPrecedingFinger(byte[] id)
        {
            var resp = Request("closest_preceding_finger", id);

            if (resp == null)
                return (-1, default);

            return (BitConverter.ToInt32(resp, 0), resp.Skip(4).ToArray());
        }

        public byte[] FindSuccessor(byte[] id)
        {
            return Request("find_successor", id);
        }

        public void NotifyForwards(byte[] id)
        {
            Invoke("notify_forwards", id);
        }

        public void NotifyBackwards(byte[] id)
        {
            Invoke("notify_backwards", id);
        }

        private DateTime LastSuccessfulPing = DateTime.MinValue;
        private TimeSpan PingCacheTime = TimeSpan.FromSeconds(1);

        public bool Ping()
        {
            if ((DateTime.UtcNow - PingCacheTime) < LastSuccessfulPing)
                return true;

            var ping_successful = Request("ping") != null;

            if (ping_successful)
                LastSuccessfulPing = DateTime.UtcNow;

            return ping_successful;
        }
    }
}
