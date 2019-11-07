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
        public Network Peers { get; set; }

        public static Random Random = new Random();

        public FingerTable Table { get; set; }
        public byte[] Successor { get; set; }
        public byte[] Predecessor { get; set; }

        public byte[] ID { get; set; }

        private TcpListener Listener { get; set; }
        private Thread ListenerThread { get; set; }

        private bool Joined { get; set; }

        private void Log(string msg)
        {
#if DEBUG
            lock (Extensions.GlobalPrintLock)
            {
                Console.Write($"{DateTime.UtcNow.ToString("HH:mm:ss.ffff")} [");
                ID.Print();
                Console.WriteLine($"] {msg}");
            }
#endif
        }

        public Node(IPAddress listen_addr, int port, int m)
        {
            ID = new byte[m / 8];
            Random.NextBytes(ID);

            // current Chordette peer ID coding:
            // 4 bytes IPv4 address
            // 2 bytes TCP listening port
            var offset = 0;

            Array.Copy(listen_addr.GetAddressBytes(), 0, ID, offset, 4);
            Array.Copy(BitConverter.GetBytes((short)port), 0, ID, offset + 4, 2);

            Listener = new TcpListener(listen_addr, port);
            Peers = new Network(this, m);

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

        private void ListenerLoop()
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

                    Peers.Add(remote_node);
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

        public RemoteNode Connect(IPEndPoint ep)
        {
            Log($"Connecting to {ep}...");

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(ep);

            var remote_node = new RemoteNode(this, socket);
            remote_node.Start();

            return remote_node;
        }

        public byte[] FindPredecessor(byte[] id)
        {
            var n_prime = (INode)this;

            while (id.IsNotIn(n_prime.ID, n_prime.Successor, start_inclusive: false))
            {
                var next_n_prime_id = n_prime.ClosestPrecedingFinger(id);

                if (next_n_prime_id == null || next_n_prime_id.SequenceEqual(n_prime.ID))
                {
                    // TODO: figure out how to actually handle this
                    Log("FindPredecessor has failed!");
                    break;
                }
                
                n_prime = Peers[next_n_prime_id];
            }

            return n_prime.ID;
        }

        public byte[] FindSuccessor(byte[] id)
        {
            var n_prime = Peers[FindPredecessor(id)];
            var succ = n_prime.Successor;
            return succ;
        }

        public byte[] ClosestPrecedingFinger(byte[] id)
        {
            for (int i = Peers.M - 1; i >= 0; i--)
            {
                var finger_id = Table[i].ID;

                if (finger_id.IsIn(this.ID, id, start_inclusive: false, end_inclusive: false)) // questionable
                    return finger_id;
            }

            return this.ID;
        }

        public bool Join(byte[] id)
        {
            if (id != null && id.Length != 0)
            {
                var n_prime = Peers[id];
                Predecessor = new byte[0];

                var proposed_successor = new byte[0];

                int max_tries = 3;

                while ((proposed_successor == null || proposed_successor.Length != id.Length) &&
                    max_tries-- > 0)
                    proposed_successor = n_prime.FindSuccessor(this.ID);
                
                if(proposed_successor == null || proposed_successor.Length != id.Length)
                {
                    Log($"Failed to join the network, exiting");
                    Peers.Clear();
                    return false;
                }

                Log($"New successor: {proposed_successor.ToUsefulString()}");
                Successor = proposed_successor;
            }
            else
            {
                Predecessor = id;
            }

            Joined = true;
            return true;
        }

        public void Stabilize()
        {
            var x = Peers[Successor]?.Predecessor;

            if (x?.IsIn(this.ID, Successor, start_inclusive: false, end_inclusive: false) == true)
            {
                Successor = x;
            }

            Peers[Successor]?.Notify(this.ID);
        }

        public void Notify(byte[] id)
        {
            if (Predecessor == null || Predecessor.Length == 0 || 
                Predecessor.SequenceEqual(ID) || 
                id.IsIn(Predecessor, this.ID, start_inclusive: false, end_inclusive: false))
            {
                Log($"New predecessor: {id.ToUsefulString()}");
                Predecessor = id;
            }
        }

        public void FixFingers()
        {
            var random_index = Random.Next(0, Peers.M);

            //Log($"Fixing finger {random_index}...");
            var successor = FindSuccessor(Table[random_index].Start);

            if (successor != null && successor.Length > 0)
            {
                Table[random_index].ID = successor;
                //Log($"Finger {random_index} is now {Table[random_index]}");
            }
            else
                Log($"Couldn't fix finger {random_index}, successor(n) returned nothing");
        }

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
}
