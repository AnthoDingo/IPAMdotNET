using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Net;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace IPAMdotNet.Data;

/// <summary>Adresse IP d'un sous-réseau (stockée sur 16 octets comme les sous-réseaux, voir <see cref="Ip.ToBytes"/>).</summary>
public class IpAddress
{
    public int Id { get; set; }

    public int SubnetId { get; set; }

    [ValidateNever]
    public Subnet? Subnet { get; set; }

    [ValidateNever, Display(Name = "Adresse")]
    public byte[] Address { get; set; } = [];

    [MaxLength(100), Display(Name = "Nom d'hôte")]
    public string? Hostname { get; set; }

    [MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }

    /// <summary>Forme normalisée aa:bb:cc:dd:ee:ff.</summary>
    [MaxLength(17), Display(Name = "Adresse MAC")]
    public string? MacAddress { get; set; }

    [MaxLength(100), Display(Name = "Propriétaire")]
    public string? Owner { get; set; }

    [Display(Name = "Étiquette")]
    public int? TagId { get; set; }

    [ValidateNever]
    public Tag? Tag { get; set; }

    [Display(Name = "Équipement")]
    public int? DeviceId { get; set; }

    [ValidateNever]
    public Device? Device { get; set; }

    [Display(Name = "Exclure du ping")]
    public bool ExcludePing { get; set; }

    /// <summary>Dernière réponse au ping de l'agent de scan.</summary>
    [Display(Name = "Vue le")]
    public DateTime? LastSeen { get; set; }

    [NotMapped, ValidateNever]
    public IPAddress Value => Ip.FromBytes(Address);

    /// <summary>Accepte 00-11-22-AA-BB-CC, 0011.22aa.bbcc, 001122AABBCC… et renvoie aa:bb:cc:dd:ee:ff ; null si invalide.</summary>
    public static string? NormalizeMac(string? text)
    {
        string hex = new((text ?? "").Where(Uri.IsHexDigit).ToArray());
        string separators = new((text ?? "").Where(c => !Uri.IsHexDigit(c)).ToArray());
        if (hex.Length != 12 || separators.Any(c => c is not (':' or '-' or '.' or ' ')))
        {
            return null;
        }
        return string.Join(':', Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2))).ToLowerInvariant();
    }
}
