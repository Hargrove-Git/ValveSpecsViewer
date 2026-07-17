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
    public string? Notes { get; set; }
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
    public bool Approved { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public ValveModel? Model { get; set; }
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

    public static readonly string[] All =
        [Bore, SeatHeight, FaceToSeat, DiscHolder, DiscThick, GuideId, Tir];

    public static string Label(string key) => key switch
    {
        Bore       => "Nozzle Bore Diameter",
        SeatHeight => "Nozzle Seat Height",
        FaceToSeat => "Face-to-Seat Dim",
        DiscHolder => "Disc Holder OD",
        DiscThick  => "Disc Thickness",
        GuideId    => "Guide ID",
        Tir        => "Spindle TIR",
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
    public List<ModelNote> Notes { get; set; } = [];
}

public enum DimStatus { Value, ConfirmedNa, NoSpec }

public record DimDisplay(
    string Label, string Key,
    decimal? Value, DimStatus Status,
    string? DimNotes,       // from published_specs.notes
    string? SourceText,     // resolved document + page
    int SpecId = 0,         // 0 = no published_specs row (nothing to approve)
    bool IsApproved = false,
    string? ApprovedBy = null,
    DateTime? ApprovedAt = null,
    string Unit = "in");

public static class DimHelper
{
    public static DimDisplay From(
        string key,
        PublishedSpec? spec,
        Dictionary<int, string> sourceTexts)
    {
        var label = DimKey.Label(key);
        if (spec is null)
            return new(label, key, null, DimStatus.NoSpec, null, null);

        var val = spec.Nominal ?? spec.SpecMin;
        var sourceText = spec.Id > 0 && sourceTexts.TryGetValue(spec.Id, out var st) ? st : null;

        if (val.HasValue)
            return new(label, key, val, DimStatus.Value, spec.Notes, sourceText,
                spec.Id, spec.Approved, spec.ApprovedBy, spec.ApprovedAt);
        if (spec.ConfirmedNa)
            return new(label, key, null, DimStatus.ConfirmedNa, spec.Notes, sourceText,
                spec.Id, spec.Approved, spec.ApprovedBy, spec.ApprovedAt);
        return new(label, key, null, DimStatus.NoSpec, spec.Notes, sourceText);
    }

    public static IEnumerable<DimDisplay> All(
        Dictionary<string, PublishedSpec> specs,
        Dictionary<int, string> sourceTexts) =>
        DimKey.All.Select(k => From(k, specs.GetValueOrDefault(k), sourceTexts));
}
