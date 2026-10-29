using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ValveSpecsViewer.Data;

public class Manufacturer
{
    [Key] public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Notes { get; set; }
}

public class ModelFamily
{
    [Key] public int Id { get; set; }
    public int ManufacturerId { get; set; }
    public string FamilyName { get; set; } = "";
    public string? Description { get; set; }
    public Manufacturer? Manufacturer { get; set; }
    public ICollection<ValveModel> Models { get; set; } = [];
}

public class ValveModel
{
    [Key] public int Id { get; set; }
    public int ManufacturerId { get; set; }
    public int? FamilyId { get; set; }
    public string ModelNumber { get; set; } = "";
    public string? LifecycleStatus { get; set; }
    public string? Confidence { get; set; }
    public string VerificationStatus { get; set; } = "";
    public string? VerificationNotes { get; set; }
    public string? Flags { get; set; }
    public bool ModelRejected { get; set; }
    public string? ModelRejectedBy { get; set; }
    public DateTime? ModelRejectedAt { get; set; }
    public string? ModelRejectionNotes { get; set; }
    public Manufacturer? Manufacturer { get; set; }
    public ModelFamily? ModelFamily { get; set; }
    public ICollection<PublishedSpec> PublishedSpecs { get; set; } = [];
    public ICollection<ModelNote> ModelNotes { get; set; } = [];
}

public class DocumentFile
{
    [Key] public int Id { get; set; }
    public int ManufacturerId { get; set; }
    public int DocumentTypeId { get; set; }
    public string Title { get; set; } = "";
    public string? DocumentNumber { get; set; }
    public string? Revision { get; set; }
    public DateOnly? RevisionDate { get; set; }
    public int? PageCount { get; set; }
    public string? FileName { get; set; }
    public string FileMimeType { get; set; } = "application/pdf";
    public byte[]? FileData { get; set; }
    public DateTime UploadedAt { get; set; }
    public string? UploadedBy { get; set; }
    public string? Notes { get; set; }
    public Manufacturer? Manufacturer { get; set; }
    public DocumentType? DocumentType { get; set; }
}

public class DocumentType
{
    [Key] public int Id { get; set; }
    public string TypeName { get; set; } = "";
    public string? Description { get; set; }
}

public class PublishedSpec
{
    [Key] public int Id { get; set; }
    public int ModelId { get; set; }
    public string Dimension { get; set; } = "";
    public decimal? SpecMin { get; set; }
    public decimal? SpecMax { get; set; }
    public decimal? Nominal { get; set; }
    public string? Unit { get; set; }
    public bool ConfirmedNa { get; set; }
    public int SourcePriority { get; set; }
    public string? Source { get; set; }
    public string? Notes { get; set; }
    public string ReviewStatus { get; set; } = Data.ReviewStatus.Unreviewed;
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionNotes { get; set; }
    public decimal? CorrectedValue { get; set; }
    public int? CorrectedDocumentFileId { get; set; }
    public string? CorrectedPageNotes { get; set; }
    public string? CorrectedBy { get; set; }
    public DateTime? CorrectedAt { get; set; }
    public string? TrimVariant { get; set; }
    // Set only when ConfirmedNa came from a reviewer's "Mark N/A" verdict. NULL means the flag
    // is source-derived (the manual doesn't publish the dimension) and must survive a revert.
    public string? ConfirmedNaBy { get; set; }
    public DateTime? ConfirmedNaAt { get; set; }
    public ValveModel? Model { get; set; }
    public DocumentFile? CorrectedDocumentFile { get; set; }
}

public class SpecSource
{
    [Key] public int Id { get; set; }
    public int PublishedSpecId { get; set; }
    public int DocumentFileId { get; set; }
    public string? PageLabel { get; set; }
    public string? TableReference { get; set; }
    public DocumentFile? DocumentFile { get; set; }
}

public class ModelNote
{
    [Key] public int Id { get; set; }
    public int ModelId { get; set; }
    public string NoteType { get; set; } = "";
    public string NoteText { get; set; } = "";
    public ValveModel? Model { get; set; }
}

// ── Signature-keyed spec values ─────────────────────────────────

public class FamilyDimension
{
    [Key] public int Id { get; set; }
    public int FamilyId { get; set; }
    public string Dimension { get; set; } = "";
    public bool IsTracked { get; set; }
    /// Permanently not applicable for this family — no source publishes it and none is expected.
    public bool NaForFamily { get; set; }
    public string? SourceNote { get; set; }
    public ICollection<DimensionDeterminant> Determinants { get; set; } = [];
}

