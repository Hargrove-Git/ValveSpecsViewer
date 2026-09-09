using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace ValveSpecsViewer.Data;

public class ValveSpecsService(ValveSpecsDb db)
{
    public async Task SetReviewStatusAsync(int specId, string status, string reviewer)
    {
        var spec = await db.PublishedSpecs.FindAsync(specId);
        if (spec is null) return;

        spec.ReviewStatus = status;
        if (status == ReviewStatus.Unreviewed)
        {
            spec.ReviewedBy = null;
            spec.ReviewedAt = null;
        }
        else
        {
            spec.ReviewedBy = reviewer;
            spec.ReviewedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    /// Records the reviewer verdict "this dimension does not apply to this valve, or is not
    /// published for it." Distinct from a rejection, which means "the published value is wrong."
    ///
    /// Non-destructive: any existing spec value is left in place and simply stops being
    /// displayed (DimHelper treats ConfirmedNa as authoritative), so the original stays
    /// available for audit.
    public async Task MarkNotApplicableAsync(int specId, string reviewer, string? reason)
    {
        var spec = await db.PublishedSpecs.FindAsync(specId);
        if (spec is null) return;

        spec.ConfirmedNa   = true;
        spec.ConfirmedNaBy = reviewer;                // provenance: this N/A came from review
        spec.ConfirmedNaAt = DateTime.UtcNow;
        spec.ReviewStatus  = ReviewStatus.Approved;   // an N/A verdict IS a sign-off
        spec.ReviewedBy    = reviewer;
        spec.ReviewedAt    = DateTime.UtcNow;
        spec.RejectionNotes = string.IsNullOrWhiteSpace(reason) ? "Not applicable" : reason;

        await db.SaveChangesAsync();
    }

    /// Undoes MarkNotApplicableAsync. ConfirmedNa is cleared ONLY when this flag was set by a
    /// reviewer (ConfirmedNaBy present). A source-derived N/A — the manual genuinely doesn't
    /// publish the dimension — survives, even if a reviewer has separately acted on the row.
    public async Task ClearNotApplicableAsync(int specId)
    {
        var spec = await db.PublishedSpecs.FindAsync(specId);
        if (spec is null) return;

        if (spec.ConfirmedNaBy is not null)
        {
            spec.ConfirmedNa   = false;
            spec.ConfirmedNaBy = null;
            spec.ConfirmedNaAt = null;
        }

        spec.ReviewStatus   = ReviewStatus.Unreviewed;
        spec.ReviewedBy     = null;
        spec.ReviewedAt     = null;
        spec.RejectionNotes = null;

        await db.SaveChangesAsync();
    }

    public async Task RejectWithCorrectionAsync(
        int specId, string reviewer, string rejectionNotes,
        decimal? correctedValue, int? correctedDocumentFileId, string? correctedPageNotes)
    {
        var spec = await db.PublishedSpecs.FindAsync(specId);
        if (spec is null) return;

        spec.ReviewStatus  = ReviewStatus.Rejected;
        spec.ReviewedBy    = reviewer;
        spec.ReviewedAt    = DateTime.UtcNow;
        spec.RejectionNotes = rejectionNotes;
        spec.CorrectedValue = correctedValue;
        spec.CorrectedDocumentFileId = correctedDocumentFileId;
        spec.CorrectedPageNotes = correctedPageNotes;
        spec.CorrectedBy = correctedValue.HasValue ? reviewer : null;
        spec.CorrectedAt = correctedValue.HasValue ? DateTime.UtcNow : null;

        await db.SaveChangesAsync();
    }

    public Task<List<DocumentFile>> GetDocumentsForManufacturerAsync(int manufacturerId) =>
        db.DocumentFiles
          .Where(d => d.ManufacturerId == manufacturerId)
          .OrderBy(d => d.Title)
          .ToListAsync();

    public async Task SetModelRejectionAsync(int modelId, bool rejected, string reviewer, string? notes)
    {
        var model = await db.Models.FindAsync(modelId);
        if (model is null) return;

        model.ModelRejected = rejected;
        model.ModelRejectedBy = rejected ? reviewer : null;
        model.ModelRejectedAt = rejected ? DateTime.UtcNow : null;
        model.ModelRejectionNotes = rejected ? notes : null;

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

        // Critical-dimension display is scoped to standard trim (trim_variant IS NULL) --
        // trim-specific values (DM, O-ring, Thermodisc, Cryodisc, etc.) are captured in the
        // same table but aren't part of the client's critical-dimension set, and would
        // collide with the standard row on the Dimension key below if included here.
        var specRows = await db.PublishedSpecs
            .Where(s => s.ModelId == modelId && s.TrimVariant == null)
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
            .Concat(specRows.Where(s => s.CorrectedDocumentFileId.HasValue).Select(s => s.CorrectedDocumentFileId!.Value))
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

        // Build human-readable source string for any correction on each spec
        var correctedSourceText = new Dictionary<int, string>();
        foreach (var spec in specRows)
        {
            string? docTitle = spec.CorrectedDocumentFileId is { } cfId && docFilesById.TryGetValue(cfId, out var cdoc)
                ? cdoc.Title
                : null;

            var entry = string.Join(", ", new[] { docTitle, spec.CorrectedPageNotes }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (entry.Length > 0)
                correctedSourceText[spec.Id] = entry;
        }

        var notes = await db.ModelNotes
            .Where(n => n.ModelId == modelId)
            .OrderBy(n => n.NoteType)
            .ToListAsync();

        // Family-wide documents (e.g. Model Number Breakdown guides) aren't the source of any
        // dimension value, so they never end up in docFilesById above -- pull them in separately.
        var familyDocs = model.FamilyId.HasValue
            ? await db.ModelFamilyDocuments
                .Include(fd => fd.DocumentFile)
                .Where(fd => fd.FamilyId == model.FamilyId)
                .Select(fd => fd.DocumentFile!)
                .ToListAsync()
            : [];

        var relatedDocuments = docFilesById.Values
            .Concat(familyDocs)
            .DistinctBy(d => d.Id)
            .OrderBy(d => d.Title)
            .ToList();

        return new ValveDetail
        {
            Model               = model,
            Specs               = specs,
            SourceText          = sourceText,
            CorrectedSourceText = correctedSourceText,
            Notes               = notes,
            RelatedDocuments    = relatedDocuments,
        };
    }
}
