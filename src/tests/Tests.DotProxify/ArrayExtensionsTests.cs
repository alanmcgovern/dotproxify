//MIT License

//Copyright (C) 2021 Alan McGovern

//Permission is hereby granted, free of charge, to any person obtaining a copy
//of this software and associated documentation files (the "Software"), to deal
//in the Software without restriction, including without limitation the rights
//to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//copies of the Software, and to permit persons to whom the Software is
//furnished to do so, subject to the following conditions:

//The above copyright notice and this permission notice shall be included in all
//copies or substantial portions of the Software.

//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//SOFTWARE.

using DotProxify;

using NUnit.Framework;

namespace Tests.DotProxy
{
    [TestFixture]
    public class ArrayExtensionsTests
    {
        [Test]
        public void SourceEmpty ()
        {
            var array = new byte[] { };
            var sequence = new byte[] { 0 };
            Assert.AreEqual (-1, array.IndexOfSequence (sequence));
        }
        [Test]
        public void SequenceEmpty ()
        {
            var array = new byte[] { 0, 1, 0, 1, 0, 2, 0, 2 };
            var sequence = new byte[] { };
            Assert.Throws<ArgumentException> (() => array.IndexOfSequence (sequence));
        }

        [Test]
        public void SequenceInMiddle ()
        {
            var array = new byte[] { 0, 1, 0, 1, 0, 2, 0, 2 };
            var sequence = new byte[] { 0, 2 };
            Assert.AreEqual (4, array.IndexOfSequence (sequence));
        }

        [Test]
        public void SequenceInMiddle_SourceTruncated ()
        {
            var array = new byte[] { 0, 1, 0, 1, 0, 2, 0, 2 };
            var sequence = new byte[] { 0, 2 };
            Assert.AreEqual (4, array.IndexOfSequence (sequence, 2, array.Length - 3));
        }

        [Test]
        public void SequenceAtEnd ()
        {
            var array = new byte[] { 0, 1, 0, 1, 0, 2, 0, 2 };
            var sequence = new byte[] { 0, 2, 0, 2 };
            Assert.AreEqual (4, array.IndexOfSequence (sequence));
        }

        [Test]
        public void SequenceAtEnd_SourceTruncated ()
        {
            var array = new byte[] { 0, 1, 0, 1, 0, 2, 0, 2 };
            var sequence = new byte[] { 0, 2, 0, 2 };
            Assert.AreEqual (-1, array.IndexOfSequence (sequence, 0, array.Length - 1));
        }

        [Test]
        public void SequenceAtBeginning ()
        {
            var array = new byte[] { 0, 1, 0, 1, 0, 2, 0, 2 };
            var sequence = new byte[] { 0, 1, 0, 1 };
            Assert.AreEqual (0, array.IndexOfSequence (sequence));
        }

        [Test]
        public void SequenceAtBeginning_SourceTruncated ()
        {
            var array = new byte[] { 0, 1, 0, 1, 0, 2, 0, 2 };
            var sequence = new byte[] { 0, 1, 0, 1 };
            Assert.AreEqual (-1, array.IndexOfSequence (sequence, 1, array.Length - 1));
        }

        [Test]
        public void SequenceMissing ()
        {
            var array = new byte[] { 0, 1, 0, 1, 0, 2, 0, 2 };
            var sequence = new byte[] { 0, 2, 0, 3 };
            Assert.AreEqual (-1, array.IndexOfSequence (sequence));
        }

        [Test]
        public void SequenceLongerThanSource ()
        {
            var array = new byte[] { 0, 2, 0 };
            var sequence = new byte[] { 0, 2, 0, 3 };
            Assert.AreEqual (-1, array.IndexOfSequence (sequence));
        }
    }
}
