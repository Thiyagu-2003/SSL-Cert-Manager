using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CertManager.Infrastructure.Ipc;

public static class IpcMessageFraming
{
    public const int MaxPayloadBytes = 4 * 1024 * 1024; // 4 MB safety limit
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task WriteMessageAsync<T>(Stream stream, T message, CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (payload.Length > MaxPayloadBytes)
        {
            throw new InvalidDataException($"Message payload size ({payload.Length} bytes) exceeds maximum allowed size ({MaxPayloadBytes} bytes).");
        }

        byte[] lengthHeader = BitConverter.GetBytes(payload.Length);
        await stream.WriteAsync(lengthHeader, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<T?> ReadMessageAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        byte[] lengthHeader = new byte[4];
        int bytesRead = await ReadExactBytesAsync(stream, lengthHeader, cancellationToken);
        if (bytesRead == 0)
        {
            return default;
        }

        int payloadLength = BitConverter.ToInt32(lengthHeader, 0);
        if (payloadLength < 0 || payloadLength > MaxPayloadBytes)
        {
            throw new InvalidDataException($"Message payload size ({payloadLength} bytes) exceeds maximum allowed size ({MaxPayloadBytes} bytes).");
        }

        byte[] payload = new byte[payloadLength];
        await ReadExactBytesAsync(stream, payload, cancellationToken);

        return JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }

    private static async Task<int> ReadExactBytesAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken);
            if (read == 0)
            {
                if (totalRead == 0) return 0;
                throw new EndOfStreamException($"Stream closed prematurely after reading {totalRead} of {buffer.Length} bytes.");
            }
            totalRead += read;
        }
        return totalRead;
    }
}