public class DimensionDeterminant
{
    [Key] public int Id { get; set; }
    public int FamilyDimensionId { get; set; }
    public string SegmentKey { get; set; } = "";
    public int Ordinal { get; set; }
    public FamilyDimension? FamilyDimension { get; set; }
}

public class SpecValue
{
    [Key] public int Id { get; set; }
    public int FamilyId { get; set; }
    public string Dimension { get; set; } = "";
    public string DeterminantSignature { get; set; } = "";
    public decimal? SpecMin { get; set; }
    public decimal? SpecMax { get; set; }
    public decimal? Nominal { get; set; }
    public string? Unit { get; set; }
    public bool ConfirmedNa { get; set; }
    /// Shown in place of a value when there is no measurement to take — e.g. "Replace during VR
    /// Repair" for a thermodisc, Cryodisc or O-ring disc. "N/A" tells a technician the field is
    /// empty; this tells them what to do instead.
    public string? NaStatement { get; set; }
    public string? Source { get; set; }
    public int? SourceDocumentFileId { get; set; }
    public string? Notes { get; set; }
    public string ReviewStatus { get; set; } = Data.ReviewStatus.Unreviewed;
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNotes { get; set; }
    public decimal? CorrectedValue { get; set; }
    public string? CorrectedBy { get; set; }
    public DateTime? CorrectedAt { get; set; }

    public decimal? Value => Nominal ?? SpecMin ?? SpecMax;
}

/// A spec value plus how many known models it governs.
///
/// `CodeMeanings` is what each code in the signature stands for, from the current grammar — e.g.
/// "00 Non-Bellows". The signature alone reads `bellows 00`, which a reviewer took to mean bellows
/// design; the code's own description is what removes that misreading.
public record SpecValueRow(SpecValue Value, int CoveredModels, string? SourceDocTitle, string? CodeMeanings = null);

/// Review progress for one dimension within a family.
public record DimensionProgress(
    string Dimension, string Label, bool NaForFamily, string Determinants,
    int Total, int Approved, int Rejected, int Unreviewed, int Na, bool HasValues);

// ── Model-number grammar ────────────────────────────────────────

public class GrammarVersion
{
    [Key] public int Id { get; set; }
    public int FamilyId { get; set; }
    public string VersionLabel { get; set; } = "";
    public int? SourceDocumentFileId { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public bool IsCurrent { get; set; }
    public string? Notes { get; set; }
    public ModelFamily? Family { get; set; }
    public ICollection<SegmentDefinition> Segments { get; set; } = [];
    public ICollection<DerivedSegment> DerivedSegments { get; set; } = [];
}

public class SegmentDefinition
{
    [Key] public int Id { get; set; }
    public int GrammarVersionId { get; set; }
    public int Position { get; set; }
    public string SegmentKey { get; set; } = "";
    public string Label { get; set; } = "";
    public bool IsRequired { get; set; }
    /// This segment starts a new dash-delimited token. Assembly needs it to emit the canonical
    /// written form, and parsing needs it to rule out a segment that cannot open a token — see
    /// GrammarService.MatchFrom, where it is the constraint that stops an optional leading code
    /// eating the first character of the required segment behind it.
    public bool DashBefore { get; set; }

    /// Regular expression describing the SHAPE of an acceptable code, for the rare axis whose codes
    /// cannot be enumerated. The 1700's "Customer Weld End Detail Identification Number" is the only
    /// one across the eight current grammars: it is customer-assigned, so the catalogue gives it no
    /// legend and no code column, and with no codes the segment could never match anything.
    ///
    /// Write it without anchors — the parser anchors both ends itself and never lets a match cross a
    /// separator. Null means enumerated codes only, which is every other segment.
    public string? Pattern { get; set; }

    /// Stands in for the description an enumerated code would have carried, since a pattern match
    /// has no SegmentValue row to take one from. Required whenever Pattern is set.
    public string? PatternDescription { get; set; }

    public string? Notes { get; set; }
    public GrammarVersion? GrammarVersion { get; set; }
    public ICollection<SegmentValue> Values { get; set; } = [];

    /// Conditions on this segment being present at all — see SegmentCondition. Empty for all but the
    /// 1700's four branch segments, and an empty list means "always applicable".
    public ICollection<SegmentCondition> Conditions { get; set; } = [];

