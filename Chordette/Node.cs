using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading;

namespace Chordette
{
    public class Node : INode
    {
        public int KeySize { get; set; }
        public Network Network { get; set; }

        public static Random Random = new Random();

        public FingerTable Table { get; set; }

        private byte[] _successor = default;
        private byte[] _predecessor = default;

        public byte[] Successor { get => _successor; set { SuccessorChanged?.Invoke(this, new SuccessorChangedEventArgs(Successor, value)); _successor = value; } }
        public byte[] Predecessor { get => _predecessor; set { PredecessorChanged?.Invoke(this, new PredecessorChangedEventArgs(Predecessor, value)); _predecessor = value; } }

        public byte[] ID { get; set; }

        public event PredecessorChangedEventHandler PredecessorChanged;
        public event SuccessorChangedEventHandler SuccessorChanged;

        public IPEndPoint ListenEndPoint => (IPEndPoint)Listener?.LocalEndpoint;

        protected TcpListener Listener { get; set; }
        protected Thread ListenerThread { get; set; }

        public bool Joined { get; set; }

        private static Logger Logger = LogManager.GetCurrentClassLogger();

        protected void Log(string msg)
        {
#if DEBUG
            lock (Extensions.GlobalPrintLock)
            {
                Console.Write($"{DateTime.UtcNow.ToString("HH:mm:ss.fff")} [");
                ID.Print();
                Console.WriteLine($"] {msg}");
            }
#else
            Logger.Debug($"[{ID.ToUsefulString(true)}] {msg}");
#endif
        }

        protected Node()
        {
            PredecessorChanged += (s, e) => { Log($"Predecessor changed " +
                $"from {e.PreviousPredecessor.ToUsefulString(true)} " +
                $"to {e.NewPredecessor.ToUsefulString(true)}"); };
            
            SuccessorChanged += (s, e) => { Log($"Successor changed " +
                $"from {e.PreviousSuccessor.ToUsefulString(true)} " +
                $"to {e.NewSuccessor.ToUsefulString(true)}"); };

            Predecessor = new byte[0];
        }

        public Node(IPAddress listen_addr, int port, int m) :
            this()
        {
            KeySize = m / 8;

            ID = new byte[KeySize];
            Random.NextBytes(ID);

            // current Chordette peer ID coding:
            // 4 bytes IPv4 address
            // 2 bytes TCP listening port
            var offset = 0;

            Array.Copy(listen_addr.GetAddressBytes(), 0, ID, offset, 4);
            Array.Copy(BitConverter.GetBytes((ushort)port), 0, ID, offset + 4, 2);

            Listener = new TcpListener(listen_addr, port);
            Network = new Network(this, m);

            Table = new FingerTable(m, this);
            Successor = Table[0].ID;
        }

        public void Start()
        {
            Listener.Start();

            ListenerThread = new Thread((ThreadStart)ListenerLoop);
            ListenerThread.Start();

            Log($"started listening on {Listener.LocalEndpoint}");
        }

        protected virtual void ListenerLoop()
        {
            while (true)
            {
                var incoming_socket = Listener.AcceptSocket();

                try
                {
                    Log($"Incoming connection on {Listener.LocalEndpoint} from {incoming_socket.RemoteEndPoint}");
                    var remote_node = new RemoteNode(this, incoming_socket);
                    remote_node.Start();
                    Log($"Connected to {remote_node.ID.ToUsefulString()} on {incoming_socket.RemoteEndPoint}");

                    Network.Add(remote_node);
                }
                catch (Exception ex)
                {
                    Log($"{ex.GetType()} occurred while trying to accept connection: {ex.Message}");

                    try
                    {
                        incoming_socket.Close();
                        incoming_socket.Dispose();
                    }
                    catch { }
                }
            }
        }
        
        public void AskForPeers(byte[] id) => AskForPeers(Network[id] as RemoteNode);
        public int AskForPeers(RemoteNode node)
        {
            if (node == null)
                return 0;

            var peers = node.GetCandidatePeers();
            var usable_peers = Network.CommitCandidatePeers(peers);
            Log($"Received {usable_peers} usable peers out of {peers.GetLength(0)} suggested peers from {node.ID.ToUsefulString(true)}");

            return usable_peers;
        }

        public IEnumerable<byte[]> BootstrapSelf(int max = 4)
        {
            return Network.GetCandidatePeers().Take(max).Where(id => Network.Connect(id) != null).ToList();
        }

