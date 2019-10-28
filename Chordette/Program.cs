using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Chordette
{
    class Program
    {
        static void Main(string[] args)
        {
            // let's initialize a keyspace with m = 160 and create 16 nodes
            int m = 160;
            double stab_freq = 10; // 10 stabilizations per second
            var pool = new NodePool(16, m);
            var n = pool.First(); // our node

            Console.WriteLine($"We're {n.ID.ToUsefulString()}");
            Console.WriteLine("Joining first node to the network...");

            n.Join(default);

            var joined = new List<Node>() { n };
            Console.WriteLine("Joining nodes to network...");

            n.Stabilize();
            n.FixFingers();

            foreach (var node in pool)
            {
                if (node.ID.SequenceEqual(n.ID))
                    continue;

                var connects_through = joined[Node.Random.Next(joined.Count)];

                Console.WriteLine($"Joining {node.ID.ToUsefulString()} through {connects_through.ID.ToUsefulString()}...");
                node.Join(connects_through.ID);

                node.Stabilize();
                node.FixFingers();
                connects_through.Stabilize();
                connects_through.FixFingers();

                joined.Add(node);
            }

            Console.WriteLine("Stabilizing...");

            while(true)
            {
                var random_node = pool.Nodes[Node.Random.Next(pool.Nodes.Count)];
                random_node.Stabilize();
                random_node.FixFingers();

                if (Node.Random.NextDouble() < (1d / 10d))
                {
                    var random_id = new byte[pool.M / 8];
                    Node.Random.NextBytes(random_id);

                    Console.WriteLine($"successor({random_id.ToUsefulString()}) = {n.FindSuccessor(random_id).ToUsefulString()}");
                    // randomly test successor(n)
                }

                Thread.Sleep((int)(1000d / stab_freq));
            }
        }
    }
}
