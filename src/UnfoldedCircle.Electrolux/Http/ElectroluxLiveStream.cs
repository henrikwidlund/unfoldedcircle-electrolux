namespace UnfoldedCircle.Electrolux.Http;

public sealed class ElectroluxLiveStream(HttpResponseMessage httpResponseMessage) : IDisposable
{
    private readonly HttpResponseMessage _httpResponseMessage = httpResponseMessage;

    public Task<Stream> GetStreamAsync(CancellationToken cancellationToken) => _httpResponseMessage.Content.ReadAsStreamAsync(cancellationToken);

    public void Dispose() => _httpResponseMessage.Dispose();
}