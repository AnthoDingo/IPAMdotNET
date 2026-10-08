using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace IPAMdotNet.Data;

public class BgpPeer
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom est requis."), MaxLength(100), Display(Name = "Nom du pair")]
    public string Name { get; set; } = "";

    [Range(1, 4294967295, ErrorMessage = "AS entre 1 et 4294967295."), Display(Name = "AS local")]
    public long LocalAs { get; set; }

    [Required(ErrorMessage = "L'adresse locale est requise."), MaxLength(45), Display(Name = "Adresse locale")]
    public string LocalAddress { get; set; } = "";

    [Range(1, 4294967295, ErrorMessage = "AS entre 1 et 4294967295."), Display(Name = "AS du pair")]
    public long PeerAs { get; set; }

    [Required(ErrorMessage = "L'adresse du pair est requise."), MaxLength(45), Display(Name = "Adresse du pair")]
    public string PeerAddress { get; set; } = "";

    [Display(Name = "VRF")]
    public int? VrfId { get; set; }

    [ValidateNever]
    public Vrf? Vrf { get; set; }

    [MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }

    public string Type => LocalAs == PeerAs ? "iBGP" : "eBGP";

    /// <summary>Sous-réseaux annoncés au pair ou reçus de lui (« routing_subnets » de phpIPAM).</summary>
    [ValidateNever]
    public List<BgpPeerSubnet> Subnets { get; set; } = [];
}

public enum BgpDirection
{
    [Display(Name = "Annoncé")] Advertised,
    [Display(Name = "Reçu")] Received,
}

/// <summary>Sous-réseau annoncé ou reçu par un pair BGP.</summary>
public class BgpPeerSubnet
{
    public int Id { get; set; }

    [Display(Name = "Pair BGP")]
    public int BgpPeerId { get; set; }

    public BgpPeer? BgpPeer { get; set; }

    [Display(Name = "Sous-réseau")]
    public int SubnetId { get; set; }

    public Subnet? Subnet { get; set; }

    [Display(Name = "Sens")]
    public BgpDirection Direction { get; set; }
}
