using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Chordette
{
    public static class NodeHelpers
    {
        /// <summary>
        /// Calculates successor(id) using the knowledge of all nodes in the network. This should ONLY be used to verify correctness.
        /// </summary>
        /// <param name="id">The Chord identifier to find the successor node of.</param>
        /// <returns>The Chord identifier of the successor node.</returns>
        public static byte[] GetSuccessorNaive(List<Node> nodes, byte[] id)
        {
            var all_nodes_sorted = nodes.OrderBy(n => new BigInteger(n.ID, true));

            // find min(dist(id, n)) where id <= n and dist is circular
            var nodes_with_dist = all_nodes_sorted.Select(n => (n, Distance(id, n.ID, nodes.First().ID.Length * 8))).OrderBy(tuple => tuple.Item2);
            var shortest = nodes_with_dist.First();

            return shortest.n.ID;
        }

        /// <summary>
        /// Calculates the distance between two points on the Chord circle.
        /// </summary>
        /// <remarks>Distance here is defined as the length of the arc that contains all the points "after" id, and all the points "before" n 
        /// given one of these specifiers is inclusive and the other exclusive.</remarks>
        /// <param name="id">The first point.</param>
        /// <param name="n">The second point.</param>
        /// <param name="m">Number of bits used to define the Chord keyspace.</param>
        /// <returns>The distance between id and n.</returns>
        public static BigInteger Distance(byte[] id, byte[] n, int m)
        {
            var id_int = new BigInteger(id, true);
            var node_int = new BigInteger(n, true);
            var m_int = BigInteger.Pow(2, m);

            if (node_int < id_int) // if "walking forward" from id to n requires us to step through 0
                return (m_int - id_int) + node_int;
            else
                return node_int - id_int;
        }
    }
}
