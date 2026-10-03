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
}
