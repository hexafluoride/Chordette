using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace Chordette
{
    public class Network : IEnumerable<INode>
    {
        public int M { get; set; }
        public ConcurrentDictionary<byte[], INode> Nodes { get; set; }
        public Node Self { get; set; }

        public int MaximumPeers { get; set; }
        private int CurrentPeers = 0;

        public Network(Node self, int m)
        {
            MaximumPeers = 8;
            Self = self;
            M = m;
            Nodes = new ConcurrentDictionary<byte[], INode>(new StructuralEqualityComparer());
            Nodes[Self.ID] = Self;
            CurrentPeers = 1;
        }

        private INode Connect(byte[] id)
        {
            // current Chordette peer ID coding:
            // 4 bytes IPv4 address
            // 2 bytes TCP listening port
            
            var offset = 0;

            var ip_bytes = id.Skip(offset).Take(4).ToArray();
            var port_bytes = id.Skip(offset + 4).Take(2).ToArray();

            var endpoint = new IPEndPoint(new IPAddress(ip_bytes), BitConverter.ToUInt16(port_bytes, 0));
            var node = Self.Connect(endpoint);

            node.DisconnectEvent += HandleNodeDisconnect;

            Add(node);

            return node;
        }

        private void HandleNodeDisconnect(object sender, RemoteNodeDisconnectingEventArgs e)
        {
            var id = (sender as RemoteNode).ID;
            Remove(id);
        }

        public void Clear()
        {
            foreach(var pair in Nodes)
            {
                if (!(pair.Value is RemoteNode))
                    continue;

                try
                {
                    ((RemoteNode)pair.Value).Disconnect(true);
                }
                catch { }
            }

            Nodes.Clear();
            Nodes[Self.ID] = Self;
        }

        private void Remove(byte[] id)
        {
            if (Nodes.TryRemove(id, out INode _))
                Interlocked.Decrement(ref CurrentPeers);
        }

        internal void Add(INode node)
        {
            if (CurrentPeers >= MaximumPeers)
            {
                // purge disconnected peers
                foreach (var dead_node in Nodes.Values.Where(n => n is RemoteNode).Cast<RemoteNode>().Where(remote_node => remote_node.Disconnected))
                    Remove(dead_node.ID);
            }

            if (CurrentPeers >= MaximumPeers) // if we STILL need to purge peers
            { 
                var oldest_connection = Nodes.Values.Where(n => n is RemoteNode).Cast<RemoteNode>().OrderByDescending(n => DateTime.UtcNow - n.ConnectionTime).FirstOrDefault();
                oldest_connection.Disconnect(true); // we're only temporarily disconnecting
                Remove(oldest_connection.ID);
            }
            
            if (!Nodes.ContainsKey(node.ID))
                Interlocked.Increment(ref CurrentPeers);

            Nodes[node.ID] = node;
        }

        public INode this[byte[] id] =>
            (id != null && id.Length == M / 8) ? (Nodes.ContainsKey(id) ? Nodes[id] : Connect(id)) : default;

        public IEnumerator<INode> GetEnumerator() => Nodes.Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => Nodes.Values.GetEnumerator();
    }
}
