using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using CodexBridge.Core.Capture;
using CodexBridge.NativeHost;

var tests = new (string Name, Action Test)[]
{
    ("synthetic fixture deserializes", SyntheticFixtureDeserializes),
    ("valid payload validates", ValidPayloadValidates),
    ("schema version mismatch fails", SchemaVersionMismatchFails),
    ("duplicate message ID fails", DuplicateMessageIdFails),
    ("selection mismatch fails", SelectionMismatchFails),
    ("oversized payload fails", OversizedPayloadFails),
    ("Native Messaging framing round-trips", NativeMessagingFramingRoundTrips),
    ("capture.import stores a valid payload", CaptureImportStoresValidPayload),
    ("unsupported Native Messaging request fails", UnsupportedNativeRequestFails),
};

var failures = 0;
foreach (var (name, test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

Console.WriteLine($"Tests: {tests.Length - failures} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

static CapturePayload LoadFixture()
{
    var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "synthetic-capture-v1.json");
    return CaptureJson.Deserialize(File.ReadAllBytes(path));
}

static void SyntheticFixtureDeserializes()
{
    var payload = LoadFixture();
    Assert(payload.SchemaVersion == 1, "schemaVersion should be 1");
    Assert(payload.Source.Kind == "chatgpt-web", "fixture source kind mismatch");
    Assert(payload.Turns.Count == 1, "fixture should contain one turn");
}

static void ValidPayloadValidates()
{
    var result = new CaptureValidator().Validate(File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "fixtures", "synthetic-capture-v1.json")));
    Assert(result.ContentHash.Length == 64, "content hash must be SHA-256 hex");
    Assert(result.ConversationId != Guid.Empty, "conversation ID must be generated");
}

static void SchemaVersionMismatchFails()
{
    var payload = LoadFixture() with { SchemaVersion = 2 };
    AssertValidationError(payload, CaptureValidationErrorCode.UnsupportedVersion);
}

static void DuplicateMessageIdFails()
{
    var payload = LoadFixture();
    var turn = payload.Turns[0];
    var duplicateAssistant = turn.Assistant! with { Id = turn.User.Id };
    var turns = payload.Turns.ToList();
    turns[0] = turn with { Assistant = duplicateAssistant };
    AssertValidationError(payload with { Turns = turns }, CaptureValidationErrorCode.DuplicateMessageId);
}

static void SelectionMismatchFails()
{
    var payload = LoadFixture() with
    {
        Selection = new CaptureSelection { SelectedTurnIds = new List<string> { "missing-turn" } },
    };
    AssertValidationError(payload, CaptureValidationErrorCode.InvalidSelection);
}

static void OversizedPayloadFails()
{
    var bytes = new byte[CaptureValidator.MaximumPayloadBytes + 1];
    AssertThrows<CaptureValidationException>(
        () => new CaptureValidator().Validate(bytes),
        exception => exception.Code == CaptureValidationErrorCode.Oversized);
}

static void NativeMessagingFramingRoundTrips()
{
    var expected = NativeResponse.Success(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));
    using var stream = new MemoryStream();
    NativeMessagingProtocol.WriteMessage(stream, expected);
    var bytes = stream.ToArray();
    var declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4));
    Assert(declaredLength == bytes.Length - 4, "Native Messaging length prefix mismatch");

    stream.Position = 0;
    var decodedBytes = NativeMessagingProtocol.ReadMessage(stream);
    Assert(decodedBytes is not null, "framing decoder returned no payload");
    var decoded = JsonSerializer.Deserialize<NativeResponse>(
        decodedBytes!,
        CaptureJson.SerializerOptions);
    Assert(decoded?.Ok == expected.Ok, "framing response ok mismatch");
    Assert(decoded?.ConversationId == expected.ConversationId, "framing conversation ID mismatch");
}

static void UnsupportedNativeRequestFails()
{
    var handler = new NativeRequestHandler(
        inbox: new CaptureInbox(Path.Combine(Path.GetTempPath(), $"codexbridge-test-{Guid.NewGuid():D}")));
    var request = Encoding.UTF8.GetBytes("{\"type\":\"not-supported\",\"payload\":null}");
    var response = handler.Handle(request);
    Assert(!response.Ok, "unsupported request should fail");
    Assert(response.ConversationId is null, "failed request must not return conversation ID");
}

static void CaptureImportStoresValidPayload()
{
    var directory = Path.Combine(Path.GetTempPath(), $"codexbridge-test-{Guid.NewGuid():D}");
    try
    {
        var handler = new NativeRequestHandler(inbox: new CaptureInbox(directory));
        var request = JsonSerializer.SerializeToUtf8Bytes(
            new NativeMessage { Type = "capture.import", Payload = LoadFixture() },
            CaptureJson.SerializerOptions);

        var response = handler.Handle(request);
        Assert(response.Ok, "capture.import should succeed for the fixture");
        Assert(response.ConversationId is not null, "successful import must return a conversation ID");

        var storedFiles = Directory.GetFiles(directory, "*.json");
        Assert(storedFiles.Length == 1, "capture.import should write one inbox file");
        var stored = new CaptureValidator().Validate(File.ReadAllBytes(storedFiles[0]));
        Assert(stored.ConversationId.ToString("D").ToUpperInvariant() == response.ConversationId,
            "inbox payload and response conversation IDs must match");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static void AssertValidationError(CapturePayload payload, CaptureValidationErrorCode expected)
{
    AssertThrows<CaptureValidationException>(
        () => new CaptureValidator().Validate(payload),
        exception => exception.Code == expected);
}

static void AssertThrows<TException>(Action action, Func<TException, bool> predicate)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException exception) when (predicate(exception))
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
