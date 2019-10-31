using System;
using System.Collections.Generic;
using System.Text;

namespace Chordette
{
    public interface INode
    {
        byte[] Successor { get; }
        byte[] Predecessor { get; }
        byte[] ID { get; }

        byte[] FindSuccessor(byte[] id);
        byte[] ClosestPrecedingFinger(byte[] id);
        void Notify(byte[] id);
    }
}
