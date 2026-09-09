using Microsoft.EntityFrameworkCore;

namespace ValveSpecsViewer.Data;

public class DocumentService(ValveSpecsDb db)
{
    public Task<List<DocumentFile>> GetAllDocumentsAsync() =>
        db.DocumentFiles
          .Include(d => d.Manufacturer)
          .Include(d => d.DocumentType)
          .OrderBy(d => d.Manufacturer!.Name)
          .ThenBy(d => d.Title)
          .ToListAsync();

    public Task<DocumentFile?> GetDocumentAsync(int id) =>
        db.DocumentFiles.FirstOrDefaultAsync(d => d.Id == id);

    public async Task UploadFileAsync(int documentId, string fileName, string mimeType, byte[] bytes, string uploadedBy)
    {
        var doc = await db.DocumentFiles.FindAsync(documentId);
        if (doc is null) return;

        doc.FileName = fileName;
        doc.FileMimeType = mimeType;
        doc.FileData = bytes;
        doc.UploadedAt = DateTime.UtcNow;
        doc.UploadedBy = uploadedBy;

        await db.SaveChangesAsync();
    }

    public async Task DeleteFileAsync(int documentId)
    {
        var doc = await db.DocumentFiles.FindAsync(documentId);
        if (doc is null) return;

        doc.FileName = null;
        doc.FileData = null;
        doc.UploadedBy = null;

        await db.SaveChangesAsync();
    }
}