        public virtual RemoteNode CreateRemoteNode(IPEndPoint ep)
        {
            Log($"Connecting to {ep}...");

            try
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                socket.Connect(ep);

                var remote_node = new RemoteNode(this, socket);
                remote_node.Start();

                if (remote_node.Disconnected)
                    return null;

                return remote_node;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public byte[] FindPredecessor(byte[] id)
        {
            var n_prime = (INode)this;

            while (id.IsNotIn(n_prime.ID, n_prime.Successor, start_inclusive: false))
            {
                (var finger_id, var next_n_prime_id) = n_prime.ClosestPrecedingFinger(id);

                if (next_n_prime_id == null || next_n_prime_id.SequenceEqual(n_prime.ID))
                {
                    // TODO: figure out how to actually handle this
                    Log("FindPredecessor has failed!");
                    break;
                }
                
                n_prime = Network[next_n_prime_id];

                if (n_prime == null)
                {
                    // TODO: figure out how to actually handle this
                    Log("FindPredecessor has failed!");
                    return id;
                }
            }

            return n_prime.ID;
        }

        public byte[] FindSuccessor(byte[] id)
        {
            var n_prime = Network[FindPredecessor(id)];

            if (n_prime == null)
                return null;

            var succ = n_prime.Successor;
            return succ;
        }

        public (int, byte[]) ClosestPrecedingFinger(byte[] id)
        {
            for (int i = Network.M - 1; i >= 0; i--)
            {
                var finger_id = Table[i].ID;

                if (!Network.IsReachable(finger_id))
                    continue;

                if (finger_id.IsIn(this.ID, id, start_inclusive: false, end_inclusive: false)) // questionable
                    return (i, finger_id);
            }

            return (0, this.ID);
        }

        public bool Join(byte[] id)
        {
            Log($"Joining {id.ToUsefulString()}...");

            if (!Joined)
            {
                var n_prime = Network[id];

                if (n_prime == null)
                    return false;

                Predecessor = new byte[0];

                var proposed_successor = new byte[0];

                int max_tries = 3;

                while ((proposed_successor == null || proposed_successor.Length != id.Length) &&
                    max_tries-- > 0)
                    proposed_successor = n_prime.FindSuccessor(this.ID);
                
                if(proposed_successor == null || proposed_successor.Length != id.Length ||
                    proposed_successor.SequenceEqual(this.ID))
                {
                    Log($"Failed to join the network, exiting");
                    Network.Clear();
                    return false;
                }

                Log($"New successor: {proposed_successor.ToUsefulString()}");
                Successor = proposed_successor;
                Joined = true;
            }

            return Joined;
        }

        public void Stabilize()
        {
            var successor_peer = Network[Successor];
            
            if (!Network.IsReachable(Successor))
            {
                Log($"Unreachable successor, trying to find a new one");
                Successor = ID;

                var peers_ordered_by_chord_dist = Network
                    .OfType<RemoteNode>()
                    .Where(p => !p.ID.SequenceEqual(ID) && Network.IsReachable(p.ID))
                    .Select(Node => (Node, NodeHelpers.Distance(ID, Node.ID, Network.M)))
                    .OrderBy(tuple => tuple.Item2)
                    .Select(tuple => tuple.Node)
                    .ToList(); // stop complaints about Peers being modified

                var list = (string.Join(", ", peers_ordered_by_chord_dist.Select(n => $"{n.ID.ToUsefulString(true)}:{BigInteger.Log(NodeHelpers.Distance(ID, n.ID, Network.M)):0.00}")));

                Log($"distance-ordered peer list: [{list}]");

                foreach (var peer in peers_ordered_by_chord_dist)
                {
                    if (Network.IsReachable(peer.ID))
                    {
                        Log($"Found alternative successor {peer.ID.ToUsefulString(true)} by second method");
                        successor_peer = peer;
                        Successor = peer.ID;
                        break;
                    }
                    else
                        (peer as RemoteNode).Disconnect(false);
                }
            }

            var x = successor_peer?.Predecessor;

            if (x?.IsIn(this.ID, Successor, start_inclusive: false, end_inclusive: false) == true && Network.IsReachable(x))
            {
                Successor = x;
            }

            Network[Successor]?.NotifyForwards(this.ID);
        }

        public void NotifyForwards(byte[] id)
        {
            if (Predecessor?.SequenceEqual(ID) == true ||
                !Network.IsReachable(Predecessor) ||
                id.IsIn(Predecessor, this.ID, start_inclusive: false, end_inclusive: false))
            {
                Predecessor = id;
            }
            else
            {
                if (id != null && Predecessor != null)
                {
                    if (!Predecessor.SequenceEqual(id))
                    {
                        Log($"Rejected forwards notification from {id.ToUsefulString(true)} (current predecessor: {Predecessor.ToUsefulString(true)})");

                        var peer = Network[id];

                        if (peer != null)
                        {
                            peer.NotifyBackwards(Predecessor);
                        }
                    }
                }
            }
        }

        public void NotifyBackwards(byte[] id)
        {
            if (Successor?.SequenceEqual(ID) == true ||
                !Network.IsReachable(Successor) ||
                id.IsIn(this.ID, Successor, start_inclusive: false, end_inclusive: false))
            {
                Successor = id;
            }
            else
            {
                if (id != null && Successor != null)
                {
                    if (!Successor.SequenceEqual(id))
                    {
                        Log($"Rejected backwards notification from {id.ToUsefulString(true)} (current successor: {Successor.ToUsefulString(true)})");

                        var peer = Network[id];

                        if (peer != null)
                        {
                            peer.NotifyForwards(Predecessor);
                        }
                    }
                }
            }
        }

        public void FixFingers()
        {
            for (int i = 0; i < Network.M; i++)
            {
                var entry = Table[i];
                var id = entry.ID;

                if (!Network.IsReachable(id))
                {
                    Log($"Fixing unreachable finger {i} with ID {id.ToUsefulString(true)}");
                    FixFinger(i);
                }
            }

            FixRandomFinger();
        }

        public void FixFinger(int finger_id)
        {
            var successor = FindSuccessor(Table[finger_id].Start);

            if (successor != null && successor.Length > 0)
            {
                Table[finger_id].ID = successor;
            }
            else
                Log($"Couldn't fix finger {finger_id}, successor(n) returned nothing");
        }

        public void FixRandomFinger() => FixFinger(Random.Next(0, Network.M));

        public bool Ping() => true; // local node is always reachable

        public override string ToString()
        {
            if (this == null || ID == null || ID.Length == 0)
                return "(no ID)";

            return ID.ToUsefulString();
        }

        #region non-concurrent methods described in section 4 of the whitepaper
        //public void InitializeFingerTable(byte[] id)
        //{
        //    var n_prime = Nodes[id];
        //    Table[0].ID = Successor = n_prime.FindSuccessor(Table[0].Start);
        //    Predecessor = Nodes[Successor].Predecessor;
        //    Nodes[Successor].Predecessor = this.ID;

        //    for (int i = 0; i < Nodes.M - 1; i++)
        //    {
        //        if (Table[i + 1].Start.IsIn(this.ID, Table[i].ID, end_inclusive: false))
        //            Table[i + 1].ID = Table[i].ID;
        //        else
        //            Table[i + 1].ID = n_prime.FindSuccessor(Table[i + 1].Start);
        //    }
        //}

        //public void UpdateOthers()
        //{
        //    for (int i = 1; i < Nodes.M; i++)
        //    {
        //        var p = Nodes[FindPredecessor((new BigInteger(this.ID, true) - BigInteger.Pow(2, i - 1)).ToPaddedArray(Nodes.M / 8))];
        //        p.UpdateFingerTable(this.ID, i);
        //    }
        //}

        //public void UpdateFingerTable(byte[] id, int i)
        //{
        //    if (id.IsIn(this.ID, Table[i].ID, end_inclusive: false))
        //    {
        //        Table[i].ID = id;
        //        Nodes[Predecessor].UpdateFingerTable(id, i);
        //    }
        //}
        #endregion
    }

    public delegate void PredecessorChangedEventHandler(object sender, PredecessorChangedEventArgs e);
    public delegate void SuccessorChangedEventHandler(object sender, SuccessorChangedEventArgs e);

    public class PredecessorChangedEventArgs : EventArgs
    {
        public byte[] PreviousPredecessor { get; set; }
        public byte[] NewPredecessor { get; set; }

        public PredecessorChangedEventArgs(byte[] previous_predecessor, byte[] new_predecessor)
        {
            PreviousPredecessor = previous_predecessor;
            NewPredecessor = new_predecessor;
        }
    }

    public class SuccessorChangedEventArgs : EventArgs
    {
        public byte[] PreviousSuccessor { get; set; }
        public byte[] NewSuccessor { get; set; }

        public SuccessorChangedEventArgs(byte[] previous_successor, byte[] new_successor)
        {
            PreviousSuccessor = previous_successor;
            NewSuccessor = new_successor;
        }
    }
}
