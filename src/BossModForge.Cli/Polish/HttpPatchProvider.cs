using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BossModForge.Core.Polish;

namespace BossModForge.Cli.Polish;

public sealed class HttpPatchProvider : IPolishProvider
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string? _apiKey;
    private readonly string? _model;

    public HttpPatchProvider(string endpoint, string? apiKey, string? model)
    {
        _endpoint = endpoint;
        _apiKey = apiKey;
        _model = model;
        _http = new HttpClient { Timeout = RequestTimeout };
    }

    public string Name => "http";

    public JsonElement Complete(JsonObject prompt)
    {
        var body = new JsonObject { ["prompt"] = prompt };
        if (!string.IsNullOrWhiteSpace(_model))
            body["model"] = _model;

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrEmpty(_apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = _http.Send(request);
        var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"polish endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}: {text}");
        }

        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }
}
