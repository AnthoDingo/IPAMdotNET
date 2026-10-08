using AnthoDingo.Setup;

namespace IPAMdotNet.Setup;

/// <summary>Page d'avertissement de l'assistant /setup, affichée après la licence et avant la connexion à la base.</summary>
public sealed class DisclaimerSetupTask : ISetupPreInstallTask
{
    public string Title => "Avertissement";

    public Task<SetupTaskResult> ExecuteAsync(CancellationToken ct = default) =>
        Task.FromResult(SetupTaskResult.Ok("""
            <p>IPAM.Net est un projet indépendant qui reprend l'ergonomie et les fonctionnalités de <strong>phpIPAM</strong>.</p>
            <p class="mb-0">Il n'est ni développé, ni maintenu, ni approuvé par le développeur de phpIPAM : pour toute question ou anomalie,
            adressez-vous au projet IPAM.Net et non à celui de phpIPAM. Les noms et marques cités appartiennent à leurs propriétaires respectifs.</p>
            """));
}
