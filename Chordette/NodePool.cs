using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Chordette
{
    public class NodePool : IEnumerable<INode>
    {
        // This is meant to simulate an actual pool of Chord nodes

        public int M { get; set; }
        public List<INode> Nodes { get; set; }
        
        public NodePool(int initial, int m)
        {
            M = m;
            Nodes = Enumerable.Range(0, initial).Select(i => (INode)(new Node(m) { Nodes = this })).ToList();
        }

        public INode this[byte[] id] =>
            Nodes.First(n => n.ID.SequenceEqual(id));

        public IEnumerator<INode> GetEnumerator() => Nodes.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => Nodes.GetEnumerator();
    }
}
