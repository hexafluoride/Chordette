using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;

namespace Chordette
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.SetWindowSize(160, Console.WindowHeight);

            int m = 160;
            int node_count = 64;

            double heartbeat = 15; // one cycle of the test loop per 15ms
            double stab_freq = 1; // a random node is stabilized every cycle
            double join_freq = 5; // a random node is joined every 5 cycles
            double test_freq = 2; // a successor(n) test is performed every 2 cycles
            double print_freq = 10; // 1 successor(n) test is printed to the console for every 10 tests

            var pool = new NodePool(node_count, m, IPAddress.Loopback, 30100);
            var n = pool.First(); // our node

            Console.WriteLine($"We're {n.ID.ToUsefulString()}");
            Console.WriteLine("Starting all listeners...");
            pool.StartAll();

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
            List<int> last_n_heartbeat_times = new List<int>();
            int lookup_average_n = 100;
            long last_message_count = 0;
            long last_stat_calc_time = 0; // milliseconds since message rate last calc.
            long message_rate = 0;
            long last_heartbeat_time = 0;

            var sw = Stopwatch.StartNew();

            while(true)
            {
                // check if there is any input
                if (Console.KeyAvailable)
                {
                    switch(Console.ReadKey(true).Key)
                    {
                        case ConsoleKey.Spacebar:
                            heartbeat = 500;
                            join_freq = stab_freq = test_freq = 1;
                            print_freq = 1;
                            break;
                    }
                }

                // randomly join a node to the network
                if(nodes_to_join.Any() && Node.Random.NextDouble() < (1d / join_freq))
                {
                    var connects_through = joined_nodes[Node.Random.Next(joined_nodes.Count)];
                    var node_to_join = nodes_to_join[Node.Random.Next(nodes_to_join.Count)];

                    Console.WriteLine($"Joining {node_to_join.ID.ToUsefulString()} through {connects_through.ID.ToUsefulString()}...");
                    if (!node_to_join.Join(connects_through.ID))
                    {
                        Console.WriteLine($"Failed to join {node_to_join} to the network.");
                    }
                    else
                    {
                        node_to_join.Stabilize();
                        node_to_join.FixFingers();
                        connects_through.Stabilize();
                        connects_through.FixFingers();

                        joined_nodes.Add(node_to_join);
                        nodes_to_join.Remove(node_to_join);
                    }
                }

                // stabilize random node N
                if (Node.Random.NextDouble() < (1d / stab_freq))
                {
                    var random_node = joined_nodes[Node.Random.Next(joined_nodes.Count)];

                    random_node.Stabilize();
                    random_node.FixFingers();
                    stabilize_count++;
                }

                // randomly test successor(n) through the node we picked earlier
                if (Node.Random.NextDouble() < (1d / test_freq))
                {
                    var random_node = joined_nodes[Node.Random.Next(joined_nodes.Count)];
                    var random_id = new byte[pool.M / 8];
                    Node.Random.NextBytes(random_id);

                    var successor_by_chord = random_node.FindSuccessor(random_id) ?? new byte[0];
                    var successor_actual = /*new byte[pool.M / 8]; // */ NodeHelpers.GetSuccessorNaive(joined_nodes, random_id);
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
                        lock (Extensions.GlobalPrintLock)
                        {
                            Console.Write($"{random_node.ID.ToUsefulString(true)}.successor({random_id.ToUsefulString(true)}) = {successor_by_chord.ToUsefulString(true)} (should be {successor_actual.ToUsefulString(true)}), ");
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
                }

                if (sw.ElapsedMilliseconds - last_stat_calc_time > 1000) // calculate once every second
                {
                    // update statistics display
                    Console.Title = $"{joined_nodes.Count} nodes, " +
                        $"{stabilize_count} stabilizations, " +
                        $"{joined_nodes.Average(q => q.Nodes.Nodes.Skip(0).Count()):0.00} average connections per peer, " +
                        $"{message_rate} messages per second, " +
                        $"{lookup_count} lookups, " +
                        $"{incorrect_lookups} wrong, " +
                        $"{correct_lookups} right ({(correct_lookups * 100d) / lookup_count:0.00}%, last {lookup_average_n}: {(last_n_lookups.Count(t => t) * 100d / last_n_lookups.Count):0.00}%) " +
                        $"average heartbeat time: {last_n_heartbeat_times.Average():0.00}ms";

                    var actual_time = sw.ElapsedMilliseconds - last_stat_calc_time; // time since last stat calculation, we shouldn't assume 1000 for accuracy
                    last_stat_calc_time = sw.ElapsedMilliseconds;

                    // we divide the total count by 2 here because each unique message sent between peers is counted twice
                    // once in the receiving side, once in the sending side
                    //var message_count = pool.Nodes.Sum(local_node => local_node.Nodes.Where(node => node is RemoteNode).Cast<RemoteNode>().Sum(remote_node => remote_node.ReceivedMessages + remote_node.SentMessages)) / 2;
                    var message_count = RemoteNode.SentMessages + RemoteNode.ReceivedMessages;
                    var delta = message_count - last_message_count;
                    last_message_count = message_count;
                    message_rate = (long)(delta / (actual_time / 1000d)); // correcting for if a heartbeat takes longer than the stat calc. cycle period
                }

                var elapsed = sw.ElapsedMilliseconds - last_heartbeat_time;
                last_heartbeat_time = sw.ElapsedMilliseconds;

                if (last_n_heartbeat_times.Count > 10)
                    last_n_heartbeat_times.RemoveAt(0);

                last_n_heartbeat_times.Add((int)elapsed);

                lock (Extensions.GlobalPrintLock)
                {
                    Console.WriteLine($"Heartbeat took {elapsed}ms, was expecting {heartbeat}ms (delta: {heartbeat - elapsed}ms)");
                }
                var next_sleep = (int)Math.Max(0, heartbeat - elapsed);

                if (next_sleep != 0)
                    Thread.Sleep(next_sleep);
            }
        }
    }
}