    /// A segment the parser can only match by shape. Worth distinguishing in the UI: the builder has
    /// to offer a text box rather than a dropdown, because there is no list to choose from.
    public bool IsFreeForm => Pattern is { Length: > 0 } && Values.Count == 0;
}

/// Gates a segment's PRESENCE on another segment's code.
///
/// The 1700's catalogue has two configuration-code diagrams that do not share a tail: the flanged
/// branch has an inlet flange facing and rating, the welded branch has a weld end detail and an
/// oversize buttweld nomenclature, and `inlet_type` (F or W) says which. The grammar is a flat
/// sequence, so before this the mutual exclusion lived only in a comment and impossible numbers --
/// a flanged valve carrying a weld end detail -- parsed as complete and correct.
///
/// Several conditions on one segment are ANDed. `MatchType` uses the same vocabulary as
/// DerivedSegmentRule: SegmentMatch.EqualTo, EndsWith, Present.
///
/// The rule that matters most: a condition is NOT enforced when the source segment did not match at
/// all. `inlet_type` is optional and omitted in practice — every one of the 52 registered 1700
/// numbers is a stem like `1706RR-HP` with no inlet type — so enforcing against an absent gate would
/// make them all unreadable. A condition catches a contradiction, and an unstated branch contradicts
/// nothing yet.
public class SegmentCondition
{
    [Key] public int Id { get; set; }
    public int SegmentDefinitionId { get; set; }

