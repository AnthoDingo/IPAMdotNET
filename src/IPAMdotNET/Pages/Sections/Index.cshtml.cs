using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPAMdotNet.Pages.Sections;

public class IndexModel(AppDbContext db) : PageModel
{
    public Section Section { get; private set; } = new();
    public List<SubnetNode> Tree { get; private set; } = [];

    /// <summary>Sous-réseaux listés : l'arbre, ou ceux qui correspondent au filtre.</summary>
    public List<SubnetNode> Listed { get; private set; } = [];

    [BindProperty(SupportsGet = true, Name = CustomFieldList.Prefix)]
    public CustomFieldList Custom { get; set; } = new();

    public List<CustomFieldInput> CustomFieldValues { get; private set; } = [];
    public bool CanWrite { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Section? section = await db.Sections.FindAsync(id);
        if (section is null)
        {
            return NotFound();
        }
        SectionAccess access = await SectionAccess.ForAsync(db, User);
        if (!access.CanRead(id))
        {
            return Forbid();
        }
        Section = section;
        CanWrite = access.CanWrite(id);
        Tree = await SubnetTree.LoadAsync(db, id);
        CustomFieldValues = await CustomFieldForm.LoadAsync(db, nameof(Section), id);
        Custom.Fields = await CustomFieldForm.DefinitionsAsync(db, nameof(Subnet));
        Listed = await Custom.ApplyAsync<Subnet, SubnetNode>(db, Tree, n => n.Subnet.Id);
        return Page();
    }
}
