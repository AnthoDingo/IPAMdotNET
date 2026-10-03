using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Net;
using System.Net.Sockets;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace IPAMdotNet.Data;

public class Subnet
{
    public int Id { get; set; }

    [Display(Name = "Section")]
    public int SectionId { get; set; }

    [ValidateNever]
    public Section? Section { get; set; }

    /// <summary>Adresse réseau sur 16 octets (voir <see cref="Ip.ToBytes"/>).</summary>
    [ValidateNever, Display(Name = "Adresse réseau")]
    public byte[] Address { get; set; } = [];

    [Display(Name = "Préfixe")]
    public int PrefixLength { get; set; }

    [MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }

    [Display(Name = "VLAN")]
    public int? VlanId { get; set; }

    [ValidateNever]
    public Vlan? Vlan { get; set; }

    [Display(Name = "VRF")]
    public int? VrfId { get; set; }

    [ValidateNever]
    public Vrf? Vrf { get; set; }

    [Display(Name = "Serveurs de noms")]
    public int? NameserverId { get; set; }

    [ValidateNever]
    public Nameserver? Nameserver { get; set; }

    [Display(Name = "Emplacement")]
    public int? LocationId { get; set; }

    [ValidateNever]
    public Location? Location { get; set; }

    [Display(Name = "Client")]
    public int? CustomerId { get; set; }

    [ValidateNever]
    public Customer? Customer { get; set; }

    [Display(Name = "Ouvert aux demandes d'adresses")]
    public bool AllowRequests { get; set; }

    [Display(Name = "Vérifier l'état des hôtes (ping)")]
    public bool PingCheck { get; set; }

    [Display(Name = "Découvrir les nouveaux hôtes")]
    public bool Discover { get; set; }

    [Display(Name = "Dernier scan")]
    public DateTime? LastScanAt { get; set; }

    [NotMapped, ValidateNever]
    public IPNetwork Network => new(Ip.FromBytes(Address), PrefixLength);

    [NotMapped, ValidateNever]
    public bool IsIPv4 => Network.BaseAddress.AddressFamily == AddressFamily.InterNetwork;

    public void SetNetwork(IPNetwork network)
    {
        Address = Ip.ToBytes(network.BaseAddress);
        PrefixLength = network.PrefixLength;
    }
}
