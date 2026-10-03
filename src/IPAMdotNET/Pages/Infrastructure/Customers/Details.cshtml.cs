using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using IPAMdotNet.Navigation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Customers;

public class DetailsModel(AppDbContext db) : PageModel
{
    public List<CustomFieldInput> CustomFieldValues { get; private set; } = [];

    public Customer Customer { get; private set; } = new();
    public LinkedObjects Linked { get; private set; } = new([], [], [], []);

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Customer? customer = await db.Customers.FindAsync(id);
        if (customer is null)
        {
            return NotFound();
        }
        Customer = customer;
        Linked = new LinkedObjects(
            await db.Subnets.Where(s => s.CustomerId == id).OrderBy(s => s.Address).ThenBy(s => s.PrefixLength).ToListAsync(),
            await db.Devices.Where(d => d.CustomerId == id).OrderBy(d => d.Hostname).ToListAsync(),
            await db.Racks.Where(r => r.CustomerId == id).OrderBy(r => r.Name).ToListAsync(),
            await db.Circuits.Include(c => c.Provider).Where(c => c.CustomerId == id).OrderBy(c => c.Cid).ToListAsync());
        CustomFieldValues = await CustomFieldForm.LoadAsync(db, nameof(Customer), id);
        return Page();
    }
}
