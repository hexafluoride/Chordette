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
        public int KeySize => M / 8;
        public int M { get; set; }
        public ConcurrentDictionary<byte[], INode> Nodes { get; set; }
        public Node Self { get; set; }

        public int MaximumPeers { get; set; }
        public int PeerCount => CurrentPeers;

        private int CurrentPeers = 0;

        protected internal HashSet<byte[]> CandidatePeers = new HashSet<byte[]>();
        protected internal Dictionary<byte[], int> UnreachableNodes = new Dictionary<byte[], int>(new StructuralEqualityComparer());
        
        public Network(Node self, int m)
        {
            MaximumPeers = 8;
            Self = self;
            M = m;
            Nodes = new ConcurrentDictionary<byte[], INode>(new StructuralEqualityComparer());
            Nodes[Self.ID] = Self;
        }

        public IEnumerable<byte[]> GetCandidatePeers()
        {
            var valid = CandidatePeers.Where(id => !Nodes.ContainsKey(id));
            return valid;
        }

        protected internal int CommitCandidatePeers(params byte[][] ids)
        {
            int added = 0;

            foreach (var id in ids)
            {
                if (!IsReachable(id, strong_check: false))
                    continue;

                if (CandidatePeers.Add(id))
                    added++;
            }

            return added;
        }

        public bool IsReachable(byte[] id, bool strong_check = true)
        {
            if (id == null || id.Length != KeySize)
                return false;

            if (UnreachableNodes.ContainsKey(id) && UnreachableNodes[id] > 0)
                return false;

            if (!strong_check)
                return true;

            if (this[id] == null)
                return false;

            if (!this[id].Ping())
                return false;

            return true;
        }

        public void MarkUnreachable(byte[] id)
        {
            if (!UnreachableNodes.ContainsKey(id))
                UnreachableNodes[id] = 0;

            UnreachableNodes[id]++;
        }

        public void MarkReachable(byte[] id) => UnreachableNodes[id] = 0;

        public virtual INode Connect(byte[] id)
        {
            // current Chordette peer ID coding:
            // 4 bytes IPv4 address
            // 2 bytes TCP listening port
            
            var offset = 0;

            var ip_bytes = id.Skip(offset).Take(4).ToArray();
            var port_bytes = id.Skip(offset + 4).Take(2).ToArray();

            var endpoint = new IPEndPoint(new IPAddress(ip_bytes), BitConverter.ToUInt16(port_bytes, 0));
            var node = Self.CreateRemoteNode(endpoint);

            if (node == null)
            {
                MarkUnreachable(id);
                return null;
            }

            node.DisconnectEvent += HandleNodeDisconnect;

            Add(node);

            return node;
        }

        protected virtual void HandleNodeDisconnect(object sender, RemoteNodeDisconnectingEventArgs e)
        {
            var id = (sender as RemoteNode).ID;
            Remove(id);

            if (e.LongTerm)
                MarkUnreachable(id);
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

            UnreachableNodes.Clear();
        }

        private void Remove(byte[] id)
        {
            Nodes.TryRemove(id, out INode _);
            Interlocked.Decrement(ref CurrentPeers);
        }

        protected internal virtual void Add(INode node)
        {
            if (CurrentPeers >= MaximumPeers)
            {
                var purgable_peers = Nodes.Values
                    .OfType<RemoteNode>()
                    .Where(n => Self?.Successor?.SequenceEqual(n?.ID) != true && Self?.Predecessor?.SequenceEqual(n?.ID) != true);

                // purge disconnected peers
                foreach (var dead_node in purgable_peers.Where(remote_node => remote_node.Disconnected))
                    Remove(dead_node.ID);

                if (CurrentPeers >= MaximumPeers) // if we STILL need to purge peers
                {
                    var oldest_connection = purgable_peers.OrderByDescending(n => DateTime.UtcNow - n.ConnectionTime).FirstOrDefault();
                    oldest_connection.Disconnect(true); // we're only temporarily disconnecting
                    Remove(oldest_connection.ID);
                }
            }
            
            if (!Nodes.ContainsKey(node.ID))
                Interlocked.Increment(ref CurrentPeers);

            Nodes[node.ID] = node;

            if (node.Ping())
                MarkReachable(node.ID);
        }

        public INode this[byte[] id] =>
            (id != null && id.Length == KeySize) ? (Nodes.ContainsKey(id) ? Nodes[id] : Connect(id)) : default;

        public IEnumerator<INode> GetEnumerator() => Nodes.Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => Nodes.Values.GetEnumerator();
    }
}
