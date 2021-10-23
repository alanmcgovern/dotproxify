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

using System;

namespace DotProxify
{
    static class ArrayExtensions
    {
        public static int IndexOfSequence (this byte[] array, byte[] sequence)
            => array.IndexOfSequence (sequence, 0, array.Length);

        public static int IndexOfSequence (this byte[] array, byte[] sequence, int startIndex, int count)
        {
            if (sequence.Length == 0)
                throw new ArgumentException ("Sequence length must be greater than zero", nameof (sequence));

            int sequenceStart = Array.IndexOf (array, sequence[0], startIndex, count);
            while (sequenceStart != -1 && sequenceStart + sequence.Length <= startIndex + count) {
                var found = true;
                for (int i = 1; i < sequence.Length; i++) {
                    if (array[sequenceStart + i] != sequence[i]) {
                        found = false;
                        break;
                    }
                }
                if (found)
                    return sequenceStart;
                sequenceStart = Array.IndexOf (array, sequence[0], sequenceStart + 1);
            }
            return -1;
        }
    }
}