    /// Key of a real segment in the same grammar, which must sit at an EARLIER position than the
    /// segment being gated — the parser evaluates conditions as it walks left to right, so a gate
    /// that came later would not yet be known. Derived segments cannot be sources: they are computed
    /// after parsing finishes.
    public string SourceSegmentKey { get; set; } = "";
    public string MatchType { get; set; } = SegmentMatch.EqualTo;
    public string? MatchValue { get; set; }
    public string? Notes { get; set; }
    public SegmentDefinition? SegmentDefinition { get; set; }
}

public class SegmentValue
{
    [Key] public int Id { get; set; }
    public int SegmentDefinitionId { get; set; }
    public string Code { get; set; } = "";
    public string? Description { get; set; }
    public SegmentDefinition? SegmentDefinition { get; set; }
}

/// An axis computed from the real segments rather than read off the nameplate.
///
/// Exists because some critical dimensions depend on a combination of segments that would
/// multiply out absurdly as a determinant. `disc_type` is the motivating case: Dual Media is
/// flagged by an Interchangeability code ending in 'DM' and is orthogonal to Seat Design, so
/// {orifice, seat_design, interchangeability} would be 1360 signatures to express a 5-way choice.
public class DerivedSegment
{
    [Key] public int Id { get; set; }
    public int GrammarVersionId { get; set; }
    public string SegmentKey { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Notes { get; set; }
    public GrammarVersion? GrammarVersion { get; set; }
    public ICollection<DerivedSegmentRule> Rules { get; set; } = [];
}

/// One precedence rule for a DerivedSegment. Lowest Priority is evaluated first and the first
/// match wins, which is what lets the Dual Media test run ahead of the seat-design tests.
public class DerivedSegmentRule
{
    [Key] public int Id { get; set; }
    public int DerivedSegmentId { get; set; }
    public int Priority { get; set; }
    public string SourceSegmentKey { get; set; } = "";
    public string MatchType { get; set; } = "";
    public string? MatchValue { get; set; }
    public string ResultCode { get; set; } = "";
    public string? Notes { get; set; }
    public DerivedSegment? DerivedSegment { get; set; }
}

/// Values of derived_segment_rules.match_type. Named EqualTo rather than Equals so it doesn't
/// hide object.Equals; the stored string stays "equals".
public static class SegmentMatch
{
    public const string EqualTo  = "equals";
    public const string EndsWith = "ends_with";
    public const string Present  = "present";
}

// ── Resolver ────────────────────────────────────────────────────────────────

/// Why a dimension does or doesn't have a value for a given model number.
///
/// `Indeterminate` is the state that justifies this whole design. It means the number is a real
/// number and the dimension is real, but the number does not say enough to pick a value — e.g.
/// `1905-00-N` never states a seat design, so its disc thickness cannot be known. The old
/// per-model flow had no way to express that and silently showed one value as if it were the
/// answer.
public enum ResolveState
{
    Approved,       // value found, and a reviewer has signed it off
    Unreviewed,     // value found, not yet reviewed
    Rejected,       // value found, but a reviewer rejected it
    NotApplicable,  // this configuration genuinely has no such dimension
    NaForFamily,    // the dimension does not apply anywhere in this family
    Indeterminate,  // the model number doesn't pin down the determinants
    NoValue,        // determinants are pinned, but nothing is seeded for that combination
}

public record ResolvedDimension(
    string Dimension,
    string Label,
    string Determinants,
    string? Signature,
    /// Human labels of the segments this number would need to supply, for Indeterminate.
    List<string> Needs,
    SpecValue? Value,
    string? SourceTitle,
    ResolveState State)
{
    // Deliberately delegates to SpecValue.Value rather than re-deriving the column precedence.
    // Spelling it out a second time here had it backwards (SpecMin first instead of Nominal),
    // which would have shown two different numbers for the same row on the review and lookup
    // screens as soon as any dimension populated both columns.
    public bool HasNumber => Value is { ConfirmedNa: false } v && v.Value is not null;
    public decimal? Number => Value?.Value;
}

public record ResolveResult(
    ParseResult Parse,
    List<ResolvedDimension> Dimensions,
    /// Other grammars that fit this number exactly as well — named so a reviewer can see that a
    /// short number is genuinely ambiguous between series.
    List<string> AlsoMatched)
{
    public int Answered      => Dimensions.Count(d => d.State is ResolveState.Approved
                                                              or ResolveState.Unreviewed
                                                              or ResolveState.Rejected);
    public int ApprovedCount => Dimensions.Count(d => d.State == ResolveState.Approved);
    public int Indeterminate => Dimensions.Count(d => d.State == ResolveState.Indeterminate);
    public int NotApplicable => Dimensions.Count(d => d.State is ResolveState.NotApplicable
                                                              or ResolveState.NaForFamily);
    public int Unseeded      => Dimensions.Count(d => d.State == ResolveState.NoValue);
}

/// One decoded segment of a model number.
///
/// `IsDerived` segments are computed, not matched. They participate in determinant signatures
/// but are excluded from the grammar fit score, since a derived hit is not evidence that this
/// grammar is the right one for the number.
public record ParsedSegment(
    int Position, string SegmentKey, string Label, string? Code,
    bool IsRequired, string? Description, bool IsDerived = false);

/// Outcome of parsing a model number against one grammar version.
public record ParseResult(
    int GrammarVersionId, string VersionLabel, int FamilyId, string FamilyName,
    string Input,
    List<ParsedSegment> Segments,
    List<string> UninterpretedTokens,
    bool IsComplete,
    // The grammar's first segment (the series prefix, e.g. "19") matched at the very start of
    // the input. Without this anchor a foreign number can pick up a spurious partial match --
    // "1705R" otherwise matches "17" as a 1900 non-standard-outlet code -- and get presented
    // as a plausible best fit. A grammar that fails the anchor simply isn't for this number.
    bool AnchorMatched)
{
    public IEnumerable<ParsedSegment> Matched => Segments.Where(s => s.Code is not null);
    public IEnumerable<ParsedSegment> Missing => Segments.Where(s => s.Code is null);
    /// Real matches only — a derived segment resolving is not evidence of grammar fit.
    public int MatchedCount => Matched.Count(s => !s.IsDerived);
    public bool IsViable => AnchorMatched;
    /// How good a fit this grammar is — used to pick the best version when several are tried.
    public int Score => AnchorMatched ? MatchedCount * 10 - UninterpretedTokens.Count * 5
                                     : int.MinValue;
}

// Associates a document with an entire model family rather than with one dimension value --
// for things like Model Number Breakdown guides, which inform a model's identity but were
// never the source of any specific measurement.
public class ModelFamilyDocument
{
    [Key] public int Id { get; set; }
    public int FamilyId { get; set; }
    public int DocumentFileId { get; set; }
    public string? Notes { get; set; }
    public DocumentFile? DocumentFile { get; set; }
}

public class User
{
    [Key] public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";
    public bool Active { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ── Auth ────────────────────────────────────────────────────────

public static class Roles
{
    public const string Viewer   = "Viewer";
    public const string Approver = "Approver";
    public const string Admin    = "Admin";

    public static readonly string[] All = [Viewer, Approver, Admin];
}

public static class ReviewStatus
{
    public const string Unreviewed = "unreviewed";
    public const string Approved   = "approved";
    public const string Rejected   = "rejected";
}

// ── EAV dimension keys ─────────────────────────────────────────

public static class DimKey
{
    public const string Bore       = "nozzle_bore_id";
    public const string SeatHeight = "nozzle_height";
    public const string FaceToSeat = "nozzle_dimension";
    public const string DiscHolder = "disc_holder_stem_od";
    public const string DiscThick  = "disc_thickness";
    public const string GuideId    = "guide_id";
    public const string Tir        = "spindle_tir";
    public const string GuideClearance = "guide_disc_holder_clearance";

    public static readonly string[] All =
        [Bore, SeatHeight, FaceToSeat, DiscHolder, DiscThick, GuideId, Tir, GuideClearance];

    public static string Label(string key) => key switch
    {
        Bore       => "Nozzle Bore Diameter",
        SeatHeight => "Nozzle Seat Height",
        FaceToSeat => "Face-to-Seat Dim",
        DiscHolder => "Disc Holder OD",
        DiscThick  => "Disc Thickness",
        GuideId    => "Guide ID",
        Tir        => "Spindle TIR",
        GuideClearance => "Guide-Disc Clearance",
        _          => key
    };
}

// ── View models ────────────────────────────────────────────────

public class ValveDetail
{
    public ValveModel Model { get; set; } = new();
    public Dictionary<string, PublishedSpec> Specs { get; set; } = [];
    // spec id → resolved source text (doc title + page)
    public Dictionary<int, string> SourceText { get; set; } = [];
    // spec id → resolved corrected-source text (doc title + correction page notes)
    public Dictionary<int, string> CorrectedSourceText { get; set; } = [];
    public List<ModelNote> Notes { get; set; } = [];
    public List<DocumentFile> RelatedDocuments { get; set; } = [];
}

public enum DimStatus { Value, ConfirmedNa, NoSpec }

public record DimDisplay(
    string Label, string Key,
    decimal? Value, DimStatus Status,
    string? DimNotes,       // from published_specs.notes
    string? SourceText,     // resolved document + page
    int SpecId = 0,         // 0 = no published_specs row (nothing to review)
    string ReviewStatus = Data.ReviewStatus.Unreviewed,
    string? ReviewedBy = null,
    DateTime? ReviewedAt = null,
    string? RejectionNotes = null,
    decimal? CorrectedValue = null,
    string? CorrectedSourceText = null,
    string Unit = "in");

public static class DimHelper
{
    public static DimDisplay From(
        string key,
        PublishedSpec? spec,
        Dictionary<int, string> sourceTexts,
        Dictionary<int, string> correctedSourceTexts)
    {
        var label = DimKey.Label(key);
        if (spec is null)
            return new(label, key, null, DimStatus.NoSpec, null, null);

        var val = spec.Nominal ?? spec.SpecMin ?? spec.SpecMax;
        var sourceText = spec.Id > 0 && sourceTexts.TryGetValue(spec.Id, out var st) ? st : null;
        var correctedSourceText = spec.Id > 0 && correctedSourceTexts.TryGetValue(spec.Id, out var cst) ? cst : null;

        // ConfirmedNa is checked BEFORE the value: a reviewer's "not applicable" verdict wins
        // over whatever is sitting in the value columns, which lets MarkNotApplicableAsync stay
        // non-destructive and keep the original for audit.
        var status = spec.ConfirmedNa ? DimStatus.ConfirmedNa
                   : val.HasValue     ? DimStatus.Value
                   : DimStatus.NoSpec;

        // Always pass SpecId and the review fields through — including for NoSpec. A row with no
        // value is still reviewable (the reviewer needs to be able to confirm it as N/A), and
        // dropping SpecId here previously stranded 273 rows with no review controls at all.
        return new(label, key,
            status == DimStatus.Value ? val : null,
            status, spec.Notes, sourceText,
            spec.Id, spec.ReviewStatus, spec.ReviewedBy, spec.ReviewedAt,
            spec.RejectionNotes, spec.CorrectedValue, correctedSourceText);
    }

    public static IEnumerable<DimDisplay> All(
        Dictionary<string, PublishedSpec> specs,
        Dictionary<int, string> sourceTexts,
        Dictionary<int, string> correctedSourceTexts) =>
        DimKey.All.Select(k => From(k, specs.GetValueOrDefault(k), sourceTexts, correctedSourceTexts));
}
