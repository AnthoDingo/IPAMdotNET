using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IPAMdotNet.Maintenance;

/// <summary>Pages de demandes d'adresses : 404 quand la fonctionnalité est désactivée dans les paramètres serveur.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class IpRequestsEnabledAttribute : Attribute, IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        if (!SettingsStore.Server.EnableIpRequests)
        {
            context.Result = new NotFoundResult();
            return;
        }
        await next();
    }
}
