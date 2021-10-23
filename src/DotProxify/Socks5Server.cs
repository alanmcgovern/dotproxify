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
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

using ReusableTasks;

namespace DotProxify
{
    enum AddressType : byte
    {
        IPV4 = 1,
        DomainName = 3,
        IPV6 = 4,
    }

    enum AuthMethod : byte
    {
        Unauthenticated = 0,
        UsernamePassword = 2,
    }

    enum Command : byte
    {
        Connect = 1,
        Bind = 2,
        UdpAssociate = 3,
    }

    enum ReplyStatus : byte
    {
        Succeeded = 0,
        GeneralServerFail = 1,
        ConnectionNotAllowed = 2,
        NetworkUnreachable = 3,
        HostUnreachable = 4,
        ConnectionRefused = 5,
        TTLExpired = 6,
        CommandNotSupported = 7,
        AddressTypeNotSupported = 8,
        Unassigned = 9,
    }

    public class Socks5Server
    {
        const byte Socks5ServerVersion = 5;

        IPEndPoint ServerEndPoint { get; }

        (byte[] Username, byte[] Password)? UserPass { get; }

        public Socks5Server (IPEndPoint endPoint)
            : this (endPoint, Array.Empty<byte> (), Array.Empty<byte> ())
        {
        }

        public Socks5Server (IPEndPoint endPoint, byte[] username, byte[] password)
        {
            ServerEndPoint = endPoint;
            if (username.Length > 0 || password.Length > 0)
                UserPass = (username, password);
        }

        public async ReusableTask<Socket> ConnectTcp (IPEndPoint clientEndPoint)
            => (await Connect (Command.Connect, clientEndPoint, null)).socket;

        public async ReusableTask<Socket> ConnectTcp (string domainName, int port)
            => (await Connect (Command.Connect, null, (domainName, port))).socket;

        public async ReusableTask<(Socket controlSocket, IPEndPoint relayEndpoint)> ConnectUdp (IPEndPoint clientEndPoint)
            => await Connect (Command.UdpAssociate, clientEndPoint, null);

