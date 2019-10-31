using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Chordette
{
    public delegate void RemoteNodeMessageHandler(object sender, RemoteNodeMessageEventArgs e);

    public class RemoteNodeMessageEventArgs : EventArgs
    {
        public byte[] RequestID { get; set; }
        public byte[] Node { get; set; }
        public byte[] Parameter { get; set; }

        public RemoteNodeMessageEventArgs(byte[] source, byte[] request_id, byte[] parameter)
        {
            RequestID = request_id;
            Node = source;
            Parameter = parameter;
        }
    }

    public class RemoteNode : INode
    {
        public static Random Random = new Random();

        public byte[] Successor { get => Request("get_successor"); }
        public byte[] Predecessor { get => Request("get_predecessor"); }
        public byte[] ID { get; private set; }
        public INode SelfNode { get; set; }

        public Socket Connection { get; set; }

        private Dictionary<byte[], ManualResetEvent> Waiters = new Dictionary<byte[], ManualResetEvent>();
        private Dictionary<byte[], byte[]> Replies = new Dictionary<byte[], byte[]>();

        private HandlerDictionary<string, RemoteNodeMessageHandler> MessageHandlers = new HandlerDictionary<string, RemoteNodeMessageHandler>();

        public RemoteNode(INode self_node, byte[] id, Socket socket)
        {
            Connection = socket;
            SelfNode = self_node;

            MessageHandlers.Add("find_successor", (s, e) => { Reply(e.RequestID, SelfNode.FindSuccessor(e.Parameter)); });
            MessageHandlers.Add("closest_preceding_finger", (s, e) => { Reply(e.RequestID, SelfNode.ClosestPrecedingFinger(e.Parameter)); });
            MessageHandlers.Add("notify", (s, e) => { SelfNode.Notify(e.Parameter); });
            MessageHandlers.Add("get_successor", (s, e) => { Reply(e.RequestID, SelfNode.Successor); });
            MessageHandlers.Add("get_predecessor", (s, e) => { Reply(e.RequestID, SelfNode.Predecessor); });
            MessageHandlers.Add("get_id", (s, e) => { Reply(e.RequestID, SelfNode.ID); });
        }

        public void Start()
        {
            var thread = new Thread(ReceiveLoop);
            thread.Start();

            ID = Request("get_id");
        }

        private void Invoke(string method, byte[] parameter = default)
        {
            parameter = parameter ?? new byte[0];

            using (var request_ms = new MemoryStream())
            using (var request_builder = new BinaryWriter(request_ms))
            {
                request_builder.Write(new byte[4]);
                request_builder.Write(method);
                request_builder.Write(parameter.Length);
                request_builder.Write(parameter);

                Connection.Send(request_ms.ToArray());
            }
        }

        private byte[] Request(string method, byte[] parameter = default)
        {
            parameter = parameter ?? new byte[0];

            var request_id = new byte[4];
            Random.NextBytes(request_id);

            var flag = new ManualResetEvent(false);
            Waiters[request_id] = flag;

            using (var request_ms = new MemoryStream())
            using (var request_builder = new BinaryWriter(request_ms))
            {
                request_builder.Write(request_id);
                request_builder.Write(method);
                request_builder.Write(parameter.Length);
                request_builder.Write(parameter);

                Connection.Send(request_ms.ToArray());
            }

            flag.WaitOne();
            var reply = Replies[request_id];

            Replies.Remove(request_id);
            Waiters.Remove(request_id);

            return reply;
        }

        private void Reply(byte[] request_id, byte[] result)
        {
            using (var response_ms = new MemoryStream())
            using (var response_builder = new BinaryWriter(response_ms))
            {
                response_builder.Write(request_id);
                response_builder.Write(result.Length);
                response_builder.Write(result);

                Connection.Send(response_ms.ToArray());
            }
        }

        private void ReceiveLoop()
        {
            var stream = new NetworkStream(Connection);
            var binary = new BinaryReader(stream);

            while (Connection.Connected)
            {
                var message_type = binary.ReadByte();

                if (message_type == 0xFE) // handle request
                {
                    var request_id = binary.ReadBytes(4);
                    var method = binary.ReadString();
                    var param_len = binary.ReadInt32();
                    param_len = Math.Max(0, Math.Min(4096, param_len));

                    var parameter = binary.ReadBytes(param_len);
                    var handlers = MessageHandlers[method];

                    handlers.ForEach(func => func(this, new RemoteNodeMessageEventArgs(ID, request_id, parameter)));
                }
                else if (message_type == 0xFD) // handle response
                {
                    var request_id = binary.ReadBytes(4);
                    var result_len = binary.ReadInt32();
                    result_len = Math.Max(0, Math.Min(4096, result_len));

                    var result = binary.ReadBytes(result_len);

                    Replies[request_id] = result;
                    Waiters[request_id].Set();
                }
            }
        }

        public byte[] ClosestPrecedingFinger(byte[] id)
        {
            return Request("closest_preceding_finger", id);
        }

        public byte[] FindSuccessor(byte[] id)
        {
            return Request("find_successor", id);
        }

        public void Notify(byte[] id)
        {
            Invoke("notify", id);
        }
    }
}
