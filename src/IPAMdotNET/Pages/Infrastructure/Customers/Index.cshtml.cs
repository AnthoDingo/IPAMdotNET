using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Customers;

public class IndexModel(AppDbContext db) : PageModel
{
    public List<Customer> Customers { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Customers = await db.Customers.OrderBy(c => c.Name).ToListAsync();
    }
}
