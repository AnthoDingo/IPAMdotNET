namespace IPAMdotNet.Data;

public class FavoriteSubnet
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public int SubnetId { get; set; }
    public Subnet? Subnet { get; set; }
}
