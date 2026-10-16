using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using BlueContract.Application.Common;
using BlueContract.Application.Receipts;
using BlueContract.Application.Receipts.Validation;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Integration;
using BlueContract.Domain.Organizations;
using BlueContract.Domain.Proposals;
using BlueContract.Domain.Receipts;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Integration;

public enum SimulationScenario
{
    /// <summary>Đúng hợp đồng, có hóa đơn khớp.</summary>
    Valid = 0,

    /// <summary>Có cảnh báo: đơn giá thấp hơn HĐ, hạn dùng gần, chưa có hóa đơn.</summary>
    Warning = 1,

    /// <summary>Không hợp lệ: đơn giá cao hơn HĐ và một mặt hàng ngoài hợp đồng.</summary>
    Invalid = 2,
}

public enum SimulationMode
{
    /// <summary>HIS đẩy phiếu qua API inbound.</summary>
    Push = 0,

    /// <summary>HIS lập phiếu, cổng kéo về bằng job định kỳ (mock HIS queue).</summary>
    Pull = 1,
}

public sealed record SimulateReceiptRequest(
    Guid? ContractId,
    ReceiptType Type = ReceiptType.Receipt,
    SimulationScenario Scenario = SimulationScenario.Valid,
    SimulationMode Mode = SimulationMode.Push,
    bool WithInvoice = true,
    int MaxLines = 3);

public sealed record SimulateReceiptResultDto(HisReceiptPayload Payload, HisIngestResultDto? Result, bool Queued);

