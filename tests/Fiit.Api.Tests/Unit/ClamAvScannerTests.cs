using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Fiit.Api.Services;
using Microsoft.Extensions.Options;

namespace Fiit.Api.Tests.Unit;

public class ClamAvScannerTests
{
    [Theory]
    [InlineData("stream: OK\0", true, null)]
    [InlineData("stream: Win.Test.EICAR_HDB-1 FOUND\0", false, "Win.Test.EICAR_HDB-1")]
    public void Parses_reply(string reply, bool clean, string? threat)
    {
        var result = ClamAvScanner.Parse(reply);
        Assert.Equal(clean, result.Clean);
        Assert.Equal(threat, result.Threat);
    }

    [Fact]
    public void Error_reply_means_unavailable() =>
        Assert.Throws<MalwareScanUnavailableException>(() => ClamAvScanner.Parse("INSTREAM size limit exceeded. ERROR\0"));

    [Fact]
    public async Task Sends_file_in_chunks_and_reads_verdict()
    {
        var content = new byte[150_000];
        Random.Shared.NextBytes(content);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = FakeClamdAsync(listener, "stream: Eicar FOUND\0");

        var result = await Scanner(((IPEndPoint)listener.LocalEndpoint).Port).ScanAsync(content, CancellationToken.None);

        Assert.Equal(new ScanResult(false, "Eicar"), result);
        var (command, received) = await server;
        Assert.Equal("zINSTREAM", command);
        Assert.Equal(content, received);
    }

    [Fact]
    public async Task Closed_port_means_unavailable()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        await Assert.ThrowsAsync<MalwareScanUnavailableException>(() => Scanner(port).ScanAsync([1, 2, 3], CancellationToken.None));
    }

    private static ClamAvScanner Scanner(int port) =>
        new(Options.Create(new ClamAvOptions { Enabled = true, Host = "127.0.0.1", Port = port, Timeout = TimeSpan.FromSeconds(5) }));

    /// <summary>Принимает одно соединение по протоколу INSTREAM и отвечает заданной строкой.</summary>
    private static async Task<(string Command, byte[] Content)> FakeClamdAsync(TcpListener listener, string reply)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        var command = new StringBuilder();
        int b;
        while ((b = stream.ReadByte()) > 0)
            command.Append((char)b);

        using var content = new MemoryStream();
        var length = new byte[4];
        while (true)
        {
            await stream.ReadExactlyAsync(length);
            var size = BinaryPrimitives.ReadInt32BigEndian(length);
            if (size == 0)
                break;
            var chunk = new byte[size];
            await stream.ReadExactlyAsync(chunk);
            content.Write(chunk);
        }
        await stream.WriteAsync(Encoding.ASCII.GetBytes(reply));
        return (command.ToString(), content.ToArray());
    }
}
