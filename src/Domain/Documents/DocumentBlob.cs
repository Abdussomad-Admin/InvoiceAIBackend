using Domain.Common;

namespace Domain.Documents;

// The file's bytes, kept apart from SourceDocument so that listing documents never loads file contents.
public sealed class DocumentBlob : BaseEntity, ITenantOwned
{
    private DocumentBlob() { }

    public Guid OrganizationId { get; private set; }
    public Guid DocumentId { get; private set; }
    public byte[] Content { get; private set; } = null!;

    public static DocumentBlob Create(Guid organizationId, Guid documentId, byte[] content)
    {
        if (organizationId == Guid.Empty || documentId == Guid.Empty)
            throw new DomainException("Organization and document are required.");
        if (content is null || content.Length == 0)
            throw new DomainException("The file content is empty.");

        return new DocumentBlob { OrganizationId = organizationId, DocumentId = documentId, Content = content };
    }
}