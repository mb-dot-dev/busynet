using Busynet.Bar.Display;
using Refit;

namespace Busynet.Bar.Http;

public interface IBusyBarApi
{
    [Post("/api/display/draw")]
    Task<HttpResponseMessage> DrawAsync([Body] DisplayElements payload, CancellationToken ct);

    [Delete("/api/display/draw")]
    Task<HttpResponseMessage> ClearAsync([AliasAs("application_name")] string applicationName, CancellationToken ct);
}
