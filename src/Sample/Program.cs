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

using System.Net;
using System.Text;
using DotProxify;

var server = new Socks5Server(new IPEndPoint (IPAddress.Loopback, 1080));
var webProxy = new HttpToSocksProxy (server);

webProxy.Start ();

foreach (var url in new[] { "https://www.google.com", "http://www.google.com", "https://google.com", "http://google.com" }) {
    var request = WebRequest.CreateHttp (url);
    request.Proxy = webProxy;
    Console.WriteLine ("WebRequest: " + url);
    try {
        var response = request.GetResponse ();
        var bytes = new byte[10240];

        while (true) {
            int read = response.GetResponseStream ().Read (bytes);
            if (read == 0)
                break;
            Console.Write (Encoding.UTF8.GetString (bytes, 0, read));
        }
    } catch {
        Console.WriteLine ("WebRequest failed: " + url);
    }
    Console.WriteLine ();
    Console.WriteLine ();
}


var httpClient = new HttpClient (new HttpClientHandler { Proxy = webProxy, UseProxy = true });
foreach (var url in new[] { "https://www.google.com", "http://www.google.com", "https://google.com", "http://google.com" }) {
    Console.WriteLine ("HttpClient: " + url);
    Console.WriteLine (await httpClient.GetStringAsync (url));
    Console.WriteLine ();
}
