using IPAMdotNet.Localization;
using System.ComponentModel.DataAnnotations;
using System.Text;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace IPAMdotNet.Pages.Administration.ImportExport;

public class IndexModel(AppDbContext db) : PageModel
{
    private const long MaxFileSize = 1024 * 1024;

    public List<SelectListItem> FormatItems { get; } = CsvTransfer.Formats.Select(f => new SelectListItem(f.Label, f.Key)).ToList();

    [BindProperty, Display(Name = "Type d'objet")]
    public string Format { get; set; } = CsvTransfer.Formats[0].Key;

    [BindProperty, Display(Name = "Fichier CSV")]
    public IFormFile? Upload { get; set; }

    /// <summary>Contenu du fichier conservé entre l'aperçu et la confirmation (champ caché).</summary>
    [BindProperty]
    public string? CsvText { get; set; }

    [BindProperty, Display(Name = "Mettre à jour les objets existants")]
    public bool Update { get; set; }

    public CsvImportResult? Result { get; private set; }
    public List<string[]> PreviewRows { get; private set; } = [];

    [TempData]
    public string? Message { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnGetExportAsync(string format)
    {
        CsvFormat? csv = CsvTransfer.Formats.FirstOrDefault(f => f.Key == format);
        if (csv is null)
        {
            return NotFound();
        }
        string content = Csv.Write(await CsvTransfer.ExportAsync(db, csv));
        // BOM UTF-8 : sans lui, Excel ouvre le fichier en ANSI et casse les accents.
        byte[] bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(content)).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"ipamdotnet-{csv.Key}-{DateTime.Now:yyyyMMdd}.csv");
    }

    /// <summary>Export Excel : un type d'objet, ou tous (une feuille par type) si <paramref name="format"/> est vide.</summary>
    public async Task<IActionResult> OnGetExcelAsync(string? format)
    {
        List<CsvFormat> formats = string.IsNullOrEmpty(format) ? [.. CsvTransfer.Formats] : [.. CsvTransfer.Formats.Where(f => f.Key == format)];
        if (formats.Count == 0)
        {
            return NotFound();
        }
        List<(string Name, List<string?[]> Rows)> sheets = [];
        foreach (CsvFormat csv in formats)
        {
            sheets.Add((csv.Label, await CsvTransfer.ExportAsync(db, csv)));
        }
        return File(Xlsx.Write(sheets), Xlsx.ContentType, $"ipamdotnet-{(formats.Count == 1 ? formats[0].Key : "export")}-{DateTime.Now:yyyyMMdd}.xlsx");
    }

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        if (Upload is null || Upload.Length == 0)
        {
            ModelState.AddModelError(nameof(Upload), L.T("Choisissez un fichier CSV."));
            return Page();
        }
        if (Upload.Length > MaxFileSize)
        {
            ModelState.AddModelError(nameof(Upload), L.T("Fichier trop volumineux (1 Mo maximum)."));
            return Page();
        }
        using StreamReader reader = new(Upload.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        CsvText = await reader.ReadToEndAsync();
        return await PrepareAsync();
    }

    public async Task<IActionResult> OnPostImportAsync()
    {
        IActionResult page = await PrepareAsync();
        if (Result is null || Result.Errors.Count > 0 || Result.Entities.Count + Result.Updated.Count == 0)
        {
            return page;
        }
        // Les objets mis à jour sont déjà suivis et modifiés par la préparation.
        db.AddRange(Result.Entities);
        await db.SaveChangesAsync();
        // ponytail: un enregistrement par objet pour ses champs personnalisés ; à regrouper si les imports deviennent volumineux.
        foreach (KeyValuePair<object, Dictionary<int, string?>> pair in Result.CustomValues)
        {
            await CustomFieldForm.SaveAsync(db, (int)db.Entry(pair.Key).Property("Id").CurrentValue!, pair.Value);
        }
        string label = CsvTransfer.Formats.First(f => f.Key == Format).Label;
        string summary = $"{Result.Entities.Count} objet(s) « {label} » créé(s), {Result.Updated.Count} mis à jour";
        await db.LogAsync(LogSeverity.Info, LogEntry.Maintenance, $"Import CSV : {summary}.",
            User.Identity?.Name, HttpContext.Connection.RemoteIpAddress?.ToString());
        Message = L.T("Import terminé : {0} objet(s) « {1} » créé(s), {2} mis à jour.", Result.Entities.Count, L.T(label), Result.Updated.Count);
        return RedirectToPage();
    }

    private async Task<IActionResult> PrepareAsync()
    {
        CsvFormat? csv = CsvTransfer.Formats.FirstOrDefault(f => f.Key == Format);
        if (csv is null || CsvText is null)
        {
            ModelState.AddModelError(string.Empty, L.T("Import incomplet : rechargez le fichier."));
            return Page();
        }
        List<string[]> rows = Csv.Parse(CsvText);
        Result = await CsvTransfer.PrepareAsync(db, csv, rows, Update);
        PreviewRows = rows.Take(21).ToList();
        return Page();
    }
}
