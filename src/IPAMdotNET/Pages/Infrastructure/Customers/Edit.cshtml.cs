using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore.Storage;

namespace IPAMdotNet.Pages.Infrastructure.Customers;

[Authorize(Policy = "Admin")]
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public Customer Customer { get; set; } = new();

    // Nom explicite : sans lui, si aucun champ « Custom[…] » n'est posté, ASP.NET retombe sur le préfixe vide et lit les autres champs comme clés.
    [BindProperty(Name = CustomFieldForm.Prefix)]
    public Dictionary<int, string?> Custom { get; set; } = [];

    public List<CustomFieldInput> CustomInputs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is not null)
        {
            Customer? customer = await db.Customers.FindAsync(id);
            if (customer is null)
            {
                return NotFound();
            }
            Customer = customer;
        }
        CustomInputs = await CustomFieldForm.LoadAsync(db, nameof(Customer), id ?? 0);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Customer.Id = id ?? 0;
        List<CustomField> customFields = await CustomFieldForm.DefinitionsAsync(db, nameof(Customer));
        Dictionary<int, string?> customValues = CustomFieldForm.Validate(customFields, Custom, ModelState);
        if (!ModelState.IsValid)
        {
            CustomInputs = CustomFieldForm.FromPosted(customFields, Custom);
            return Page();
        }
        db.Update(Customer);
        await db.SaveChangesAsync();
        await CustomFieldForm.SaveAsync(db, Customer.Id, customValues);
        return RedirectToPage("Details", new { id = Customer.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        Customer? customer = await db.Customers.FindAsync(id);
        if (customer is null)
        {
            return NotFound();
        }
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
        await db.DetachCustomerAsync(id);
        db.Customers.Remove(customer);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return RedirectToPage("Index");
    }
}
