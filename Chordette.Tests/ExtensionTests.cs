using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Chordette;

namespace Chordette.Tests
{
    [TestClass]
    public class ExtensionTests
    {
        [TestMethod]
        public void TestIsIn()
        {
            Assert.IsTrue(5.IsIn(0, 10));
            Assert.IsTrue(13.IsIn(10, 3));
            Assert.IsTrue(0.IsIn(10, 3));
            Assert.IsTrue(3.IsIn(10, 3));
            Assert.IsTrue(10.IsIn(10, 3));
            Assert.IsTrue(5.IsIn(0, 5));
            Assert.IsFalse((-5).IsIn(0, 10));
            Assert.IsFalse(5.IsIn(0, 5, end_inclusive: false));
            Assert.IsFalse(5.IsIn(10, 3));
            Assert.IsFalse(9.IsIn(10, 3));
        }
    }
}
