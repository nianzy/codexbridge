using System.Buffers.Binary;
using System.Text.Json;
using CodexBridge.Core.Capture;

namespace CodexBridge.NativeHost;

public static class NativeMessagingProtocol
{
    public const int MaximumMessageBytes = CaptureValidator.MaximumPayloadBytes + 100_000;

    public static byte[]? ReadMessage(Stream input, int maximumMessageBytes = MaximumMessageBytes)
    {
        Span<byte> header = stackalloc byte[sizeof(uint)];
        var firstByteCount = input.Read(header[..1]);
        if (firstByteCount == 0)
        {
            return null;
        }

        ReadExactly(input, header[1..]);
        var messageLength = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (messageLength == 0 || messageLength > maximumMessageBytes)
        {
            throw new InvalidDataException($"Native Messaging message length is invalid: {messageLength}.");
        }

        var message = new byte[checked((int)messageLength)];
        ReadExactly(input, message);
        return message;
    }

    public static void WriteMessage<T>(Stream output, T message, int maximumMessageBytes = MaximumMessageBytes)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, CaptureJson.SerializerOptions);
        if (payload.Length == 0 || payload.Length > maximumMessageBytes)
        {
            throw new InvalidDataException($"Native Messaging response length is invalid: {payload.Length}.");
        }

        Span<byte> header = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(header, checked((uint)payload.Length));
        output.Write(header);
        output.Write(payload);
        output.Flush();
    }

    private static void ReadExactly(Stream input, Span<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            var read = input.Read(buffer);
            if (read == 0)
            {
                throw new EndOfStreamException("Native Messaging message ended before the declared length.");
            }

            buffer = buffer[read..];
        }
    }
}
