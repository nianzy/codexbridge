using CodexBridge.NativeHost;

var input = Console.OpenStandardInput();
var output = Console.OpenStandardOutput();
var handler = new NativeRequestHandler();

try
{
    var request = NativeMessagingProtocol.ReadMessage(input);
    if (request is null)
    {
        return;
    }

    var response = handler.Handle(request);
    NativeMessagingProtocol.WriteMessage(output, response);
    if (!response.Ok && response.Error is not null)
    {
        Console.Error.WriteLine(response.Error);
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Codex Bridge Native Host error: {exception.Message}");
    try
    {
        NativeMessagingProtocol.WriteMessage(
            output,
            NativeResponse.Failure("Codex Bridge Native Host could not process the request."));
    }
    catch (Exception responseException)
    {
        Console.Error.WriteLine($"Codex Bridge Native Host response error: {responseException.Message}");
    }
}
