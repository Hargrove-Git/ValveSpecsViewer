using Microsoft.EntityFrameworkCore;

namespace ValveSpecsViewer.Data;

/// Answers the question a technician actually has: "I have this valve in front of me — what are
/// its critical dimensions?"
///
/// It decodes the model number, computes each tracked dimension's determinant signature from the
/// decoded segments, and looks the value up in `spec_values`. Because the signature is built from
/// the segments the number actually supplies, the resolver can tell the difference between "the
/// answer is 0.335" and "this number cannot tell you the answer" — which is the distinction the
/// old per-model list could not express, and the reason a partial number like `1905-00-N` was
/// unsafe to approve against.
public class ResolverService(ValveSpecsDb db, GrammarService grammar)
{
    public async Task<ResolveResult?> ResolveAsync(string modelNumber)
    {
        var parses = await grammar.ParseAllAsync(modelNumber);
        if (parses.Count == 0) return null;

        var best = await PickBestAsync(modelNumber, parses);

        // Other grammars that fit this number exactly as well as the chosen one. Worth naming:
        // for a partial number the choice is a judgement call, and a reviewer should see that the
        // same digits could be read as a different series.
        var alsoMatched = parses
            .Where(p => p.Score == best.Score && p.GrammarVersionId != best.GrammarVersionId)
            .Select(p => $"{p.FamilyName} ({p.VersionLabel})")
            .Distinct()
            .ToList();

        var dims = await db.FamilyDimensions
            .Include(fd => fd.Determinants)
            .Where(fd => fd.FamilyId == best.FamilyId)
            .ToListAsync();

        var values = await db.SpecValues
            .Where(sv => sv.FamilyId == best.FamilyId)
            .ToListAsync();

        var docIds = values.Where(v => v.SourceDocumentFileId.HasValue)
                           .Select(v => v.SourceDocumentFileId!.Value).Distinct().ToList();
        var docTitles = docIds.Count == 0
            ? []
            : await db.DocumentFiles.Where(d => docIds.Contains(d.Id))
                      .ToDictionaryAsync(d => d.Id, d => d.Title);

        // Codes this number actually supplies, real and derived alike.
        var codes = best.Matched.ToDictionary(
            s => s.SegmentKey, s => s.Code!, StringComparer.OrdinalIgnoreCase);

        var labels = best.Segments.ToDictionary(
            s => s.SegmentKey, s => s.Label, StringComparer.OrdinalIgnoreCase);

        // A derived segment that failed to resolve is unhelpful on its own — "Disc Type: unknown"
        // doesn't tell anyone what to go and find. Its rules name the real segments it depends on,
        // so those get reported instead.
        var derivedSources = await DerivedSourceLabelsAsync(best.GrammarVersionId, labels);

        var resolved = dims
            .Where(fd => fd.IsTracked)
            .Select(fd => Resolve(fd, codes, labels, derivedSources, values, docTitles))
            .OrderBy(d => d.Label)
            .ToList();

        return new ResolveResult(best, resolved, alsoMatched);
    }

    /// Chooses which grammar's family to resolve against.
    ///
    /// Grammar fit (Score) stays the primary signal, but a short model number frequently ties:
    /// `1905-00-N` matches four segments under both the 1900 and the 1900/P grammar with nothing
    /// left over, so both score identically and the winner was previously decided by family id.
    /// That silently resolved a standard 1900 against the 1900/P family — which holds no models
    /// and no values — and produced an empty dimension list.
    ///
    /// Among equally good fits, two much better signals are available: whether that family
    /// actually has this model number registered, and whether it has any values seeded at all.
    /// Both are applied only within the top-scoring group, so a genuinely better grammar is never
    /// passed over in favour of a worse one that happens to be populated.
    private async Task<ParseResult> PickBestAsync(string modelNumber, List<ParseResult> parses)
    {
        var topScore = parses.Max(p => p.Score);
        var contenders = parses.Where(p => p.Score == topScore).ToList();
        if (contenders.Count == 1) return contenders[0];

        var trimmed = (modelNumber ?? "").Trim();
        var familyIds = contenders.Select(p => p.FamilyId).Distinct().ToList();
        var nullableIds = familyIds.Cast<int?>().ToList();   // models.family_id is nullable

        var registeredIn = await db.Models
            .Where(m => m.ModelNumber == trimmed && m.FamilyId != null && nullableIds.Contains(m.FamilyId))
            .Select(m => m.FamilyId!.Value)
            .Distinct()
            .ToListAsync();

        var seededIn = await db.SpecValues
            .Where(v => familyIds.Contains(v.FamilyId))
            .Select(v => v.FamilyId)
            .Distinct()
            .ToListAsync();

        return contenders
            .OrderByDescending(p => registeredIn.Contains(p.FamilyId))
            .ThenByDescending(p => seededIn.Contains(p.FamilyId))
            .ThenByDescending(p => p.IsComplete)
            .First();
    }

    private static ResolvedDimension Resolve(
        FamilyDimension fd,
        Dictionary<string, string> codes,
        Dictionary<string, string> labels,
        Dictionary<string, List<string>> derivedSources,
        List<SpecValue> values,
        Dictionary<int, string> docTitles)
    {
        var keys = fd.Determinants.OrderBy(d => d.Ordinal).Select(d => d.SegmentKey).ToList();
        var determinants = keys.Count == 0 ? "universal" : string.Join(" + ", keys);
        var label = DimKey.Label(fd.Dimension);

        if (fd.NaForFamily)
            return new(fd.Dimension, label, determinants, null, [], null, null,
                       ResolveState.NaForFamily);

        var missing = keys.Where(k => !codes.ContainsKey(k)).ToList();
        if (missing.Count > 0)
        {
            var needs = missing
                .SelectMany(k => derivedSources.TryGetValue(k, out var src) && src.Count > 0
                                 ? src
                                 : [labels.GetValueOrDefault(k, k)])
                .Distinct()
                .ToList();
            return new(fd.Dimension, label, determinants, null, needs, null, null,
                       ResolveState.Indeterminate);
        }

        var signature = keys.Count == 0
            ? "(all)"
            : string.Join("|", keys.Select(k => $"{k}={codes[k]}"));

        var value = values.FirstOrDefault(
            v => v.Dimension == fd.Dimension && v.DeterminantSignature == signature);

        if (value is null)
            return new(fd.Dimension, label, determinants, signature, [], null, null,
                       ResolveState.NoValue);

        var title = value.SourceDocumentFileId is { } id ? docTitles.GetValueOrDefault(id) : null;

        var state = value.ConfirmedNa      ? ResolveState.NotApplicable
                  : value.ReviewStatus == ReviewStatus.Approved ? ResolveState.Approved
                  : value.ReviewStatus == ReviewStatus.Rejected ? ResolveState.Rejected
                  : ResolveState.Unreviewed;

        return new(fd.Dimension, label, determinants, signature, [], value, title, state);
    }

    /// derived segment key -> labels of the real segments its rules read.
    private async Task<Dictionary<string, List<string>>> DerivedSourceLabelsAsync(
        int grammarVersionId, Dictionary<string, string> labels)
    {
        var derived = await db.DerivedSegments
            .Include(d => d.Rules)
            .Where(d => d.GrammarVersionId == grammarVersionId)
            .ToListAsync();

        return derived.ToDictionary(
            d => d.SegmentKey,
            d => d.Rules.Select(r => labels.GetValueOrDefault(r.SourceSegmentKey, r.SourceSegmentKey))
                        .Distinct().ToList(),
            StringComparer.OrdinalIgnoreCase);
    }
}
