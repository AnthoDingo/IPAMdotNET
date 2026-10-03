using IPAMdotNet.Data;
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

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is null)
        {
            return Page();
        }
        Customer? customer = await db.Customers.FindAsync(id);
        if (customer is null)
        {
            return NotFound();
        }
        Customer = customer;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Customer.Id = id ?? 0;
        if (!ModelState.IsValid)
        {
            return Page();
        }
        db.Update(Customer);
        await db.SaveChangesAsync();
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
