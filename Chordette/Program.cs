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
            int m = 160;
            int node_count = 64;

            double heartbeat = 100; // one cycle of the test loop per 100ms
            double stab_freq = 1; // a random node is stabilized every cycle
            double join_freq = 15; // a random node is joined every 15 cycles
            double test_freq = 2; // a successor(n) test is performed every 2 cycles
            double print_freq = 10; // 1 successor(n) test is printed to the console for every 10 tests

            var pool = new NodePool(node_count, m);
            var n = pool.First(); // our node

            Console.WriteLine($"We're {n.ID.ToUsefulString()}");
            Console.WriteLine("Joining first node to the network...");

            n.Join(default);

            var joined_nodes = new List<Node>() { n };
            var nodes_to_join = pool.Nodes.Where(node => !node.ID.SequenceEqual(n.ID)).ToList();
            
            n.Stabilize();
            n.FixFingers();

            // let's hold some statistics
            int stabilize_count = 0;
            int lookup_count = 0;
            int correct_lookups = 0;
            int incorrect_lookups = 0;
            List<bool> last_n_lookups = new List<bool>();
            int lookup_average_n = 100;

            while(true)
            {
                // randomly join a node to the network
                if(nodes_to_join.Any() && Node.Random.NextDouble() < (1d / join_freq))
                {
                    var connects_through = joined_nodes[Node.Random.Next(joined_nodes.Count)];
                    var node_to_join = nodes_to_join[Node.Random.Next(nodes_to_join.Count)];

                    Console.WriteLine($"Joining {node_to_join.ID.ToUsefulString()} through {connects_through.ID.ToUsefulString()}...");
                    node_to_join.Join(connects_through.ID);

                    node_to_join.Stabilize();
                    node_to_join.FixFingers();
                    connects_through.Stabilize();
                    connects_through.FixFingers();

                    joined_nodes.Add(node_to_join);
                    nodes_to_join.Remove(node_to_join);
                }

                // stabilize random node N
                if (Node.Random.NextDouble() < (1d / stab_freq))
                {
                    var random_node = pool.Nodes[Node.Random.Next(pool.Nodes.Count)];

                    random_node.Stabilize();
                    random_node.FixFingers();
                    stabilize_count++;
                }

                // randomly test successor(n) through the node we picked earlier
                if (Node.Random.NextDouble() < (1d / test_freq))
                {
                    var random_id = new byte[pool.M / 8];
                    Node.Random.NextBytes(random_id);

                    var successor_by_chord = n.FindSuccessor(random_id);
                    var successor_actual = NodeHelpers.GetSuccessorNaive(joined_nodes, random_id);
                    lookup_count++;

                    if (successor_by_chord.SequenceEqual(successor_actual))
                    {
                        last_n_lookups.Add(true);
                        correct_lookups++;
                    }
                    else
                    {
                        last_n_lookups.Add(false);
                        incorrect_lookups++;
                    }

                    if (last_n_lookups.Count > lookup_average_n)
                        last_n_lookups.RemoveAt(0);

                    // randomly print the results of the successor(n) sanity check
                    if (Node.Random.NextDouble() < (1d / print_freq))
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
                }

                // update statistics display
                Console.Title = $"{stabilize_count} stabilizations, {lookup_count} lookups, {incorrect_lookups} wrong, {correct_lookups} right ({(correct_lookups * 100d) / lookup_count:0.00}%, last {lookup_average_n}: {(last_n_lookups.Count(t => t) * 100d / last_n_lookups.Count):0.00}%)";
                Thread.Sleep((int)heartbeat);
            }
        }
    }
}
