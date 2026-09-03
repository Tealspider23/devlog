using Microsoft.AspNetCore.Http;

namespace Devlog.Api.Security;

/// <summary>
/// Guards every route it is applied to behind <c>X-Devlog-Token</c>.
/// <para>
/// Applied to the <c>/v1</c> group only, never globally, so <c>/health</c> stays
/// reachable with no token — a monitor or the desktop shell needs to tell
/// "collector not running" apart from "wrong token" without holding a secret.
/// </para>
/// </summary>
public sealed class TokenAuthFilter(ApiTokenStore tokens) : IEndpointFilter
{
    public const string HeaderName = "X-Devlog-Token";

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();

        if (string.IsNullOrEmpty(provided) || !string.Equals(provided, tokens.Token, StringComparison.Ordinal))
        {
            return ValueTask.FromResult<object?>(Results.Unauthorized());
        }

        return next(context);
    }
}
