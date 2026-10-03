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
            new("Recherche", "search"),
            new("Calculateur IP", "calculator"),
            new("Journal des modifications", "clock-history"),
            new("Sous-réseaux favoris", "star"),
            new("Demandes d'adresses", "inbox"),
            new("Instructions", "info-circle"),
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
            new("Équipements", "hdd"),
            new("Racks", "hdd-stack"),
            new("Emplacements", "geo-alt"),
            new("Circuits", "plug"),
            new("Clients", "people"),
            new("Préfixes RTC", "telephone"),
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
            new("Équipements", "hdd"),
            new("Types d'équipements", "cpu"),
            new("Racks", "hdd-stack"),
            new("VLAN", "hdd-network", "/Network/Vlans/Index"),
            new("VRF", "shuffle", "/Network/Vrfs/Index"),
            new("Serveurs de noms", "globe2", "/Network/Nameservers/Index"),
            new("Emplacements", "geo-alt"),
            new("NAT", "arrow-left-right", "/Network/Nat/Index"),
            new("Clients", "people"),
            new("Circuits", "plug"),
            new("Préfixes RTC", "telephone"),
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
