using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;

namespace IPAMdotNet.Navigation;

/// <summary>Entrée de menu. <paramref name="Page"/> null = fonctionnalité pas encore implémentée (affichée désactivée).</summary>
public sealed record MenuItem(string Label, string Icon, string? Page = null, Func<ServerSettings, bool>? Visible = null)
{
    /// <summary>Entrée affichée selon les paramètres serveur (fonctionnalité désactivable).</summary>
    public bool IsVisible => Visible?.Invoke(SettingsStore.Server) != false;
}

public sealed record MenuGroup(string Label, IReadOnlyList<MenuItem> Items);

/// <summary>Menus Outils et Administration, calqués sur ceux de phpIPAM.</summary>
public static class Menus
{
    public static readonly IReadOnlyList<MenuGroup> Tools =
    [
        new("Outils",
        [
            new("Recherche", "search", "/Tools/Search/Index"),
            new("Calculateur IP", "calculator", "/Tools/Calculator/Index"),
            new("Journal des modifications", "clock-history", "/Tools/Changelog/Index"),
            new("Sous-réseaux favoris", "star", "/Tools/Favorites/Index"),
            new("Demandes d'adresses", "inbox", "/Tools/Requests/Index", s => s.EnableIpRequests),
            new("Instructions", "info-circle", "/Tools/Instructions/Index"),
        ]),
        new("Réseau",
        [
            new("Sous-réseaux", "diagram-3", "/Network/Subnets/Index"),
            new("VLAN", "hdd-network", "/Network/Vlans/Index"),
            new("VRF", "shuffle", "/Network/Vrfs/Index"),
            new("NAT", "arrow-left-right", "/Network/Nat/Index"),
            new("Multicast", "broadcast", "/Network/Multicast/Index"),
            new("Routage", "signpost-split", "/Network/Routing/Index"),
            new("DNS", "globe", "/Network/Nameservers/Index"),
        ]),
        new("Infrastructure",
        [
            new("Équipements", "hdd", "/Infrastructure/Devices/Index"),
            new("Racks", "hdd-stack", "/Infrastructure/Racks/Index"),
            new("Emplacements", "geo-alt", "/Infrastructure/Locations/Index"),
            new("Circuits", "plug", "/Infrastructure/Circuits/Index"),
            new("Clients", "people", "/Infrastructure/Customers/Index"),
            new("Préfixes RTC", "telephone", "/Infrastructure/Pstn/Index"),
        ]),
    ];

    public static readonly IReadOnlyList<MenuGroup> Administration =
    [
        new("Serveur",
        [
            new("Paramètres", "gear", "/Administration/Settings/Index"),
            new("Utilisateurs", "person", "/Administration/Users/Index"),
            new("Groupes", "people", "/Administration/Groups/Index"),
            new("Méthodes d'authentification", "shield-lock", "/Administration/AuthMethods/Index"),
            new("Messagerie", "envelope", "/Administration/Mail/Index"),
            new("API", "code-slash", "/Administration/ApiKeys/Index"),
            new("Agents de scan", "broadcast-pin"),
            new("Langues", "translate"),
            new("Widgets", "grid", "/Administration/Widgets/Index"),
            new("Étiquettes", "tags", "/Administration/Tags/Index"),
        ]),
        new("Gestion IP",
        [
            new("Sections", "collection", "/Administration/Sections/Index"),
            new("Sous-réseaux", "diagram-3", "/Network/Subnets/Index"),
            new("Équipements", "hdd", "/Infrastructure/Devices/Index"),
            new("Types d'équipements", "cpu", "/Infrastructure/DeviceTypes/Index"),
            new("Racks", "hdd-stack", "/Infrastructure/Racks/Index"),
            new("VLAN", "hdd-network", "/Network/Vlans/Index"),
            new("VRF", "shuffle", "/Network/Vrfs/Index"),
            new("Serveurs de noms", "globe2", "/Network/Nameservers/Index"),
            new("Emplacements", "geo-alt", "/Infrastructure/Locations/Index"),
            new("NAT", "arrow-left-right", "/Network/Nat/Index"),
            new("Clients", "people", "/Infrastructure/Customers/Index"),
            new("Circuits", "plug", "/Infrastructure/Circuits/Index"),
            new("Préfixes RTC", "telephone", "/Infrastructure/Pstn/Index"),
            new("Routage", "signpost-split", "/Network/Routing/Index"),
        ]),
        new("Maintenance",
        [
            new("Champs personnalisés", "input-cursor-text", "/Administration/CustomFields/Index"),
            new("Vérifier la base", "database-check", "/Administration/Verify/Index"),
            new("Remplacer des valeurs", "arrow-repeat", "/Administration/Replace/Index"),
            new("Import / export", "arrow-down-up", "/Administration/ImportExport/Index"),
            new("Journaux", "journal-text", "/Administration/Logs/Index"),
        ]),
    ];
}
