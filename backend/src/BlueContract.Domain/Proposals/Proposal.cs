using BlueContract.Domain.Common;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Workflows;

namespace BlueContract.Domain.Proposals;

public enum ProposalStatus
{
    Draft = 0,

    /// <summary>Đang ký (người lập ký số trước khi gửi).</summary>
    Signing = 1,

    /// <summary>Đã gửi — chờ tổng hợp.</summary>
    Submitted = 2,

    /// <summary>Đã được gộp vào một phiếu tổng hợp.</summary>
    Aggregated = 3,

    Returned = 4,
    Cancelled = 5,
}

/// <summary>Input of a proposal line; stock figures come from the HIS (X–N–T) at drafting time.</summary>
public sealed record ProposalLineDraft(
    Guid ContractId,
    string ContractNumber,
    Guid ContractItemId,
    Guid ItemId,
    string ItemCode,
    string ItemName,
    string Unit,
    string? HisKey,
    decimal OpeningStock,
    decimal Received,
    decimal Issued,
    decimal CurrentStock,
    decimal ContractRemaining,
    decimal ProposedQuantity,
    string? Note);

/// <summary>
/// Phiếu đề xuất của khoa/phòng (spec §5.4, §6.2 B1) over one domain; lines may come from several contracts of the domain
/// (ADR-035). Rule: SL đề xuất ≤ SL còn lại của hợp đồng.
/// </summary>
public sealed class Proposal : Entity
{
    private readonly List<ProposalLine> _lines = [];

    private Proposal()
    {
        Number = string.Empty;
        PeriodText = string.Empty;
    }

    public Proposal(string number, Guid departmentId, Guid? facilityId, Guid warehouseId, ProcurementDomain domain, DateOnly periodFrom, DateOnly periodTo, string? createdByName)
    {
        if (periodTo < periodFrom)
        {
            throw new DomainException("PROPOSAL_PERIOD_INVALID");
        }

        Number = Guard.Required(number, "PROPOSAL_NUMBER_REQUIRED");
        DepartmentId = departmentId;
        FacilityId = facilityId;
        WarehouseId = warehouseId;
        Domain = domain;
        PeriodFrom = periodFrom;
        PeriodTo = periodTo;
        PeriodText = $"Từ {periodFrom:dd/MM/yyyy} đến {periodTo:dd/MM/yyyy}";
        CreatedByName = createdByName;
        Status = ProposalStatus.Draft;
    }

    public string Number { get; private set; }

    public Guid DepartmentId { get; private set; }

    public Guid? FacilityId { get; private set; }

    /// <summary>Kho lấy số liệu Xuất – Nhập – Tồn.</summary>
    public Guid WarehouseId { get; private set; }

    public ProcurementDomain Domain { get; private set; }

    public DateOnly PeriodFrom { get; private set; }

    public DateOnly PeriodTo { get; private set; }

    public string PeriodText { get; private set; }

    public string? Note { get; private set; }

    public string? CreatedByName { get; private set; }

    public ProposalStatus Status { get; private set; }

    public Guid? DocumentId { get; private set; }

