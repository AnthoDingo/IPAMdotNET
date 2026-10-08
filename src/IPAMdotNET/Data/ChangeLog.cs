using System.ComponentModel.DataAnnotations;

namespace IPAMdotNet.Data;

public enum ChangeAction
{
    [Display(Name = "Création")] Created,
    [Display(Name = "Modification")] Updated,
    [Display(Name = "Suppression")] Deleted,
}

/// <summary>Entrée du journal des modifications, écrite automatiquement par <see cref="AppDbContext.SaveChangesAsync(bool, CancellationToken)"/>.</summary>
public class ChangeLog
{
    public int Id { get; set; }

    public DateTime Date { get; set; }

    public int? UserId { get; set; }

    [MaxLength(200)]
    public string UserName { get; set; } = "";

    /// <summary>Nom de la classe de l'entité (Subnet, Vlan…).</summary>
    [MaxLength(50)]
    public string EntityType { get; set; } = "";

    public int EntityId { get; set; }

    [MaxLength(200)]
    public string? EntityLabel { get; set; }

    public ChangeAction Action { get; set; }

    /// <summary>JSON : { "Libellé du champ": ["ancienne valeur", "nouvelle valeur"] }.</summary>
    public string? Changes { get; set; }

    /// <summary>
    /// Section de l'objet au moment du changement (types de <see cref="SectionScopedTypes"/>) : filtre les entrées selon les droits,
    /// y compris pour un objet supprimé depuis. Pas de clé étrangère : la section peut avoir disparu.
    /// </summary>
    public int? SectionId { get; set; }

    /// <summary>Types visibles seulement si la section est lisible (une entrée sans section n'est montrée qu'aux admins).</summary>
    public static readonly string[] SectionScopedTypes = [nameof(Section), nameof(Subnet), nameof(IpAddress), nameof(IpRequest)];

    /// <summary>Types d'administration : leurs entrées ne sont montrées qu'aux admins.</summary>
    public static readonly string[] AdminOnlyTypes =
        [nameof(User), nameof(Group), nameof(AuthMethod), nameof(ApiKey), nameof(RemoteAgent), nameof(AppSetting), nameof(CustomField), nameof(Tag)];

    /// <summary>Libellé français et page de détail (ou de liste) de chaque type journalisé.</summary>
    public static readonly IReadOnlyDictionary<string, (string Label, string Page, bool HasDetails)> Types =
        new Dictionary<string, (string, string, bool)>
        {
            [nameof(Section)] = ("Section", "/Sections/Index", true),
            [nameof(Subnet)] = ("Sous-réseau", "/Network/Subnets/Details", true),
            [nameof(Vlan)] = ("VLAN", "/Network/Vlans/Index", false),
            [nameof(VlanDomain)] = ("Domaine L2", "/Network/VlanDomains/Index", false),
            [nameof(Vrf)] = ("VRF", "/Network/Vrfs/Index", false),
            [nameof(Nameserver)] = ("Serveurs de noms", "/Network/Nameservers/Index", false),
            [nameof(NatRule)] = ("NAT", "/Network/Nat/Index", false),
            [nameof(NatRuleObject)] = ("Objet NAT", "/Network/Nat/Index", false),
            [nameof(BgpPeer)] = ("Pair BGP", "/Network/Routing/Index", false),
            [nameof(BgpPeerSubnet)] = ("Sous-réseau BGP", "/Network/Routing/Index", false),
            [nameof(Location)] = ("Emplacement", "/Infrastructure/Locations/Details", true),
            [nameof(Customer)] = ("Client", "/Infrastructure/Customers/Details", true),
            [nameof(DeviceType)] = ("Type d'équipement", "/Infrastructure/DeviceTypes/Index", false),
            [nameof(Device)] = ("Équipement", "/Infrastructure/Devices/Details", true),
            [nameof(Rack)] = ("Rack", "/Infrastructure/Racks/Details", true),
            [nameof(CircuitProvider)] = ("Fournisseur", "/Infrastructure/Circuits/Providers/Index", false),
            [nameof(Circuit)] = ("Circuit", "/Infrastructure/Circuits/Index", false),
            [nameof(CircuitType)] = ("Type de circuit", "/Infrastructure/Circuits/Types/Index", false),
            [nameof(LogicalCircuit)] = ("Circuit logique", "/Infrastructure/Circuits/Logical/Index", false),
            [nameof(LogicalCircuitMember)] = ("Membre de circuit logique", "/Infrastructure/Circuits/Logical/Index", false),
            [nameof(PstnPrefix)] = ("Préfixe RTC", "/Infrastructure/Pstn/Details", true),
            [nameof(PstnNumber)] = ("Numéro RTC", "/Infrastructure/Pstn/Index", false),
            [nameof(IpRequest)] = ("Demande d'adresse", "/Tools/Requests/Index", false),
            [nameof(AppSetting)] = ("Paramètre", "/Tools/Instructions/Index", false),
            [nameof(CustomField)] = ("Champ personnalisé", "/Administration/CustomFields/Index", false),
            [nameof(User)] = ("Utilisateur", "/Administration/Users/Edit", true),
            [nameof(Group)] = ("Groupe", "/Administration/Groups/Edit", true),
            [nameof(AuthMethod)] = ("Méthode d'authentification", "/Administration/AuthMethods/Edit", true),
            [nameof(ApiKey)] = ("Clé d'API", "/Administration/ApiKeys/Index", false),
            [nameof(RemoteAgent)] = ("Agent de scan distant", "/Administration/ScanAgents/Index", false),
            [nameof(Tag)] = ("Étiquette", "/Administration/Tags/Edit", true),
            [nameof(IpAddress)] = ("Adresse IP", "/Network/Addresses/Edit", true),
        };
}
