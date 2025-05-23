using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;

public static class ProxyHandler
{
    public static async Task HandleClientAsync(TcpClient client)
    {
        using var clientStream = client.GetStream();
        using var reader = new StreamReader(clientStream, leaveOpen: true);
        using var writer = new StreamWriter(clientStream, leaveOpen: true) { AutoFlush = true };

        string requestLine = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(requestLine)) return;

        //Console.WriteLine($"[DEBUG] {requestLine}");

        if (requestLine.StartsWith("CONNECT"))
        {
            var parts = requestLine.Split(' ');
            var hostPort = parts[1].Split(':');
            var hostname = hostPort[0];
            var port = int.Parse(hostPort[1]);

            await writer.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n");

            await HandleHttpsTunnelAsync(clientStream, hostname, port);
        }
        else
        {
            await HandleHttpRequestAsync(requestLine, reader, writer);
        }
    }

    private static async Task HandleHttpRequestAsync(string requestLine, StreamReader clientReader, StreamWriter clientWriter)
    {
        var parts = requestLine.Split(' ');
        if (parts.Length < 3 || !Uri.TryCreate(parts[1], UriKind.Absolute, out var uri))
        {
            await clientWriter.WriteAsync("HTTP/1.1 400 Bad Request\r\n\r\n");
            return;
        }

        string hostname = uri.Host;
        int port = uri.Port > 0 ? uri.Port : 80;

        using var serverTcp = new TcpClient();
        await serverTcp.ConnectAsync(hostname, port);
        using var serverStream = serverTcp.GetStream();

        var headersBuilder = new StringBuilder();
        string line;
        while (!string.IsNullOrEmpty(line = await clientReader.ReadLineAsync()))
        {
            if (line.StartsWith("Proxy-Connection:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase))
                continue;

            headersBuilder.AppendLine(line);
        }

        var requestBuilder = new StringBuilder();
        requestBuilder.AppendLine($"{parts[0]} {uri.PathAndQuery} {parts[2]}");
        requestBuilder.Append(headersBuilder.ToString());
        requestBuilder.AppendLine();

        var requestBytes = Encoding.ASCII.GetBytes(requestBuilder.ToString());
        await serverStream.WriteAsync(requestBytes, 0, requestBytes.Length);

        var clientStream = clientReader.BaseStream;

        var clientToServer = RelayStreamAsync(clientStream, serverStream);
        var serverToClient = RelayStreamAsync(serverStream, clientStream);

        await Task.WhenAny(clientToServer, serverToClient);
    }

    private static async Task RelayStreamAsync(Stream input, Stream output)
    {
        var buffer = new byte[8192];
        try
        {
            int bytesRead;
            while ((bytesRead = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await output.WriteAsync(buffer, 0, bytesRead);
                await output.FlushAsync();
            }
        }
        catch
        {
            // Ignore any unexpected error
        }
    }


    private static async Task HandleHttpsTunnelAsync(NetworkStream clientStream, string hostname, int port)
    {
        var cert = CertificateAuthority.GenerateCertificateForHost(hostname);
        //  Console.WriteLine($"HasPrivateKey = {cert.HasPrivateKey}");

        var sslClientStream = new SslStream(clientStream, leaveInnerStreamOpen: false);

        try
        {
            await sslClientStream.AuthenticateAsServerAsync(
                cert,
                clientCertificateRequired: false,
                enabledSslProtocols: SslProtocols.Tls12 | SslProtocols.Tls13,
                checkCertificateRevocation: false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TLS client error: {ex}");
            return;
        }

        using var serverTcp = new TcpClient();
        await serverTcp.ConnectAsync(hostname, port);

        using var sslServerStream = new SslStream(serverTcp.GetStream(), false,
            (sender, certificate, chain, errors) => true);

        try
        {
            await sslServerStream.AuthenticateAsClientAsync(hostname);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TLS server error: {ex}");
            return;
        }

        await RelayAndInterceptAsync(sslClientStream, sslServerStream);
    }

    private static async Task RelayAndInterceptAsync(Stream clientStream, Stream serverStream)
    {
        var clientToServer = CopyStreamWithLoggingAsync(clientStream, serverStream, "REQUEST");
        var serverToClient = CopyStreamWithLoggingAsync(serverStream, clientStream, "RESPONSE");

        await Task.WhenAny(clientToServer, serverToClient);
    }

    private static async Task CopyStreamWithLoggingAsync(Stream input, Stream output, string direction)
    {
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            try
            {
                var text = System.Text.Encoding.ASCII.GetString(buffer, 0, read);
                // Console.WriteLine($"[DEBUG] [{direction}] {read} bytes : {text}");
                Logger.Log($"[{direction}] {read} bytes : {text}");
            }
            catch { }

            await output.WriteAsync(buffer, 0, read);
            await output.FlushAsync();
        }
    }
}
