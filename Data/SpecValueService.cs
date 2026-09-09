using Microsoft.EntityFrameworkCore;

namespace ValveSpecsViewer.Data;

/// Review of dimension values keyed by determinant signature rather than by model.
///
/// A reviewer approves an engineering fact once — "orifice H, non-bellows, barrel O.D. 1.117" —
/// and it governs every model sharing that signature. This is what removes the ambiguity in the
/// old per-model flow, where `1905-00-N` could not tell you which seat design you were looking
/// at and therefore which disc thickness you were signing off.
public class SpecValueService(ValveSpecsDb db, GrammarService grammar)
{
    /// Families that have something to review, with their manufacturer loaded.
    ///
    /// The manufacturer is not decoration: family names are NOT unique across manufacturers. There
    /// are already two families called "2700 Series", one Farris and one Consolidated, so a flat
    /// list of family names is genuinely ambiguous rather than only theoretically so.
    /// Deliberately two round trips rather than a Join. `Include` after a Join projection is not
    /// supported by EF Core -- it is dropped or throws rather than loading the navigation -- which
    /// left Manufacturer null on every row and made the manufacturer picker render empty.
    public async Task<List<ModelFamily>> GetFamiliesWithSpecValuesAsync()
    {
        var ids = await db.FamilyDimensions.Select(fd => fd.FamilyId).Distinct().ToListAsync();

        return await db.ModelFamilies
            .Include(f => f.Manufacturer)
            .Where(f => ids.Contains(f.Id))
            .OrderBy(f => f.Manufacturer!.Name).ThenBy(f => f.FamilyName)
            .ToListAsync();
    }

    /// Per-dimension review progress for a family — drives the dimension picker.
    public async Task<List<DimensionProgress>> GetProgressAsync(int familyId)
    {
        var dims = await db.FamilyDimensions
            .Include(fd => fd.Determinants)
            .Where(fd => fd.FamilyId == familyId && fd.IsTracked)
            .ToListAsync();

        var values = await db.SpecValues
            .Where(sv => sv.FamilyId == familyId)
            .ToListAsync();

        return dims.Select(fd =>
        {
            var mine = values.Where(v => v.Dimension == fd.Dimension).ToList();
            var determinants = fd.Determinants.Count == 0
                ? "universal"
                : string.Join(" + ", fd.Determinants.OrderBy(d => d.Ordinal).Select(d => d.SegmentKey));

            return new DimensionProgress(
                fd.Dimension, DimKey.Label(fd.Dimension), fd.NaForFamily, determinants,
                mine.Count,
                mine.Count(v => v.ReviewStatus == ReviewStatus.Approved && !v.ConfirmedNa),
                mine.Count(v => v.ReviewStatus == ReviewStatus.Rejected && !v.ConfirmedNa),
                // A confirmed-N/A row is settled, not outstanding work — otherwise the summary
                // reads "1 open, 1 N/A" for the same single row and implies work that isn't there.
                mine.Count(v => v.ReviewStatus == ReviewStatus.Unreviewed && !v.ConfirmedNa),
                mine.Count(v => v.ConfirmedNa),
                mine.Count > 0);
        })
        .OrderByDescending(p => p.HasValues)
        .ThenBy(p => p.Label)
        .ToList();
    }

    /// Every signature for one (family, dimension), with how many known models each governs.
    public async Task<List<SpecValueRow>> GetRowsAsync(int familyId, string dimension)
    {
        var values = await db.SpecValues
            .Where(sv => sv.FamilyId == familyId && sv.Dimension == dimension)
            .ToListAsync();

        var docIds = values.Where(v => v.SourceDocumentFileId.HasValue)
                           .Select(v => v.SourceDocumentFileId!.Value).Distinct().ToList();
        var docTitles = docIds.Count == 0 ? [] :
            await db.DocumentFiles.Where(d => docIds.Contains(d.Id))
                    .ToDictionaryAsync(d => d.Id, d => d.Title);

        var coverage = await CountCoveredModelsAsync(familyId, dimension, values.Select(v => v.DeterminantSignature));

        return values
            .Select(v => new SpecValueRow(
                v,
                coverage.GetValueOrDefault(v.DeterminantSignature),
                v.SourceDocumentFileId is { } id && docTitles.TryGetValue(id, out var t) ? t : null))
            // Settled N/A rows sort last. disc_thickness has 51 of them against 34 real values,
            // and ordering purely by signature puts "cryodisc" first — burying every row that
            // needs a decision behind seventeen screens of nothing to do.
            .OrderBy(r => r.Value.ConfirmedNa)
            .ThenBy(r => r.Value.DeterminantSignature, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// Signature -> number of models in the family whose decoded segments match it.
    ///
    /// A model only counts when it actually supplies every determinant segment. A partial number
    /// missing the seat design contributes to no seat-design signature — which is precisely the
    /// gap that made per-model approving unsafe, so it is surfaced rather than papered over.
    private async Task<Dictionary<string, int>> CountCoveredModelsAsync(
        int familyId, string dimension, IEnumerable<string> signatures)
    {
        var result = signatures.Distinct().ToDictionary(s => s, _ => 0);

        var fd = await db.FamilyDimensions
            .Include(x => x.Determinants)
            .FirstOrDefaultAsync(x => x.FamilyId == familyId && x.Dimension == dimension);
        if (fd is null) return result;

        var keys = fd.Determinants.OrderBy(d => d.Ordinal).Select(d => d.SegmentKey).ToList();

        var models = await db.Models.Where(m => m.FamilyId == familyId).ToListAsync();

        // Universal dimension: every model in the family is covered.
        if (keys.Count == 0)
        {
            if (result.ContainsKey("(all)")) result["(all)"] = models.Count;
            return result;
        }

        var grammars = await grammar.GetVersionsAsync(familyId);
        if (grammars.Count == 0) return result;

        foreach (var m in models)
        {
            var parse = grammars.Select(g => GrammarService.Parse(m.ModelNumber, g))
                                .Where(p => p.IsViable)
                                .OrderByDescending(p => p.Score)
                                .FirstOrDefault();
            if (parse is null) continue;

            var codes = parse.Matched.ToDictionary(s => s.SegmentKey, s => s.Code!);
            if (!keys.All(codes.ContainsKey)) continue;   // model doesn't pin this dimension down

            var sig = string.Join("|", keys.Select(k => $"{k}={codes[k]}"));
            if (result.ContainsKey(sig)) result[sig]++;
        }

        return result;
    }

    // ── Review actions ──────────────────────────────────────────

    public async Task SetReviewStatusAsync(int specValueId, string status, string reviewer)
    {
        var v = await db.SpecValues.FindAsync(specValueId);
        if (v is null) return;

        v.ReviewStatus = status;
        if (status == ReviewStatus.Unreviewed)
        {
            v.ReviewedBy = null; v.ReviewedAt = null; v.ReviewNotes = null;
        }
        else
        {
            v.ReviewedBy = reviewer; v.ReviewedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
    }

    public async Task RejectAsync(int specValueId, string reviewer, string notes, decimal? correctedValue)
    {
        var v = await db.SpecValues.FindAsync(specValueId);
        if (v is null) return;

        v.ReviewStatus = ReviewStatus.Rejected;
        v.ReviewedBy = reviewer;
        v.ReviewedAt = DateTime.UtcNow;
        v.ReviewNotes = notes;
        v.CorrectedValue = correctedValue;
        v.CorrectedBy = correctedValue.HasValue ? reviewer : null;
        v.CorrectedAt = correctedValue.HasValue ? DateTime.UtcNow : null;
        await db.SaveChangesAsync();
    }
}
