using System;
using System.Collections.Generic;
using System.Text;

namespace Chordette
{
    public interface INode
    {
        int KeySize { get; }

        byte[] Successor { get; }
        byte[] Predecessor { get; }
        byte[] ID { get; }

        byte[] FindSuccessor(byte[] id);
        (int, byte[]) ClosestPrecedingFinger(byte[] id);
        void NotifyForwards(byte[] id);
        void NotifyBackwards(byte[] id);

        bool Ping();
    }
}
