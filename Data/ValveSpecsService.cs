using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace ValveSpecsViewer.Data;

public class ValveSpecsService(ValveSpecsDb db)
{
    // No auth/roles yet — swap this out for the real signed-in user once available.
    private const string PendingAuthApprover = "Unassigned (pending auth)";

    public async Task SetApprovalAsync(int specId, bool approved)
    {
        var spec = await db.PublishedSpecs.FindAsync(specId);
        if (spec is null) return;

        spec.Approved   = approved;
        spec.ApprovedBy = approved ? PendingAuthApprover : null;
        spec.ApprovedAt = approved ? DateTime.UtcNow : null;

        await db.SaveChangesAsync();
    }

    public Task<List<string>> GetManufacturersAsync() =>
        db.Manufacturers
          .OrderBy(m => m.Name)
          .Select(m => m.Name)
          .ToListAsync();

    public Task<List<ValveModel>> GetModelsAsync(string manufacturerName) =>
        db.Models
          .Include(m => m.Manufacturer)
          .Include(m => m.ModelFamily)
          .Where(m => m.Manufacturer!.Name == manufacturerName)
          .OrderBy(m => m.ModelNumber)
          .ToListAsync();

    public async Task<ValveDetail?> GetValveDetailAsync(int modelId)
    {
        var model = await db.Models
            .Include(m => m.Manufacturer)
            .Include(m => m.ModelFamily)
            .FirstOrDefaultAsync(m => m.Id == modelId);

        if (model is null) return null;

        var specRows = await db.PublishedSpecs
            .Where(s => s.ModelId == modelId)
            .ToListAsync();

        var specs = specRows.ToDictionary(s => s.Dimension);

        // Build source text for each spec row
        // Strategy:
        //   1. Check spec_sources for a linked document_file — gives us doc title + page
        //   2. Fall back to published_specs.source text
        //   3. If source is "doc_file_id=N", resolve the id to the document title

        var specIds = specRows.Select(s => s.Id).ToList();

        // Load any spec_sources rows that exist for these specs (join doc title + page)
        var specSourceRows = await db.SpecSources
            .Include(ss => ss.DocumentFile)
            .Where(ss => specIds.Contains(ss.PublishedSpecId))
            .ToListAsync();

        // Group by published_spec_id (can be multiple sources per spec)
        var specSourcesBySpecId = specSourceRows
            .GroupBy(ss => ss.PublishedSpecId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Collect all doc_file_id references from .source text to resolve in bulk
        var docFileIdRefs = specRows
            .Where(s => s.Source != null && s.Source.StartsWith("doc_file_id="))
            .Select(s => int.TryParse(s.Source!.Replace("doc_file_id=", ""), out var n) ? n : 0)
            .Where(n => n > 0)
            .Distinct()
            .ToList();

        var docFilesById = docFileIdRefs.Count > 0
            ? await db.DocumentFiles
                .Where(d => docFileIdRefs.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id)
            : [];

        // Build human-readable source string per spec
        var sourceText = new Dictionary<int, string>();
        foreach (var spec in specRows)
        {
            var parts = new List<string>();

            // Prefer spec_sources linked doc if available
            if (specSourcesBySpecId.TryGetValue(spec.Id, out var ssList))
            {
                foreach (var ss in ssList)
                {
                    var docTitle = ss.DocumentFile?.Title ?? $"Doc #{ss.DocumentFileId}";
                    var page = ss.PageLabel;
                    var table = ss.TableReference;
                    var entry = docTitle;
                    if (!string.IsNullOrWhiteSpace(page))   entry += $", p. {page}";
                    if (!string.IsNullOrWhiteSpace(table))  entry += $" ({table})";
                    parts.Add(entry);
                }
            }

            // If no spec_sources, use .source text (resolve doc_file_id refs)
            if (parts.Count == 0 && spec.Source is not null)
            {
                var src = spec.Source;
                if (src.StartsWith("doc_file_id=") &&
                    int.TryParse(src.Replace("doc_file_id=", ""), out var dfId) &&
                    docFilesById.TryGetValue(dfId, out var doc))
                {
                    parts.Add(doc.Title);
                    if (!string.IsNullOrWhiteSpace(doc.DocumentNumber))
                        parts[^1] += $" ({doc.DocumentNumber})";
                }
                else if (!src.StartsWith("doc_file_id="))
                {
                    // Already human-readable (e.g. "Manual p3-20, Dim J")
                    parts.Add(src);
                }
            }

            if (parts.Count > 0)
                sourceText[spec.Id] = string.Join(" · ", parts);
        }

        var notes = await db.ModelNotes
            .Where(n => n.ModelId == modelId)
            .OrderBy(n => n.NoteType)
            .ToListAsync();

        return new ValveDetail
        {
            Model      = model,
            Specs      = specs,
            SourceText = sourceText,
            Notes      = notes,
        };
    }
}