    public Guid? SummaryId { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public IReadOnlyCollection<ProposalLine> Lines => _lines;

    public bool IsEditable => Status is ProposalStatus.Draft or ProposalStatus.Returned;

    public void SetNote(string? note)
    {
        EnsureEditable();
        Note = Guard.Optional(note);
    }

    public void SetLines(IReadOnlyCollection<ProposalLineDraft> lines)
    {
        EnsureEditable();
        if (lines.Count == 0)
        {
            throw new DomainException("PROPOSAL_LINES_REQUIRED");
        }

        if (lines.GroupBy(l => l.ContractItemId).Any(g => g.Count() > 1))
        {
            throw new DomainException("PROPOSAL_LINE_DUPLICATE");
        }

        _lines.Clear();
        foreach (var line in lines)
        {
            _lines.Add(new ProposalLine(Id, line));
        }
    }

    public void AttachDocument(Guid documentId) => DocumentId = documentId;

    public void ApplyWorkflow(WorkflowStatus status, DateTimeOffset now)
    {
        if (Status is ProposalStatus.Aggregated or ProposalStatus.Cancelled)
        {
            return;
        }

        switch (status)
        {
            case WorkflowStatus.Running:
                Status = ProposalStatus.Signing;
                break;
            case WorkflowStatus.Completed:
                Status = ProposalStatus.Submitted;
                SubmittedAt ??= now;
                break;
            case WorkflowStatus.Returned:
                Status = ProposalStatus.Returned;
                break;
            case WorkflowStatus.Cancelled:
                Status = ProposalStatus.Draft;
                break;
        }
    }

    public void MarkAggregated(Guid summaryId)
    {
        if (Status != ProposalStatus.Submitted)
        {
            throw new DomainException("PROPOSAL_NOT_SUBMITTED");
        }

        Status = ProposalStatus.Aggregated;
        SummaryId = summaryId;
    }

    /// <summary>The summary was cancelled: the proposal can be aggregated again.</summary>
    public void ReleaseFromSummary()
    {
        if (Status == ProposalStatus.Aggregated)
        {
            Status = ProposalStatus.Submitted;
            SummaryId = null;
        }
    }

    public void Cancel()
    {
        if (Status is ProposalStatus.Aggregated or ProposalStatus.Signing)
        {
            throw new DomainException("PROPOSAL_NOT_CANCELLABLE");
        }

        Status = ProposalStatus.Cancelled;
    }

    private void EnsureEditable()
    {
        if (!IsEditable)
        {
            throw new DomainException("PROPOSAL_LOCKED");
        }
    }
}

public sealed class ProposalLine
{
    private ProposalLine()
    {
        ContractNumber = string.Empty;
        ItemCode = string.Empty;
        ItemName = string.Empty;
        Unit = string.Empty;
    }

    internal ProposalLine(Guid proposalId, ProposalLineDraft d)
    {
        if (d.ProposedQuantity <= 0)
        {
            throw new DomainException("PROPOSAL_QUANTITY_INVALID");
        }

        if (d.ProposedQuantity > d.ContractRemaining)
        {
            throw new DomainException("PROPOSAL_QTY_EXCEEDS_CONTRACT");
        }

        Id = Guid.CreateVersion7();
        ProposalId = proposalId;
        ContractId = d.ContractId;
        ContractNumber = d.ContractNumber;
        ContractItemId = d.ContractItemId;
        ItemId = d.ItemId;
        ItemCode = d.ItemCode;
        ItemName = d.ItemName;
        Unit = d.Unit;
        HisKey = d.HisKey;
        OpeningStock = d.OpeningStock;
        Received = d.Received;
        Issued = d.Issued;
        CurrentStock = d.CurrentStock;
        ContractRemaining = d.ContractRemaining;
        ProposedQuantity = d.ProposedQuantity;
        Note = d.Note;
    }

    public Guid Id { get; private set; }

    public Guid ProposalId { get; private set; }

    public Guid ContractId { get; private set; }

    public string ContractNumber { get; private set; }

    public Guid ContractItemId { get; private set; }

    public Guid ItemId { get; private set; }

    public string ItemCode { get; private set; }

    public string ItemName { get; private set; }

    public string Unit { get; private set; }

    public string? HisKey { get; private set; }

    public decimal OpeningStock { get; private set; }

    public decimal Received { get; private set; }

    public decimal Issued { get; private set; }

    public decimal CurrentStock { get; private set; }

    /// <summary>SL còn lại của hợp đồng tại thời điểm lập phiếu.</summary>
    public decimal ContractRemaining { get; private set; }

    public decimal ProposedQuantity { get; private set; }

    public string? Note { get; private set; }
}
