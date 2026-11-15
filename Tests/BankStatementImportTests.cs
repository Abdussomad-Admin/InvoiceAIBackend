using Application.Banking;
using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Domain.Banking;

namespace Tests;

public sealed class BankStatementImportTests
{
    [Fact]
    public async Task Import_SavesValidRowsAndReturnsRowLevelValidationErrors()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var repository = new FakeBankStatementRepository();
        var handler = new ImportBankStatementHandler(repository, new FakeUnitOfWork(),
            new TestCurrentOrganization(organizationId), new TestCurrentUser(userId));

        var result = await handler.Handle("statement.csv", "hash-1", [
            new(2, "2026-11-01", "Customer transfer", "1200.00", "NGN", "REF-1200"),
            new(3, "not-a-date", "Bad date", "500", "NGN", null),
            new(4, "2026-11-02", "Bad amount", "five hundred", "NGN", null)
        ], CancellationToken.None);

        Assert.Equal(3, result.RowsRead);
        Assert.Equal(1, result.Imported);
        Assert.Equal([3, 4], result.Errors.Select(error => error.RowNumber));
        Assert.NotNull(repository.AddedBatch);
        Assert.Single(repository.AddedBatch.Lines);
        Assert.Equal("REF-1200", repository.AddedBatch.Lines[0].Reference);
    }

    [Fact]
    public async Task Import_RejectsTheSameStatementFileAgain()
    {
        var repository = new FakeBankStatementRepository { FileAlreadyImported = true };
        var handler = new ImportBankStatementHandler(repository, new FakeUnitOfWork(),
            new TestCurrentOrganization(Guid.NewGuid()), new TestCurrentUser(Guid.NewGuid()));

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle("statement.csv", "same-hash", [], CancellationToken.None));
        Assert.Null(repository.AddedBatch);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }

    private sealed class FakeBankStatementRepository : IBankStatementRepository
    {
        public bool FileAlreadyImported { get; init; }
        public BankStatementBatch? AddedBatch { get; private set; }

        public Task<bool> FileHashExistsAsync(string fileHash, CancellationToken cancellationToken = default)
            => Task.FromResult(FileAlreadyImported);
        public Task<BankStatementBatch?> GetBatchAsync(Guid batchId, CancellationToken cancellationToken = default)
            => Task.FromResult<BankStatementBatch?>(null);
        public Task<IReadOnlyList<BankStatementLine>> GetLinesAsync(Guid batchId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BankStatementLine>>([]);
        public Task<BankReconciliationCoverageDto> GetCoverageAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(BankReconciliationCoverageDto.Empty);
        public Task<BankStatementLine?> GetLineAsync(Guid lineId, CancellationToken cancellationToken = default)
            => Task.FromResult<BankStatementLine?>(null);
        public Task<bool> LockLineAsync(Guid lineId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
        public Task<bool> LockPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
        public Task<bool> PaymentCanMatchAsync(BankStatementLine line, Guid paymentId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
        public Task<BankStatementSuggestionsDto> GetSuggestionsAsync(BankStatementLine line, CancellationToken cancellationToken = default)
            => Task.FromResult(new BankStatementSuggestionsDto([], []));
        public void Add(BankStatementBatch batch) => AddedBatch = batch;
    }
}
