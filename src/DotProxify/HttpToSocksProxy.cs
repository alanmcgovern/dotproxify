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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using ReusableTasks;

namespace DotProxify
{
    public class HttpToSocksProxy : IWebProxy
    {
        CancellationTokenSource Cancellation { get; set; }

        ICredentials? IWebProxy.Credentials { get; set; }

        EndPoint? LocalInterceptEndPoint { get; set; }

        Socks5Server SocksServer { get; }

        public HttpToSocksProxy (Socks5Server server)
        {
            Cancellation = new CancellationTokenSource ();
            SocksServer = server;
        }

        public void Start ()
        {
            var listener = new Socket (AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind (new IPEndPoint (IPAddress.Loopback, 0));
            listener.Listen (10);
            LocalInterceptEndPoint = listener.LocalEndPoint;

            Cancellation.Cancel ();
            Cancellation = new CancellationTokenSource ();
            Cancellation.Token.Register (listener.Dispose);

            _ = AcceptAsync (listener);
        }

        public void Stop ()
        {
            Cancellation.Cancel ();
            LocalInterceptEndPoint = null;
        }

        async ReusableTask AcceptAsync (Socket listener)
        {
            while (true) {
                try {
                    var localSocket = await Task.Factory.FromAsync (listener.BeginAccept (null, null), listener.EndAccept);
                    EstablishProxy (localSocket);
                } catch {
                    // stuff
                }
            }
        }

        async void EstablishProxy (Socket local)
        {
            var buffer = new byte[16 * 1024];
            var doubleNewline = new[] { (byte) '\r', (byte) '\n', (byte) '\r', (byte) '\n' };
            Socket? proxiedConnection = null;

            try {
                // keep reading until we have the 'CONNECT' request. Depending on the implementation, we may also over-read
                // and have excess data. If we don't have the metadata we need within 16kB,it probably means something bad has occurred.
                int totalRead = 0;
                int headerEndIndex = -1;
                while (totalRead < buffer.Length && headerEndIndex == -1) {
                    var read = await Task.Factory.FromAsync (local.BeginReceive (buffer, totalRead, buffer.Length - totalRead, SocketFlags.None, null, null), local.EndReceive);
                    if (read == 0)
                        throw new InvalidOperationException ("couldn't read CONNECT request");
                    totalRead += read;
                    headerEndIndex = buffer.IndexOfSequence (doubleNewline, 0, totalRead);
                }

                if (headerEndIndex == -1)
                    throw new InvalidOperationException ("HeaderEnd");

                var lines = Encoding.ASCII.GetString (buffer, 0, headerEndIndex).Split (new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                bool useConnectProxy = UseConnectProxy (lines, out string host, out int port, out string httpVersion);
                try {
                    proxiedConnection = await SocksServer.ConnectTcp (host, port);
                } catch {
                    var response = Encoding.ASCII.GetBytes ($"{httpVersion} 503 Service Unavailable\r\n\r\n");
                    await Task.Factory.FromAsync (local.BeginSend (response, 0, response.Length, SocketFlags.None, null, null), local.EndSend);
                    throw;
                }

                if (useConnectProxy) {
                    // If this is a CONNECT then we need to inform the local socket that it can proceed.
                    var response = Encoding.ASCII.GetBytes ($"{httpVersion} 200 Connection established\r\n\r\n");
                    await Task.Factory.FromAsync (local.BeginSend (response, 0, response.Length, SocketFlags.None, null, null), local.EndSend);

                    // Forward on any data we may have overread
                    if (totalRead > (headerEndIndex + doubleNewline.Length))
                        await Task.Factory.FromAsync (proxiedConnection.BeginSend (buffer, headerEndIndex + doubleNewline.Length, totalRead - headerEndIndex - doubleNewline.Length, SocketFlags.None, null, null), proxiedConnection.EndSend);
                } else {
                    // Otherwise directly proxy traffic to/from.
                    await Task.Factory.FromAsync (proxiedConnection.BeginSend (buffer, 0, totalRead, SocketFlags.None, null, null), proxiedConnection.EndSend);
                }

                // Keep proxying
                var tasks = new[] {
                    TransferData (local, proxiedConnection, buffer).AsTask (),
                    TransferData (proxiedConnection, local, new byte[16 * 1024]).AsTask ()
                };
                await Task.WhenAny (tasks);
                proxiedConnection.Shutdown (SocketShutdown.Both);
                local.Shutdown (SocketShutdown.Both);
                proxiedConnection.Dispose ();
                await Task.WhenAll (tasks);
            } catch {
                // stuff
            } finally {
                if (proxiedConnection != null)
                    proxiedConnection.Dispose ();
                local.Dispose ();
            }
        }

        private bool UseConnectProxy (string[] lines, out string host, out int port, out string httpVersion)
        {
            var hostLine = lines.First (t => t.StartsWith ("Host: ", StringComparison.OrdinalIgnoreCase));
            var hostParts = hostLine.Split (' ')[1].Split (':');
            host = hostParts[0];
            port = hostParts.Length == 2 ? int.Parse (hostParts[1]) : 80;

            var requestParts = lines[0].Split (' ');

            var hostUri = requestParts[1];
            if (hostUri.StartsWith ("http://", StringComparison.Ordinal)) {
                host = new Uri (hostUri).Host;
                port = new Uri (hostUri).Port;
            }

            httpVersion = requestParts[2];
            return StringComparer.OrdinalIgnoreCase.Compare (requestParts[0], "connect") == 0;
        }

        async ReusableTask TransferData (Socket from, Socket to, byte[] buffer)
        {
            int read = 0;
            while ((read = await Task.Factory.FromAsync (from.BeginReceive (buffer, 0, buffer.Length, SocketFlags.None, null, null), from.EndReceive)) > 0)
                await Task.Factory.FromAsync (to.BeginSend (buffer, 0, read, SocketFlags.None, null, null), to.EndSend);
        }

        Uri IWebProxy.GetProxy (Uri destination)
        {
            if (LocalInterceptEndPoint == null)
                throw new InvalidOperationException ("You must call 'Start' on this object before using it");
            return new Uri ($"http://{LocalInterceptEndPoint}");
        }

        bool IWebProxy.IsBypassed (Uri host)
            => false;
    }
}