        async ReusableTask<(Socket socket, IPEndPoint relayEndPoint)> Connect (Command command, IPEndPoint? endPoint, (string domainName, int port)? domain)
        {
            var socket = new Socket (ServerEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try {
                await ConnectToSocksServer (socket);
                await Authenticate (socket);
                var relayEndPoint = await ProxyTo (socket, command, endPoint, domain);
                return (socket, relayEndPoint);
            } catch {
                socket.Dispose ();
                throw;
            }
        }

        async ReusableTask ConnectToSocksServer (Socket socket)
        {
            // Connect to the SOCKS5 server
            await Task.Factory.FromAsync (socket.BeginConnect (ServerEndPoint, null, null), socket.EndConnect);
        }

        async ReusableTask Authenticate (Socket socket)
        {
            var authMethod = UserPass == null ? AuthMethod.Unauthenticated : AuthMethod.UsernamePassword;
            var buffer = new byte[3];
            var offset = 0;

            // Prepare the handshake message
            buffer[offset++] = Socks5ServerVersion; // ver
            buffer[offset++] = 1; // number of authentication methods
            buffer[offset++] = (byte) authMethod; // authentication method(s)
            if (await Task.Factory.FromAsync (socket.BeginSend (buffer, 0, offset, SocketFlags.None, null, null), socket.EndSend) != offset)
                throw new InvalidOperationException ("Failed to send handshake to server");

            // Receive the response specifying which auth method to use
            if (await Task.Factory.FromAsync (socket.BeginReceive (buffer, 0, 2, SocketFlags.None, null, null), socket.EndReceive) != 2)
                throw new InvalidOperationException ("Failed to receive authentication selection from server");

            CheckVersion (buffer[0]);

            if (buffer[1] != (byte) authMethod)
                throw new InvalidOperationException ($"Server does not accept {(AuthMethod) authMethod} connections");

            if (UserPass != null) {
                var userPass = UserPass.Value;
                buffer = new byte[1 + 1 + userPass.Username.Length + 1 + userPass.Password.Length];

                offset = 0;
                buffer[offset++] = 1; // version
                buffer[offset++] = (byte) userPass.Username.Length;
                Buffer.BlockCopy (userPass.Username, 0, buffer, offset, buffer.Length);
                offset += buffer.Length;
                buffer[offset++] = (byte) userPass.Password.Length;
                Buffer.BlockCopy (userPass.Password, 0, buffer, offset, userPass.Password.Length);
                offset += userPass.Password.Length;

                if (await Task.Factory.FromAsync (socket.BeginSend (buffer, 0, offset, SocketFlags.None, null, null), socket.EndSend) != offset)
                    throw new InvalidOperationException ("Failed to send username/password to server");

                if (await Task.Factory.FromAsync (socket.BeginReceive (buffer, 0, 2, SocketFlags.None, null, null), socket.EndReceive) != 2)
                    throw new InvalidOperationException ("Failed to send username/password to server");
                if (buffer[0] != 1)
                    throw new InvalidOperationException ("Server authentication login had an unexpected version number");
                if (buffer[1] != 0)
                    throw new InvalidOperationException ("Server rejected the authentication attempt");
            }
        }

        async ReusableTask<IPEndPoint> ProxyTo (Socket socket, Command command, IPEndPoint? clientEndPoint, (string DomainName, int Port)? domain)
        {
            // Proxy through to the other side
            byte[] addressBytes;
            byte[] portBytes;
            AddressType addressType;
            if (clientEndPoint != null) {
                addressType = clientEndPoint.AddressFamily == AddressFamily.InterNetwork ? AddressType.IPV4 : AddressType.IPV6;
                addressBytes = clientEndPoint.Address.GetAddressBytes ();
                portBytes = BitConverter.GetBytes (IPAddress.HostToNetworkOrder ((short) clientEndPoint.Port));
            } else {
                addressType = AddressType.DomainName;
                addressBytes = System.Text.Encoding.UTF8.GetBytes (domain!.Value.DomainName);
                portBytes = BitConverter.GetBytes (IPAddress.HostToNetworkOrder ((short) domain!.Value.Port));
            }

            var buffer = new byte[1 + 1 + 1 + 1 + (addressType == AddressType.DomainName ? 1 : 0) + addressBytes.Length + 2];

            var offset = 0;
            buffer[offset++] = Socks5ServerVersion;   // version
            buffer[offset++] = (byte) command; //command
            buffer[offset++] = 0;                     // reserved
            buffer[offset++] = (byte) addressType;     // address type
            if (addressType == AddressType.DomainName)
                buffer[offset++] = (byte) addressBytes.Length;
            Buffer.BlockCopy (addressBytes, 0, buffer, offset, addressBytes.Length);
            offset += addressBytes.Length;
            Buffer.BlockCopy (portBytes, 0, buffer, offset, portBytes.Length);
            offset += portBytes.Length;

            if (await Task.Factory.FromAsync (socket.BeginSend (buffer, 0, offset, SocketFlags.None, null, null), socket.EndSend) != offset)
                throw new InvalidOperationException ("Connection closed by server before the proxied connection could be initiated");

            // Read the first part of the response, including address type specifier
            buffer = new byte[1 + 1 + 1 + 1];
            if (await Task.Factory.FromAsync (socket.BeginReceive (buffer, 0, buffer.Length, SocketFlags.None, null, null), socket.EndReceive) != buffer.Length)
                throw new InvalidOperationException ("Connection closed by server before the proxied connection was established");

            CheckVersion (buffer[0]);
            if (buffer[1] != 0)
                throw new InvalidOperationException ($"Server failed to establish the requested connection. Error was {(ReplyStatus) buffer[1]}");

            var boundAddressLength = (AddressType) buffer[3] switch {
                AddressType.IPV4 => 4,
                AddressType.IPV6 => 16,
                _ => throw new InvalidOperationException ("Unsupported bound address type")
            };

            // Address and port
            buffer = new byte[boundAddressLength];
            if (await Task.Factory.FromAsync (socket.BeginReceive (buffer, 0, buffer.Length, SocketFlags.None, null, null), socket.EndReceive) != buffer.Length)
                throw new InvalidOperationException ("Connection closed by server before the proxied connection was established");

            var relayAddress = new IPAddress (buffer);
            if (await Task.Factory.FromAsync (socket.BeginReceive (buffer, 0, 2, SocketFlags.None, null, null), socket.EndReceive) != 2)
                throw new InvalidOperationException ("Connection closed by server before the proxied connection was established");

            var relayPort = (ushort) IPAddress.NetworkToHostOrder ((short) BitConverter.ToUInt16 (buffer, 0));
            return new IPEndPoint (relayAddress, relayPort);
        }

        static void CheckVersion (byte version)
        {
            if (version != Socks5ServerVersion)
                throw new InvalidOperationException ("Invalid protocol version received");
        }
    }
}
