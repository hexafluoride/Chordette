using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

        public Node this[byte[] id] =>
            Nodes.First(n => n.ID.SequenceEqual(id));

        public IEnumerator<Node> GetEnumerator()
        {
            return Nodes.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return Nodes.GetEnumerator();
        }
    }
}
