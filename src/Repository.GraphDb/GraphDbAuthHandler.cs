using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace Forge.Repository.GraphDb;

/// <summary>
/// <see cref="DelegatingHandler"/> that injects a Basic-Auth <c>Authorization</c> header
/// derived from <see cref="GraphDbOptions.Username"/> and <see cref="GraphDbOptions.Password"/>
/// into every outgoing request. Per-request header injection allows
/// <see cref="IHttpClientFactory"/> to rotate the underlying HTTP handler (and refresh DNS)
/// without losing credentials. See ADR-0005 of the Repository.GraphDb slice.
/// </summary>
internal sealed class GraphDbAuthHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<GraphDbOptions> _options;

    public GraphDbAuthHandler(IOptionsMonitor<GraphDbOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;
        if (!string.IsNullOrEmpty(opts.Username))
        {
            var creds = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{opts.Username}:{opts.Password ?? string.Empty}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", creds);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
