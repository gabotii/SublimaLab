using System.Collections.Concurrent;

namespace SublimaLab.Blazor.Services;

/// <summary>
/// Hands API responses from the server-side prerender to the WebAssembly app. While the server
/// prerenders a page it records every GET response; they are embedded in the page (persistent
/// component state) and the browser replays each one once instead of fetching it again, so the
/// prerendered content doesn't flash back to a loading state when WebAssembly takes over.
/// </summary>
public sealed class PrerenderCache(bool isRecording = false)
{
    public const string StateKey = "sl-api-responses";

    private readonly ConcurrentDictionary<string, string> _responses = new(StringComparer.Ordinal);

    /// <summary>True on the server while prerendering.</summary>
    public bool IsRecording { get; } = isRecording;

    public void Record(string url, string json)
    {
        if (IsRecording)
        {
            _responses[url] = json;
        }
    }

    /// <summary>Returns a handed-off response once; later calls go to the network.</summary>
    public bool TryTake(string url, out string json) => _responses.TryRemove(url, out json!);

    public Dictionary<string, string> Snapshot() => new(_responses, StringComparer.Ordinal);

    public void Load(IReadOnlyDictionary<string, string> responses)
    {
        foreach (var (url, json) in responses)
        {
            _responses[url] = json;
        }
    }
}
