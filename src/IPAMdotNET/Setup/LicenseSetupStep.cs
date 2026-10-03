using System.Net;
using System.Reflection;
using AnthoDingo.Setup;

namespace IPAMdotNet.Setup;

public sealed class LicenseSetupStep : ISetupExtraStep
{
    private static readonly string LicenseText = LoadLicense();

    public string Id => "license";
    public string Label => "Licence";

    public Task<string> RenderAsync(SetupExtraStepContext ctx, CancellationToken ct) =>
        Task.FromResult($"""
            <form method="post" action="/setup">
              <input type="hidden" name="step" value="{Id}" />
              <input type="hidden" name="pendingState" value="{WebUtility.HtmlEncode(ctx.PendingStateToken)}" />
              <p>{WebUtility.HtmlEncode(ctx.AppName)} est distribué sous licence GNU AGPL v3 ou ultérieure.</p>
              <pre class="border rounded p-2 small" style="height:20rem;overflow:auto;white-space:pre-wrap">{WebUtility.HtmlEncode(LicenseText)}</pre>
              <div class="form-check mb-3">
                <input class="form-check-input" type="checkbox" id="acceptLicense" name="acceptLicense" value="true" required />
                <label class="form-check-label" for="acceptLicense">J'ai lu et j'accepte les termes de la licence</label>
              </div>
              <button type="submit" class="btn btn-primary w-100">Continuer</button>
            </form>
            """);

    public Task<SetupExtraStepResult> HandleAsync(SetupExtraStepContext ctx, IFormCollection form, CancellationToken ct) =>
        Task.FromResult(form["acceptLicense"] == "true"
            ? SetupExtraStepResult.Success()
            : SetupExtraStepResult.Failure("Vous devez accepter la licence pour continuer."));

    private static string LoadLicense()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LICENSE")
            ?? throw new InvalidOperationException("Ressource LICENSE introuvable.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