/// <summary>
/// Endpoint dev "POST /api/dev/his/simulate-receipt" (spec §7.3): builds a realistic HIS receipt for an effective contract
/// (quantities from the open call order when there is one) and pushes it through the real ingestion, or queues it in the
/// mock HIS for the pull job.
/// </summary>
public sealed class HisSimulator(
    IAppDbContext db,
    HisReceiptIngestion ingestion,
    IntegrationLogWriter logs,
    IMockHisReceiptQueue queue,
    IClock clock)
{
    public async Task<Result<SimulateReceiptResultDto>> SimulateAsync(SimulateReceiptRequest r, CancellationToken ct)
    {
        var contract = await PickContractAsync(r.ContractId, ct);
        if (contract is null)
        {
            return Error.Validation("SIMULATION_NO_CONTRACT");
        }

        var payload = await BuildAsync(contract, r, ct);
        if (payload is null)
        {
            return Error.Validation(r.Type == ReceiptType.Return ? "SIMULATION_NOTHING_TO_RETURN" : "SIMULATION_NOTHING_REMAINING");
        }

        if (r.Mode == SimulationMode.Pull)
        {
            queue.Add(payload);
            return new SimulateReceiptResultDto(payload, null, true);
        }

        var body = JsonSerializer.Serialize(payload, IntegrationLogService.Json);
        var logId = await logs.BeginAsync(IntegrationDirection.Inbound, IntegrationLog.Operations.PushWarehouseReceipt, "POST", "/api/dev/his/simulate-receipt",
            payload.HisReceiptId, body, null, "Giả lập HIS (dev)", ct);
        var watch = Stopwatch.StartNew();
        var result = await ingestion.IngestAsync(payload, ct);
        await logs.CompleteAsync(logId, result.IsSuccess ? IntegrationLogStatus.Success : IntegrationLogStatus.Failed, result.IsSuccess ? 200 : 400,
            result.IsSuccess ? JsonSerializer.Serialize(result.Value, IntegrationLogService.Json) : null, result.Error?.Code,
            result.Error is null ? null : ErrorMessages.Get(result.Error.Code), watch.ElapsedMilliseconds, result.IsSuccess ? result.Value.ReceiptId : null, ct);
        return result.IsSuccess ? new SimulateReceiptResultDto(payload, result.Value, false) : result.Error!;
    }

    private async Task<Contract?> PickContractAsync(Guid? contractId, CancellationToken ct)
    {
        var query = db.Contracts.AsNoTracking().Include(c => c.Items).Where(c => c.Status == ContractStatus.Effective);
        if (contractId is { } id)
        {
            return await query.FirstOrDefaultAsync(c => c.Id == id, ct);
        }

        return await query.Where(c => c.RequiresWarehouseReceipt && c.Items.Any(i => i.Quantity > i.ReceivedQuantity))
            .OrderByDescending(c => db.CallOrders.Any(o => o.SupplierId == c.SupplierId
                && (o.Status == CallOrderStatus.SupplierConfirmed || o.Status == CallOrderStatus.PartiallyConfirmed)))
            .ThenByDescending(c => c.EffectiveAt)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>The main warehouse of the contract's domain (thuốc → kho thuốc, VTYT → kho VTYT...).</summary>
    private async Task<string> DefaultWarehouseAsync(Contract contract, CancellationToken ct)
    {
        var domain = await db.ItemCategories.Where(c => c.Id == contract.CategoryId).Select(c => (ProcurementDomain?)c.Domain).FirstOrDefaultAsync(ct);
        WarehouseType? type = domain switch
        {
            ProcurementDomain.Drug => WarehouseType.Drug,
            ProcurementDomain.MedicalSupply => WarehouseType.MedicalSupply,
            ProcurementDomain.Chemical => WarehouseType.Chemical,
            ProcurementDomain.Equipment => WarehouseType.Asset,
            _ => null,
        };
        var warehouses = db.Warehouses.AsNoTracking().OrderBy(w => w.Code);
        return await warehouses.Where(w => type == null || w.Type == type).Select(w => w.HisWarehouseCode ?? w.Code).FirstOrDefaultAsync(ct)
            ?? await warehouses.Select(w => w.HisWarehouseCode ?? w.Code).FirstAsync(ct);
    }

    private async Task<HisReceiptPayload?> BuildAsync(Contract contract, SimulateReceiptRequest r, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var supplier = await db.Suppliers.AsNoTracking().FirstAsync(s => s.Id == contract.SupplierId, ct);
        var bid = contract.BidDecisionId is { } bidId ? await db.BidDecisions.AsNoTracking().Where(b => b.Id == bidId).Select(b => b.Number).FirstOrDefaultAsync(ct) : null;
        var itemIds = contract.Items.Select(i => i.ItemId).ToList();
        var keys = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.HisKey, ct);
        var received = await ReceiptValidationLoader.NetReceivedAsync(ReceiptValidationLoader.Counted(db.WarehouseReceipts.AsNoTracking()).Where(x => x.ContractId == contract.Id), ct);

        // Quantities: the open call order of the supplier (valid deliveries), otherwise a slice of the remaining quantity.
        CallOrder? order = null;
        var quantities = new List<(ContractItem Item, decimal Quantity)>();
        if (r.Type == ReceiptType.Receipt)
        {
            order = await db.CallOrders.AsNoTracking().Include(o => o.Lines)
                .Where(o => o.SupplierId == contract.SupplierId && (o.Status == CallOrderStatus.SupplierConfirmed || o.Status == CallOrderStatus.PartiallyConfirmed)
                    && o.Lines.Any(l => l.ContractId == contract.Id && l.DeliveredQuantity < (l.ConfirmedQuantity ?? l.Quantity)))
                .OrderBy(o => o.CallDate).FirstOrDefaultAsync(ct);
            if (order is not null)
            {
                quantities.AddRange(order.Lines.Where(l => l.ContractId == contract.Id)
                    .Select(l => (Item: contract.Items.First(i => i.Id == l.ContractItemId), Quantity: (l.ConfirmedQuantity ?? l.Quantity) - l.DeliveredQuantity))
                    .Where(x => x.Quantity > 0));
            }
            else
            {
                // Valid: items still called but not delivered (no "vượt SL đã gọi"); other scenarios: the other items.
                var rows = contract.Items
                    .Select(i => (Item: i, Remaining: i.Quantity - received.GetValueOrDefault(i.Id), Open: i.CalledQuantity - received.GetValueOrDefault(i.Id)))
                    .Where(x => x.Remaining > 0)
                    .ToList();
                if (r.Scenario == SimulationScenario.Valid && rows.Any(x => x.Open > 0))
                {
                    rows = [.. rows.Where(x => x.Open > 0)];
                }

                var ordered = r.Scenario == SimulationScenario.Valid
                    ? rows.OrderByDescending(x => x.Open).ThenBy(x => x.Item.ItemCode)
                    : rows.OrderBy(x => x.Open).ThenBy(x => x.Item.ItemCode);
                quantities.AddRange(ordered.Select(x => (x.Item, Quantity: r.Scenario == SimulationScenario.Valid && x.Open > 0
                    ? Math.Min(x.Open, Math.Max(1, decimal.Round(x.Open / 2, 0)))
                    : Math.Min(x.Remaining, Math.Max(1, decimal.Round(x.Remaining * 0.1m, 0))))));
            }
        }
        else
        {
            quantities.AddRange(contract.Items.Select(i => (Item: i, Received: received.GetValueOrDefault(i.Id)))
                .Where(x => x.Received > 0)
                .Select(x => (x.Item, Quantity: Math.Max(1, decimal.Round(x.Received * 0.1m, 0, MidpointRounding.ToZero)))));
        }

        quantities = [.. quantities.Take(Math.Clamp(r.MaxLines, 1, 20))];
        if (quantities.Count == 0)
        {
            return null;
        }

        var warehouseCode = order is not null
            ? await db.Warehouses.Where(w => w.Id == order.WarehouseId).Select(w => w.HisWarehouseCode ?? w.Code).FirstAsync(ct)
            : await DefaultWarehouseAsync(contract, ct);
        var serial = RandomNumberGenerator.GetInt32(100_000, 999_999);
        var lines = quantities.Select((x, index) =>
        {
            var price = r.Scenario switch
            {
                SimulationScenario.Warning when index == 0 => decimal.Round(x.Item.UnitPrice * 0.98m, 0),
                SimulationScenario.Invalid when index == 0 => decimal.Round(x.Item.UnitPrice * 1.05m, 0) + 1,
                _ => x.Item.UnitPrice,
            };
            var expiry = r.Scenario == SimulationScenario.Warning && index == 0 ? today.AddDays(60) : today.AddYears(2);
            return new HisReceiptLinePayload(keys.GetValueOrDefault(x.Item.ItemId), x.Item.ItemCode, x.Item.ItemName, x.Item.Unit,
                $"L{today:yyMM}{index + 1:00}", expiry, x.Quantity, price, x.Item.VatRate);
        }).ToList();
        if (r.Scenario == SimulationScenario.Invalid)
        {
            lines.Add(new HisReceiptLinePayload("KX999999", "NGOAI-HD", "Mặt hàng không thuộc hợp đồng", "Hộp", "LX01", today.AddYears(2), 5, 100_000, 5));
        }

        var total = lines.Sum(l => decimal.Round(l.Quantity * l.UnitPrice, 0, MidpointRounding.AwayFromZero));
        var vat = lines.Sum(l => decimal.Round(decimal.Round(l.Quantity * l.UnitPrice, 0, MidpointRounding.AwayFromZero) * (l.VatRate ?? 0) / (100 + (l.VatRate ?? 0)), 0));
        var invoice = r.WithInvoice && r.Scenario != SimulationScenario.Warning && r.Type == ReceiptType.Receipt
            ? new HisInvoicePayload("1C26TAA", serial.ToString("0000000", System.Globalization.CultureInfo.InvariantCulture), today, total - vat, vat, total, supplier.TaxCode)
            : null;
        var prefix = r.Type == ReceiptType.Return ? "HT" : "PN";
        return new HisReceiptPayload(
            $"SIM-{clock.UtcNow:yyyyMMddHHmmss}-{serial}", $"{prefix}{serial}", r.Type, today, warehouseCode, bid, supplier.TaxCode, supplier.Name,
            contract.Number, order?.Number, "Nhân viên giao hàng " + (supplier.ShortName ?? supplier.Name),
            r.Type == ReceiptType.Return ? "Hoàn trả hàng không đạt chất lượng" : "Phiếu giả lập từ HIS", invoice, lines);
    }
}
