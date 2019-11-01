using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Text;

namespace Chordette
{
    public class NodePool : IEnumerable<Node>
    {
        public int M { get; set; }
        public List<Node> Nodes { get; set; }
        
        public NodePool(int initial, int m, IPAddress addr, int starting_port)
        {
            M = m;
            Nodes = Enumerable.Range(0, initial).Select(i => new Node(addr, starting_port + i, m)).ToList();
        }

        public void StartAll() =>
            Nodes.ForEach(node => node.Start());

        public Node this[byte[] id] =>
            Nodes.First(n => n.ID.SequenceEqual(id));

        public IEnumerator<Node> GetEnumerator() => Nodes.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => Nodes.GetEnumerator();
    }
}
