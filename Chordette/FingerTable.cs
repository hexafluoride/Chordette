using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Text;

namespace Chordette
{
    public class FingerTable : IEnumerable<FingerEntry>
    {
        public FingerEntry[] Entries { get; set; }

        public FingerTable(int m, Node node)
        {
            Entries = new FingerEntry[m];

            for (int i = 0; i < m; i++)
            {
                Entries[i] = FingerEntry.Create(GetFingerID(node.ID, i, m), node);
            }
        }

        private byte[] GetFingerID(byte[] self, int i, int m)
        {
            var self_int = new BigInteger(self, true);
            var two_to_ith = BigInteger.Pow(2, i);
            var finger_id_int = (self_int + two_to_ith) % BigInteger.Pow(2, m);

            //Debug.Assert(finger_id_int < BigInteger.Pow(2, m), "hol up");

            return finger_id_int.ToPaddedArray(m / 8);
        }

        public IEnumerator<FingerEntry> GetEnumerator() => ((IEnumerable<FingerEntry>)Entries).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

        public FingerEntry this[int i] => Entries[i];
    }

    public class FingerEntry
    {
        public byte[] Start { get; set; }
        public byte[] End { get; set; }
        public byte[] ID { get; set; }

        public static FingerEntry Create(byte[] start, Node node)
        {
            return new FingerEntry()
            {
                Start = start,
                ID = node.ID
            };
        }

        public override string ToString() => $"start: {Start.ToUsefulString()}, node: {ID.ToUsefulString()}";
    }
}
