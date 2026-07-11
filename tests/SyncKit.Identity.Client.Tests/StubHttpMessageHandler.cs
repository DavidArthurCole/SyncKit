namespace SyncKit.Identity.Client.Tests;

// Canonical stub for outbound-HTTP tests in this solution: hands back a caller-supplied
// response and records the last request so assertions can check method/path/headers/body.
public sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler {
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        LastRequest = request;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return respond(request);
    }
}
