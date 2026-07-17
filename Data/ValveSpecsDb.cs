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
            e.Property(x => x.Notes).HasColumnName("notes");
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
            e.Property(x => x.Approved).HasColumnName("approved");
            e.Property(x => x.ApprovedBy).HasColumnName("approved_by");
            e.Property(x => x.ApprovedAt).HasColumnName("approved_at");
            e.HasOne(x => x.Model).WithMany(x => x.PublishedSpecs).HasForeignKey(x => x.ModelId);
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

        b.Entity<ModelNote>(e => {
            e.ToTable("model_notes");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ModelId).HasColumnName("model_id");
            e.Property(x => x.NoteType).HasColumnName("note_type");
            e.Property(x => x.NoteText).HasColumnName("note_text");
            e.HasOne(x => x.Model).WithMany(x => x.ModelNotes).HasForeignKey(x => x.ModelId);
        });
    }
}
