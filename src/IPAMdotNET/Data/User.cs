namespace IPAMdotNet.Data;

public class User
{
    public int Id { get; set; }
    public required string UserName { get; set; }
    public required string PasswordHash { get; set; }
    public string? DisplayName { get; set; }
    public bool IsAdmin { get; set; }

    // Noms d'utilisateur stockés en minuscules : comparaison identique sur les trois moteurs (Postgres est sensible à la casse).
    public static string NormalizeUserName(string userName) => userName.Trim().ToLowerInvariant();
}

