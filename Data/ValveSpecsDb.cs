using Microsoft.EntityFrameworkCore;

namespace ValveSpecsViewer.Data;

public class ValveSpecsDb : DbContext
{
    public ValveSpecsDb(DbContextOptions<ValveSpecsDb> options) : base(options) { }

    public DbSet<Manufacturer>  Manufacturers  => Set<Manufacturer>();
    public DbSet<ModelFamily>   ModelFamilies  => Set<ModelFamily>();
    public DbSet<ValveModel>    Models         => Set<ValveModel>();
    public DbSet<DocumentFile>  DocumentFiles  => Set<DocumentFile>();
    public DbSet<PublishedSpec> PublishedSpecs => Set<PublishedSpec>();
    public DbSet<SpecSource>    SpecSources    => Set<SpecSource>();
    public DbSet<ModelNote>     ModelNotes     => Set<ModelNote>();
    public DbSet<User>          Users          => Set<User>();
    public DbSet<DocumentType>  DocumentTypes  => Set<DocumentType>();
    public DbSet<ModelFamilyDocument> ModelFamilyDocuments => Set<ModelFamilyDocument>();
    public DbSet<FamilyDimension>      FamilyDimensions      => Set<FamilyDimension>();
    public DbSet<DimensionDeterminant> DimensionDeterminants => Set<DimensionDeterminant>();
    public DbSet<SpecValue>            SpecValues            => Set<SpecValue>();
    public DbSet<GrammarVersion>      GrammarVersions      => Set<GrammarVersion>();
    public DbSet<SegmentDefinition>   SegmentDefinitions   => Set<SegmentDefinition>();
    public DbSet<SegmentValue>        SegmentValues        => Set<SegmentValue>();
    public DbSet<SegmentCondition>    SegmentConditions    => Set<SegmentCondition>();
    public DbSet<DerivedSegment>      DerivedSegments      => Set<DerivedSegment>();
    public DbSet<DerivedSegmentRule>  DerivedSegmentRules  => Set<DerivedSegmentRule>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Manufacturer>(e => {
            e.ToTable("manufacturers");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Notes).HasColumnName("notes");
        });

        b.Entity<ModelFamily>(e => {
            e.ToTable("model_families");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ManufacturerId).HasColumnName("manufacturer_id");
            e.Property(x => x.FamilyName).HasColumnName("family_name");
            e.Property(x => x.Description).HasColumnName("description");
            e.HasOne(x => x.Manufacturer).WithMany().HasForeignKey(x => x.ManufacturerId);
        });

        b.Entity<ValveModel>(e => {
            e.ToTable("models");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ManufacturerId).HasColumnName("manufacturer_id");
            e.Property(x => x.FamilyId).HasColumnName("family_id");
            e.Property(x => x.ModelNumber).HasColumnName("model_number");
            e.Property(x => x.LifecycleStatus).HasColumnName("lifecycle_status");
            e.Property(x => x.Confidence).HasColumnName("confidence");
            e.Property(x => x.VerificationStatus).HasColumnName("verification_status");
            e.Property(x => x.VerificationNotes).HasColumnName("verification_notes");
            e.Property(x => x.Flags).HasColumnName("flags");
            e.Property(x => x.ModelRejected).HasColumnName("model_rejected");
            e.Property(x => x.ModelRejectedBy).HasColumnName("model_rejected_by");
            e.Property(x => x.ModelRejectedAt).HasColumnName("model_rejected_at");
            e.Property(x => x.ModelRejectionNotes).HasColumnName("model_rejection_notes");
            e.HasOne(x => x.Manufacturer).WithMany().HasForeignKey(x => x.ManufacturerId);
            e.HasOne(x => x.ModelFamily).WithMany(x => x.Models).HasForeignKey(x => x.FamilyId);
        });

        b.Entity<DocumentFile>(e => {
            e.ToTable("document_files");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ManufacturerId).HasColumnName("manufacturer_id");
            e.Property(x => x.DocumentTypeId).HasColumnName("document_type_id");
            e.Property(x => x.Title).HasColumnName("title");
            e.Property(x => x.DocumentNumber).HasColumnName("document_number");
            e.Property(x => x.Revision).HasColumnName("revision");
            e.Property(x => x.RevisionDate).HasColumnName("revision_date");
            e.Property(x => x.PageCount).HasColumnName("page_count");
            e.Property(x => x.FileName).HasColumnName("file_name");
            e.Property(x => x.FileMimeType).HasColumnName("file_mime_type");
            e.Property(x => x.FileData).HasColumnName("file_data");
            e.Property(x => x.UploadedAt).HasColumnName("uploaded_at");
            e.Property(x => x.UploadedBy).HasColumnName("uploaded_by");
            e.Property(x => x.Notes).HasColumnName("notes");
            e.HasOne(x => x.Manufacturer).WithMany().HasForeignKey(x => x.ManufacturerId);
            e.HasOne(x => x.DocumentType).WithMany().HasForeignKey(x => x.DocumentTypeId);
        });

        b.Entity<DocumentType>(e => {
            e.ToTable("document_types");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TypeName).HasColumnName("type_name");
            e.Property(x => x.Description).HasColumnName("description");
        });

        b.Entity<PublishedSpec>(e => {
            e.ToTable("published_specs");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ModelId).HasColumnName("model_id");
            e.Property(x => x.Dimension).HasColumnName("dimension");
            e.Property(x => x.SpecMin).HasColumnName("spec_min").HasPrecision(10, 5);
            e.Property(x => x.SpecMax).HasColumnName("spec_max").HasPrecision(10, 5);
            e.Property(x => x.Nominal).HasColumnName("nominal").HasPrecision(10, 5);
            e.Property(x => x.Unit).HasColumnName("unit");
            e.Property(x => x.ConfirmedNa).HasColumnName("confirmed_na");
            e.Property(x => x.SourcePriority).HasColumnName("source_priority");
            e.Property(x => x.Source).HasColumnName("source");
            e.Property(x => x.Notes).HasColumnName("notes");
            e.Property(x => x.ReviewStatus).HasColumnName("review_status");
            e.Property(x => x.ReviewedBy).HasColumnName("reviewed_by");
            e.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
            e.Property(x => x.RejectionNotes).HasColumnName("rejection_notes");
            e.Property(x => x.CorrectedValue).HasColumnName("corrected_value").HasPrecision(10, 5);
            e.Property(x => x.CorrectedDocumentFileId).HasColumnName("corrected_document_file_id");
            e.Property(x => x.CorrectedPageNotes).HasColumnName("corrected_page_notes");
            e.Property(x => x.CorrectedBy).HasColumnName("corrected_by");
            e.Property(x => x.CorrectedAt).HasColumnName("corrected_at");
            e.Property(x => x.TrimVariant).HasColumnName("trim_variant");
            e.Property(x => x.ConfirmedNaBy).HasColumnName("confirmed_na_by");
            e.Property(x => x.ConfirmedNaAt).HasColumnName("confirmed_na_at");
            e.HasOne(x => x.Model).WithMany(x => x.PublishedSpecs).HasForeignKey(x => x.ModelId);
            e.HasOne(x => x.CorrectedDocumentFile).WithMany().HasForeignKey(x => x.CorrectedDocumentFileId);
        });

        b.Entity<SpecSource>(e => {
            e.ToTable("spec_sources");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.PublishedSpecId).HasColumnName("published_spec_id");
            e.Property(x => x.DocumentFileId).HasColumnName("document_file_id");
            e.Property(x => x.PageLabel).HasColumnName("page_label");
            e.Property(x => x.TableReference).HasColumnName("table_reference");
            e.HasOne(x => x.DocumentFile).WithMany().HasForeignKey(x => x.DocumentFileId);
        });

        b.Entity<FamilyDimension>(e => {
            e.ToTable("family_dimensions");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FamilyId).HasColumnName("family_id");
            e.Property(x => x.Dimension).HasColumnName("dimension");
            e.Property(x => x.IsTracked).HasColumnName("is_tracked");
            e.Property(x => x.NaForFamily).HasColumnName("na_for_family");
            e.Property(x => x.SourceNote).HasColumnName("source_note");
            e.HasMany(x => x.Determinants).WithOne(x => x.FamilyDimension!).HasForeignKey(x => x.FamilyDimensionId);
        });

        b.Entity<DimensionDeterminant>(e => {
            e.ToTable("dimension_determinants");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FamilyDimensionId).HasColumnName("family_dimension_id");
            e.Property(x => x.SegmentKey).HasColumnName("segment_key");
            e.Property(x => x.Ordinal).HasColumnName("ordinal");
        });

        b.Entity<SpecValue>(e => {
            e.ToTable("spec_values");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FamilyId).HasColumnName("family_id");
            e.Property(x => x.Dimension).HasColumnName("dimension");
            e.Property(x => x.DeterminantSignature).HasColumnName("determinant_signature");
            e.Property(x => x.SpecMin).HasColumnName("spec_min").HasPrecision(10, 5);
            e.Property(x => x.SpecMax).HasColumnName("spec_max").HasPrecision(10, 5);
            e.Property(x => x.Nominal).HasColumnName("nominal").HasPrecision(10, 5);
            e.Property(x => x.Unit).HasColumnName("unit");
            e.Property(x => x.ConfirmedNa).HasColumnName("confirmed_na");
            e.Property(x => x.NaStatement).HasColumnName("na_statement");
            e.Property(x => x.Source).HasColumnName("source");
            e.Property(x => x.SourceDocumentFileId).HasColumnName("source_document_file_id");
            e.Property(x => x.Notes).HasColumnName("notes");
            e.Property(x => x.ReviewStatus).HasColumnName("review_status");
            e.Property(x => x.ReviewedBy).HasColumnName("reviewed_by");
            e.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
            e.Property(x => x.ReviewNotes).HasColumnName("review_notes");
            e.Property(x => x.CorrectedValue).HasColumnName("corrected_value").HasPrecision(10, 5);
            e.Property(x => x.CorrectedBy).HasColumnName("corrected_by");
            e.Property(x => x.CorrectedAt).HasColumnName("corrected_at");
            e.Ignore(x => x.Value);
        });

        b.Entity<GrammarVersion>(e => {
            e.ToTable("grammar_versions");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FamilyId).HasColumnName("family_id");
            e.Property(x => x.VersionLabel).HasColumnName("version_label");
            e.Property(x => x.SourceDocumentFileId).HasColumnName("source_document_file_id");
            e.Property(x => x.EffectiveFrom).HasColumnName("effective_from");
            e.Property(x => x.IsCurrent).HasColumnName("is_current");
            e.Property(x => x.Notes).HasColumnName("notes");
            e.HasOne(x => x.Family).WithMany().HasForeignKey(x => x.FamilyId);
            e.HasMany(x => x.Segments).WithOne(x => x.GrammarVersion!).HasForeignKey(x => x.GrammarVersionId);
            e.HasMany(x => x.DerivedSegments).WithOne(x => x.GrammarVersion!).HasForeignKey(x => x.GrammarVersionId);
        });

        b.Entity<SegmentDefinition>(e => {
            e.ToTable("segment_definitions");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.GrammarVersionId).HasColumnName("grammar_version_id");
            e.Property(x => x.Position).HasColumnName("position");
            e.Property(x => x.SegmentKey).HasColumnName("segment_key");
            e.Property(x => x.Label).HasColumnName("label");
            e.Property(x => x.IsRequired).HasColumnName("is_required");
            e.Property(x => x.DashBefore).HasColumnName("dash_before");
            e.Property(x => x.Pattern).HasColumnName("pattern");
            e.Property(x => x.PatternDescription).HasColumnName("pattern_description");
            e.Property(x => x.Notes).HasColumnName("notes");
            e.Ignore(x => x.IsFreeForm);
            e.HasMany(x => x.Values).WithOne(x => x.SegmentDefinition!).HasForeignKey(x => x.SegmentDefinitionId);
            e.HasMany(x => x.Conditions).WithOne(x => x.SegmentDefinition!).HasForeignKey(x => x.SegmentDefinitionId);
        });

        b.Entity<SegmentCondition>(e => {
            e.ToTable("segment_conditions");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.SegmentDefinitionId).HasColumnName("segment_definition_id");
            e.Property(x => x.SourceSegmentKey).HasColumnName("source_segment_key");
            e.Property(x => x.MatchType).HasColumnName("match_type");
            e.Property(x => x.MatchValue).HasColumnName("match_value");
            e.Property(x => x.Notes).HasColumnName("notes");
        });

        b.Entity<SegmentValue>(e => {
            e.ToTable("segment_values");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.SegmentDefinitionId).HasColumnName("segment_definition_id");
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.Description).HasColumnName("description");
        });

        b.Entity<DerivedSegment>(e => {
            e.ToTable("derived_segments");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.GrammarVersionId).HasColumnName("grammar_version_id");
            e.Property(x => x.SegmentKey).HasColumnName("segment_key");
            e.Property(x => x.Label).HasColumnName("label");
            e.Property(x => x.Notes).HasColumnName("notes");
            e.HasMany(x => x.Rules).WithOne(x => x.DerivedSegment!).HasForeignKey(x => x.DerivedSegmentId);
        });

        b.Entity<DerivedSegmentRule>(e => {
            e.ToTable("derived_segment_rules");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.DerivedSegmentId).HasColumnName("derived_segment_id");
            e.Property(x => x.Priority).HasColumnName("priority");
            e.Property(x => x.SourceSegmentKey).HasColumnName("source_segment_key");
            e.Property(x => x.MatchType).HasColumnName("match_type");
            e.Property(x => x.MatchValue).HasColumnName("match_value");
            e.Property(x => x.ResultCode).HasColumnName("result_code");
            e.Property(x => x.Notes).HasColumnName("notes");
        });

        b.Entity<ModelFamilyDocument>(e => {
            e.ToTable("model_family_documents");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FamilyId).HasColumnName("family_id");
            e.Property(x => x.DocumentFileId).HasColumnName("document_file_id");
            e.Property(x => x.Notes).HasColumnName("notes");
            e.HasOne(x => x.DocumentFile).WithMany().HasForeignKey(x => x.DocumentFileId);
        });

        b.Entity<ModelNote>(e => {
            e.ToTable("model_notes");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ModelId).HasColumnName("model_id");
            e.Property(x => x.NoteType).HasColumnName("note_type");
            e.Property(x => x.NoteText).HasColumnName("note_text");
            e.HasOne(x => x.Model).WithMany(x => x.ModelNotes).HasForeignKey(x => x.ModelId);
        });

        b.Entity<User>(e => {
            e.ToTable("users");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Username).HasColumnName("username");
            e.Property(x => x.Password).HasColumnName("password");
            e.Property(x => x.DisplayName).HasColumnName("display_name");
            e.Property(x => x.Role).HasColumnName("role");
            e.Property(x => x.Active).HasColumnName("active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });
    }
}
