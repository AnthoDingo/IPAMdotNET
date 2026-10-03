using System.Net;
using IPAMdotNet.Data;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Tools.Search;

public class IndexModel(AppDbContext db) : PageModel
{
    private const int Limit = 100;

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public List<Subnet> Subnets { get; private set; } = [];
    public List<Vlan> Vlans { get; private set; } = [];
    public List<Vrf> Vrfs { get; private set; } = [];
    public List<Device> Devices { get; private set; } = [];
    public List<Location> Locations { get; private set; } = [];
    public List<Customer> Customers { get; private set; } = [];
    public List<Circuit> Circuits { get; private set; } = [];
    public List<NatRule> NatRules { get; private set; } = [];
    public List<BgpPeer> BgpPeers { get; private set; } = [];
    public List<Nameserver> Nameservers { get; private set; } = [];
    public List<PstnPrefix> PstnPrefixes { get; private set; } = [];

    public int Total => Subnets.Count + Vlans.Count + Vrfs.Count + Devices.Count + Locations.Count + Customers.Count
        + Circuits.Count + NatRules.Count + BgpPeers.Count + Nameservers.Count + PstnPrefixes.Count;

    public async Task OnGetAsync()
    {
        string query = Q?.Trim() ?? "";
        if (query.Length == 0)
        {
            return;
        }
        // Comparaison en minuscules des deux côtés : même résultat sur les trois moteurs (PostgreSQL est sensible à la casse).
        string text = query.ToLowerInvariant();
        long? number = long.TryParse(query, out long parsed) ? parsed : null;

        Subnets = await SearchSubnetsAsync(query, text);
        Vlans = await db.Vlans
            .Where(v => v.Name.ToLower().Contains(text) || (v.Description != null && v.Description.ToLower().Contains(text))
                || v.Number == number)
            .OrderBy(v => v.Number).Take(Limit).ToListAsync();
        Vrfs = await db.Vrfs
            .Where(v => v.Name.ToLower().Contains(text) || (v.RouteDistinguisher != null && v.RouteDistinguisher.ToLower().Contains(text))
                || (v.Description != null && v.Description.ToLower().Contains(text)))
            .OrderBy(v => v.Name).Take(Limit).ToListAsync();
        Devices = await db.Devices.Include(d => d.DeviceType)
            .Where(d => d.Hostname.ToLower().Contains(text) || (d.IpAddress != null && d.IpAddress.Contains(text))
                || (d.Description != null && d.Description.ToLower().Contains(text)))
            .OrderBy(d => d.Hostname).Take(Limit).ToListAsync();
        Locations = await db.Locations
            .Where(l => l.Name.ToLower().Contains(text) || (l.Address != null && l.Address.ToLower().Contains(text))
                || (l.Description != null && l.Description.ToLower().Contains(text)))
            .OrderBy(l => l.Name).Take(Limit).ToListAsync();
        Customers = await db.Customers
            .Where(c => c.Name.ToLower().Contains(text) || (c.City != null && c.City.ToLower().Contains(text))
                || (c.ContactPerson != null && c.ContactPerson.ToLower().Contains(text)) || (c.ContactMail != null && c.ContactMail.ToLower().Contains(text)))
            .OrderBy(c => c.Name).Take(Limit).ToListAsync();
        Circuits = await db.Circuits.Include(c => c.Provider)
            .Where(c => c.Cid.ToLower().Contains(text) || (c.Comment != null && c.Comment.ToLower().Contains(text)))
            .OrderBy(c => c.Cid).Take(Limit).ToListAsync();
        NatRules = await db.NatRules
            .Where(n => n.Name.ToLower().Contains(text) || n.Source.Contains(text) || n.Destination.Contains(text))
            .OrderBy(n => n.Name).Take(Limit).ToListAsync();
        BgpPeers = await db.BgpPeers
            .Where(b => b.Name.ToLower().Contains(text) || b.LocalAddress.Contains(text) || b.PeerAddress.Contains(text)
                || b.LocalAs == number || b.PeerAs == number)
            .OrderBy(b => b.Name).Take(Limit).ToListAsync();
        Nameservers = await db.Nameservers
            .Where(n => n.Name.ToLower().Contains(text) || n.Servers.Contains(text))
            .OrderBy(n => n.Name).Take(Limit).ToListAsync();
        string? digits = PstnPrefix.Normalize(query);
        // En mémoire (préfixes peu nombreux) : un numéro complet (+3314…) doit aussi trouver les préfixes qui le commencent.
        PstnPrefixes = (await db.PstnPrefixes.ToListAsync())
            .Where(p => p.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || (digits != null && (p.Prefix.Contains(digits, StringComparison.Ordinal) || digits.StartsWith(p.Prefix, StringComparison.Ordinal))))
            .OrderBy(p => p.Prefix, StringComparer.Ordinal).Take(Limit).ToList();
    }

    /// <summary>
    /// Adresse → sous-réseaux qui la contiennent ; réseau CIDR → sous-réseaux qui le contiennent ou qu'il contient ;
    /// texte → description.
    /// </summary>
    private async Task<List<Subnet>> SearchSubnetsAsync(string query, string text)
    {
        IPNetwork? target = null;
        if (IPAddress.TryParse(query, out IPAddress? address))
        {
            target = new IPNetwork(address, address.GetAddressBytes().Length * 8);
        }
        else if (query.Contains('/') && IPNetwork.TryParse(query, out IPNetwork network))
        {
            target = network;
        }

        IQueryable<Subnet> subnets = db.Subnets.Include(s => s.Section).Include(s => s.Vlan).Include(s => s.Vrf);
        if (target is null)
        {
            return await subnets.Where(s => s.Description != null && s.Description.ToLower().Contains(text))
                .OrderBy(s => s.Address).ThenBy(s => s.PrefixLength).Take(Limit).ToListAsync();
        }
        // ponytail: filtrage en mémoire sur tous les sous-réseaux ; passer à une requête par bornes d'adresses si le volume l'exige.
        List<Subnet> all = await subnets.OrderBy(s => s.Address).ThenBy(s => s.PrefixLength).ToListAsync();
        IPNetwork wanted = target.Value;
        return all.Where(s => Ip.Contains(s.Network, wanted) || Ip.Contains(wanted, s.Network)).Take(Limit).ToList();
    }
}
