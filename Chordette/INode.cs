using System;
using System.Collections.Generic;
using System.Text;

namespace Chordette
{
    public interface INode
    {
        byte[] Successor { get; set; }
        byte[] Predecessor { get; set; }
        byte[] ID { get; set; }

        byte[] FindSuccessor(byte[] id);
        byte[] ClosestPrecedingFinger(byte[] id);
        void Notify(byte[] id);
    }
}
