using IPAMdotNet.Data;

namespace IPAMdotNet.Navigation;

/// <summary>Objets rattachés à un emplacement ou à un client (fiches de détail).</summary>
public sealed record LinkedObjects(List<Subnet> Subnets, List<Device> Devices, List<Rack> Racks, List<Circuit> Circuits);
