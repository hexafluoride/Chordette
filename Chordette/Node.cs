using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Chordette
{
    public class Node
    {
        internal NodePool Nodes { get; set; }

        public static Random Random = new Random();

        public FingerTable Table { get; set; }
        public byte[] Successor { get; set; }
        public byte[] Predecessor { get; set; }

        public byte[] ID { get; set; }

        internal Node(int m)
        {
            ID = new byte[m / 8];
            Random.NextBytes(ID);

            Table = new FingerTable(m, this);
            Successor = Table[0].ID;
        }

        public byte[] FindPredecessor(byte[] id)
        {
            var n_prime = this;

            while (id.IsNotIn(n_prime.ID, n_prime.Successor, start_inclusive: false))
            {
                n_prime = Nodes[n_prime.ClosestPrecedingFinger(id)];
            }

            return n_prime.ID;
        }

        public byte[] FindSuccessor(byte[] id)
        {
            var n_prime = Nodes[FindPredecessor(id)];
            return n_prime.Successor;
        }

        public byte[] ClosestPrecedingFinger(byte[] id)
        {
            for (int i = Nodes.M - 1; i >= 0; i--)
            {
                var finger_id = Table[i].ID;

                if (finger_id.IsIn(this.ID, id, start_inclusive: false, end_inclusive: false)) // questionable
                    return finger_id;
            }

            return this.ID;
        }

        public void Join(byte[] id)
        {
            if (id != null && id.Length != 0)
            {
                var n_prime = Nodes[id];
                Predecessor = new byte[0];
                Successor = n_prime.FindSuccessor(this.ID);
            }
            else
            {
                Predecessor = id;
            }
        }

        public void Stabilize()
        {
            var x = Nodes[Successor].Predecessor;

            if (x?.IsIn(this.ID, Successor, start_inclusive: false, end_inclusive: false) == true)
                Successor = x;

            Nodes[Successor].Notify(this.ID);
        }

        public void Notify(byte[] id)
        {
            if (Predecessor == null || Predecessor.Length == 0 || id.IsIn(Predecessor, this.ID, start_inclusive: false, end_inclusive: false))
                Predecessor = id;
        }

        public void FixFingers()
        {
            var random_index = Random.Next(1, Nodes.M);
            Table[random_index].ID = FindSuccessor(Table[random_index].Start);
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
