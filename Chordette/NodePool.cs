using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Chordette
{
    public class NodePool : IEnumerable<Node>
    {
        // This is meant to simulate an actual pool of Chord nodes

        public int M { get; set; }
        public List<Node> Nodes { get; set; }
        
        public NodePool(int initial, int m)
        {
            M = m;
            Nodes = Enumerable.Range(0, initial).Select(i => new Node(m) { Nodes = this }).ToList();
        }

        /// <summary>
        /// Calculates successor(id) using the knowledge of all nodes in the network. This should ONLY be used to verify correctness.
        /// </summary>
        /// <param name="id">The Chord identifier to find the successor node of.</param>
        /// <returns>The Chord identifier of the successor node.</returns>
        public byte[] GetSuccessorNaive(byte[] id)
        {
            var all_nodes_sorted = Nodes.OrderBy(n => new BigInteger(n.ID, true));

            // find min(dist(id, n)) where id <= n and dist is circular
            var nodes_with_dist = all_nodes_sorted.Select(n => (n, Distance(id, n.ID, M))).OrderBy(tuple => tuple.Item2);
            var shortest = nodes_with_dist.First();

            return shortest.n.ID;
        }

        private BigInteger Distance(byte[] id, byte[] n, int m)
        {
            var id_int = new BigInteger(id, true);
            var node_int = new BigInteger(n, true);
            var m_int = BigInteger.Pow(2, m);

            if (node_int < id_int) // if "walking forward" from id to n requires us to step through 0
                return (m_int - id_int) + node_int;
            else
                return node_int - id_int;
        }

        public Node this[byte[] id] =>
            Nodes.First(n => n.ID.SequenceEqual(id));

        public IEnumerator<Node> GetEnumerator() => Nodes.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => Nodes.GetEnumerator();
    }
}
