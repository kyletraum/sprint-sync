using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SprintSync.Api.Tenancy;

namespace SprintSync.Api.Tests.Infrastructure;

/// <summary>
/// Exposes the server-resolved ambient tenant at <c>/__test/tenant</c> so tests
/// can assert what the acting context actually became (FR-010) rather than
/// inferring it from downstream behaviour.
///
/// Registered as an <see cref="IStartupFilter"/> and appended AFTER the app's
/// own pipeline, so it runs once authentication, JIT provisioning and tenant
/// resolution have all had their say. The path is unmapped, so routing falls
/// through to it. Test-only — it exists nowhere in the production app.
/// </summary>
public sealed class TenantProbeStartupFilter : IStartupFilter
{
    public const string Path = "/__test/tenant";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);

        app.Run(async context =>
        {
            if (context.Request.Path != Path)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var tenant = context.RequestServices.GetRequiredService<ITenantContext>();
            await context.Response.WriteAsJsonAsync(new TenantProbeResponse(tenant.OrganizationId));
        });
    };
}

public sealed record TenantProbeResponse(Guid? OrganizationId);
