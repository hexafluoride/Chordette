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

            // let's hold some statistics
            int stabilize_count = 0;
            int lookup_count = 0;
            int correct_lookups = 0;
            int incorrect_lookups = 0;

            while(true)
            {
                var random_node = pool.Nodes[Node.Random.Next(pool.Nodes.Count)];
                random_node.Stabilize();
                random_node.FixFingers();
                stabilize_count++;

                // randomly test successor(n)
                var random_id = new byte[pool.M / 8];
                Node.Random.NextBytes(random_id);

                var successor_by_chord = n.FindSuccessor(random_id);
                var successor_actual = pool.GetSuccessorNaive(random_id);
                lookup_count++;

                if (Node.Random.NextDouble() < (1d / 10d))
                {
                    Console.Write($"successor({random_id.ToUsefulString(true)}) = {successor_by_chord.ToUsefulString(true)} (should be {successor_actual.ToUsefulString(true)}), ");
                    var prev_fg_color = Console.ForegroundColor;

                    if (successor_by_chord.SequenceEqual(successor_actual))
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("correct");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("incorrect");
                    }

                    Console.ForegroundColor = prev_fg_color;
                }

                if (successor_by_chord.SequenceEqual(successor_actual))
                    correct_lookups++;
                else
                    incorrect_lookups++;

                Console.Title = $"{stabilize_count} stabilizations, {lookup_count} lookups, {incorrect_lookups} wrong, {correct_lookups} right ({(correct_lookups * 100d) / lookup_count:0.00}%)";

                Thread.Sleep((int)(1000d / stab_freq));
            }
        }
    }
}
