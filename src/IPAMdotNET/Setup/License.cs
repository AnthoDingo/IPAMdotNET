using System.Reflection;

namespace IPAMdotNet.Setup;

/// <summary>Texte de la licence (fichier LICENSE embarqué dans l'assembly), affiché en première page de l'assistant /setup.</summary>
public static class License
{
    public static string Text()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LICENSE")
            ?? throw new InvalidOperationException("Ressource LICENSE introuvable.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
