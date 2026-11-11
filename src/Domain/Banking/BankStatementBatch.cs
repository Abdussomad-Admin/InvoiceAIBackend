using Domain.Common;

namespace Domain.Banking;

public sealed class BankStatementBatch : BaseEntity, ITenantOwned
{
    private BankStatementBatch() { }

    public Guid OrganizationId { get; private set; }
    public string FileName { get; private set; } = null!;
    public string FileHash { get; private set; } = null!;
    public Guid ImportedByUserId { get; private set; }
    public int ImportedLineCount { get; private set; }
    public IReadOnlyList<BankStatementLine> Lines => _lines;
    private readonly List<BankStatementLine> _lines = [];

    public static BankStatementBatch Create(Guid organizationId, Guid userId, string fileName, string fileHash)
    {
        if (organizationId == Guid.Empty || userId == Guid.Empty)
            throw new DomainException("Organization and user are required.");
        return new BankStatementBatch
        {
            OrganizationId = organizationId,
            ImportedByUserId = userId,
            FileName = Guard.NotBlank(Path.GetFileName(fileName.Replace('\\', '/')), nameof(FileName), 200),
            FileHash = Guard.NotBlank(fileHash, nameof(FileHash), 64)
        };
    }

    public void AddLine(BankStatementLine line)
    {
        if (line.OrganizationId != OrganizationId || line.StatementBatchId != Id)
            throw new DomainException("The statement line must belong to this batch and organization.");
        _lines.Add(line);
        ImportedLineCount = _lines.Count;
    }
}
