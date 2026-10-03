using System.ComponentModel.DataAnnotations;
using IPAMdotNet.Data;
using IPAMdotNet.Navigation;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Administration.Users;

public sealed class UserInput
{
    [Required(ErrorMessage = "Le nom d'utilisateur est requis."), MaxLength(100), Display(Name = "Nom d'utilisateur")]
    public string UserName { get; set; } = "";

    [MaxLength(200), Display(Name = "Nom affiché")]
    public string? DisplayName { get; set; }

    [MaxLength(200), EmailAddress(ErrorMessage = "Adresse e-mail invalide."), Display(Name = "E-mail")]
    public string? Email { get; set; }

    [Display(Name = "Administrateur")]
    public bool IsAdmin { get; set; }

    [Display(Name = "Compte actif")]
    public bool Enabled { get; set; } = true;

    [Display(Name = "Authentification")]
    public int? AuthMethodId { get; set; }

    [DataType(DataType.Password), Display(Name = "Mot de passe")]
    public string? Password { get; set; }

    [DataType(DataType.Password), Display(Name = "Confirmation")]
    public string? PasswordConfirm { get; set; }

    public List<int> GroupIds { get; set; } = [];
}

public class EditModel(AppDbContext db) : PageModel
{
    public const int MinPasswordLength = 8;

    [BindProperty]
    public UserInput Input { get; set; } = new();

    public int UserId { get; private set; }
    public bool IsSelf => UserId == User.UserId();
    public List<SelectListItem> AuthMethods { get; private set; } = [];
    public List<Group> Groups { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is not null)
        {
            User? user = await db.Users.Include(u => u.Groups).SingleOrDefaultAsync(u => u.Id == id);
            if (user is null)
            {
                return NotFound();
            }
            UserId = user.Id;
            Input = new UserInput
            {
                UserName = user.UserName,
                DisplayName = user.DisplayName,
                Email = user.Email,
                IsAdmin = user.IsAdmin,
                Enabled = user.Enabled,
                AuthMethodId = user.AuthMethodId,
                GroupIds = user.Groups.Select(g => g.Id).ToList(),
            };
        }
        await LoadListsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        UserId = id ?? 0;
        User? user = id is null ? null : await db.Users.Include(u => u.Groups).SingleOrDefaultAsync(u => u.Id == id);
        if (id is not null && user is null)
        {
            return NotFound();
        }

        string userName = Data.User.NormalizeUserName(Input.UserName);
        if (await db.Users.AnyAsync(u => u.UserName == userName && u.Id != UserId))
        {
            ModelState.AddModelError("Input.UserName", "Ce nom d'utilisateur existe déjà.");
        }
        if (Input.AuthMethodId is not null && !await db.AuthMethods.AnyAsync(a => a.Id == Input.AuthMethodId))
        {
            ModelState.AddModelError("Input.AuthMethodId", "Méthode d'authentification inconnue.");
        }
        bool local = Input.AuthMethodId is null;
        bool needsPassword = local && (user is null || string.IsNullOrEmpty(user.PasswordHash));
        if (local && (needsPassword || !string.IsNullOrEmpty(Input.Password)))
        {
            if (string.IsNullOrEmpty(Input.Password) || Input.Password.Length < MinPasswordLength)
            {
                ModelState.AddModelError("Input.Password", $"Le mot de passe doit faire au moins {MinPasswordLength} caractères.");
            }
            else if (Input.Password != Input.PasswordConfirm)
            {
                ModelState.AddModelError("Input.PasswordConfirm", "Les deux mots de passe ne correspondent pas.");
            }
        }
        if (IsSelf && (!Input.IsAdmin || !Input.Enabled))
        {
            ModelState.AddModelError(string.Empty, "Vous ne pouvez pas retirer vos propres droits d'administrateur ni désactiver votre compte.");
        }
        else if (user is not null && user.IsAdmin && user.Enabled && (!Input.IsAdmin || !Input.Enabled) && !await OtherActiveAdminExistsAsync(user.Id))
        {
            ModelState.AddModelError(string.Empty, "Il doit rester au moins un administrateur actif.");
        }
        if (!ModelState.IsValid)
        {
            await LoadListsAsync();
            return Page();
        }

        user ??= new User { UserName = userName, PasswordHash = "" };
        user.UserName = userName;
        user.DisplayName = string.IsNullOrWhiteSpace(Input.DisplayName) ? null : Input.DisplayName.Trim();
        user.Email = string.IsNullOrWhiteSpace(Input.Email) ? null : Input.Email.Trim();
        user.IsAdmin = Input.IsAdmin;
        user.Enabled = Input.Enabled;
        user.AuthMethodId = Input.AuthMethodId;
        if (!local)
        {
            user.PasswordHash = "";
        }
        else if (!string.IsNullOrEmpty(Input.Password))
        {
            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Input.Password);
        }
        user.Groups = await db.Groups.Where(g => Input.GroupIds.Contains(g.Id)).ToListAsync();
        if (user.Id == 0)
        {
            db.Users.Add(user);
        }
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        User? user = await db.Users.Include(u => u.Groups).SingleOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return NotFound();
        }
        UserId = id;
        if (IsSelf)
        {
            ModelState.AddModelError(string.Empty, "Vous ne pouvez pas supprimer votre propre compte.");
        }
        else if (user.IsAdmin && user.Enabled && !await OtherActiveAdminExistsAsync(id))
        {
            ModelState.AddModelError(string.Empty, "Il doit rester au moins un administrateur actif.");
        }
        else if (await db.IpRequests.AnyAsync(r => r.RequestedById == id || r.ProcessedById == id))
        {
            ModelState.AddModelError(string.Empty, "Cet utilisateur a des demandes d'adresses : désactivez-le plutôt que de le supprimer.");
        }
        if (!ModelState.IsValid)
        {
            return await OnGetAsync(id);
        }
        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    private Task<bool> OtherActiveAdminExistsAsync(int userId) =>
        db.Users.AnyAsync(u => u.Id != userId && u.IsAdmin && u.Enabled);

    private async Task LoadListsAsync()
    {
        AuthMethods = await db.AuthMethods.OrderBy(a => a.Name).Select(a => new SelectListItem(a.Name, a.Id.ToString())).ToListAsync();
        Groups = await db.Groups.OrderBy(g => g.Name).ToListAsync();
    }
}
