using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Customers;

public class IndexModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true, Name = CustomFieldList.Prefix)]
    public CustomFieldList Custom { get; set; } = new();

    public List<Customer> Customers { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await Custom.LoadAsync(db, nameof(Customer));
        Customers = await Custom.Apply(db, db.Customers).OrderBy(c => c.Name).ToListAsync();
    }
}
