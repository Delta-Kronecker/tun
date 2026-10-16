using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Se7enTun.Tests;

public sealed class SocksTcpForwarderTests
{
    [Fact]
    public async Task Relays_tcp_bytes_to_the_upstream_and_back()
    {
        var echo = new TcpListener(IPAddress.Loopback, 0);
        echo.Start();
        var echoPort = ((IPEndPoint)echo.LocalEndpoint).Port;

        var server = Task.Run(async () =>
        {
            using var accepted = await echo.AcceptTcpClientAsync();
            var stream = accepted.GetStream();
            var buffer = new byte[256];
            var read = await stream.ReadAsync(buffer);
            if (read > 0) await stream.WriteAsync(buffer.AsMemory(0, read));
        });

        await using var forwarder = new SocksTcpForwarder(IPAddress.Loopback.ToString(), echoPort, 0);
        forwarder.Start();
        Assert.True(forwarder.LocalPort > 0);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, forwarder.LocalPort)
                .WaitAsync(TimeSpan.FromSeconds(10));

            var stream = client.GetStream();
            var payload = Encoding.ASCII.GetBytes("hello-tun");
            await stream.WriteAsync(payload);

            var reply = new byte[payload.Length];
            var offset = 0;
            while (offset < reply.Length)
            {
                var read = await stream.ReadAsync(reply.AsMemory(offset))
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True(read > 0, "upstream closed before the reply was complete");
                offset += read;
            }

            Assert.Equal(payload, reply);
            await server.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, forwarder.ConnectionsServed);
        }
        finally
        {
            echo.Stop();
        }
    }

    [Fact]
    public void Constructor_validates_its_endpoints()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SocksTcpForwarder("203.0.113.10", 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SocksTcpForwarder("203.0.113.10", 1080, 70000));
        Assert.Throws<ArgumentException>(
            () => new SocksTcpForwarder("", 1080, 0));
    }
}
