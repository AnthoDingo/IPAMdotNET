namespace IPAMdotNet.Navigation;

/// <summary>Modèle du bouton « Supprimer » des pages de modification (rien n'est affiché en création, Id = 0).</summary>
public sealed record DeleteForm(int Id, string Label);
