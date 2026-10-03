namespace IPAMdotNet.Navigation;

/// <summary>Entrée de menu. <paramref name="Page"/> null = fonctionnalité pas encore implémentée (affichée désactivée).</summary>
public sealed record MenuItem(string Label, string Icon, string? Page = null);

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
            new("Demandes d'adresses", "inbox", "/Tools/Requests/Index"),
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
            new("Paramètres", "gear"),
            new("Utilisateurs", "person"),
            new("Groupes", "people"),
            new("Méthodes d'authentification", "shield-lock"),
            new("Messagerie", "envelope"),
            new("API", "code-slash"),
            new("Agents de scan", "broadcast-pin"),
            new("Langues", "translate"),
            new("Widgets", "grid"),
            new("Étiquettes", "tags"),
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
            new("Champs personnalisés", "input-cursor-text"),
            new("Vérifier la base", "database-check"),
            new("Remplacer des valeurs", "arrow-repeat"),
            new("Import / export", "arrow-down-up"),
            new("Journaux", "journal-text"),
        ]),
    ];
}
