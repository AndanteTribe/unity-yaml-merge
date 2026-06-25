using System.Net;
using System.Xml;
using System.Xml.Linq;

namespace UnityYamlMerge.Core;

public static class HttpClientExtensions
{
    private const int MaxRetryCount = 3;
    private const int TimeoutMilliSeconds = 5 * 60 * 1000; // 5 minutes

    public static async Task<string> GetLatestLtsVersionAsync(this HttpClient httpClient, ReadOnlyMemory<string> excludeVersions = default, CancellationToken cancellationToken = default)
    {
        const string uri = "https://unity.com/releases/editor/lts-releases.xml";

        cancellationToken.ThrowIfCancellationRequested();
        var xmlReaderSettings = new XmlReaderSettings { Async = true };

        for (var i = MaxRetryCount; i > 0; i--)
        {
            using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutTokenSource.CancelAfter(TimeoutMilliSeconds);
            var externalCancellation = timeoutTokenSource.Token;

            try
            {
                using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, externalCancellation);
                if (i > 1 && IsRetryStatusCode(response.StatusCode))
                {
                    continue;
                }
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(externalCancellation);
                using var reader = XmlReader.Create(stream, xmlReaderSettings);
                while (await reader.ReadAsync())
                {
                    if (reader is not { NodeType: XmlNodeType.Element, LocalName: "item" })
                    {
                        continue;
                    }
                    var item = (XElement)await XNode.ReadFromAsync(reader, externalCancellation);
                    var link = item.Element("link")?.Value;
                    if (string.IsNullOrEmpty(link))
                    {
                        continue;
                    }

                    var version = link.AsSpan().TrimEnd('/');
                    version = version[(version.LastIndexOf('/') + 1)..];

                    var excluded = false;
                    foreach (var excludeVersion in excludeVersions.Span)
                    {
                        if (version.SequenceEqual(excludeVersion.AsSpan()))
                        {
                            excluded = true;
                            break;
                        }
                    }
                    if (!excluded)
                    {
                        return version.ToString();
                    }
                }
            }
            catch (OperationCanceledException e) when (e.CancellationToken == timeoutTokenSource.Token)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(e.Message, e, cancellationToken);
                }
                throw new TimeoutException($"Request to {uri} timed out after {TimeoutMilliSeconds} milliseconds.", e);
            }
        }

        throw new InvalidOperationException("Latest Unity LTS version was not found.");
    }

    public static async Task<bool> CheckValidVersionAsync(this HttpClient httpClient, string version, CancellationToken cancellationToken = default)
    {
        const string uri = "https://unity3d.com/unity/releases.xml";

        cancellationToken.ThrowIfCancellationRequested();
        var xmlReaderSettings = new XmlReaderSettings { Async = true };

        for (var i = MaxRetryCount; i > 0; i--)
        {
            using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutTokenSource.CancelAfter(TimeoutMilliSeconds);
            var externalCancellation = timeoutTokenSource.Token;

            try
            {
                using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, externalCancellation);
                if (i > 1 && IsRetryStatusCode(response.StatusCode))
                {
                    continue;
                }
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(externalCancellation);
                using var reader = XmlReader.Create(stream, xmlReaderSettings);
                while (await reader.ReadAsync())
                {
                    if (reader is not { NodeType: XmlNodeType.Element, LocalName: "item" })
                    {
                        continue;
                    }
                    var item = (XElement)await XNode.ReadFromAsync(reader, externalCancellation);
                    var link = item.Element("link")?.Value;
                    if (string.IsNullOrEmpty(link))
                    {
                        continue;
                    }

                    var releasedVersion = link.AsSpan().TrimEnd('/');
                    releasedVersion = releasedVersion[(releasedVersion.LastIndexOf('/') + 1)..];

                    if (releasedVersion.SequenceEqual(version))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (OperationCanceledException e) when (e.CancellationToken == timeoutTokenSource.Token)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(e.Message, e, cancellationToken);
                }
                throw new TimeoutException($"Request to {uri} timed out after {TimeoutMilliSeconds} milliseconds.", e);
            }
        }

        throw new InvalidOperationException("Failed to validate Unity version.");
    }

    private static bool IsRetryStatusCode(HttpStatusCode statusCode)
    {
        return statusCode is
            HttpStatusCode.RequestTimeout
            or HttpStatusCode.Conflict
            or HttpStatusCode.TooManyRequests
            or >= (HttpStatusCode)500 and <= (HttpStatusCode)599;
    }
}