using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

public class SalesContractTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ISalesContractService Contracts => GetService<ISalesContractService>();

    private sealed class Setup
    {
        public Guid Location;
        public Guid Item;
        public Guid Customer;
        public Guid OtherCustomer;
        public Guid Outlet;
        public Guid OtherOutlet;
        public Guid Currency;
        public Guid LegalEntity;
        public Guid Contract;
    }

    private async Task<Setup> SetupAsync()
    {
        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Euro";
        currency.Code = $"E{Guid.NewGuid():N}"[..2].ToUpperInvariant();
        currency.Symbol = "€";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Germany";
        country.CodeISO2 = $"{Guid.NewGuid():N}"[..2].ToUpperInvariant();
        country.CodeISO3 = $"{Guid.NewGuid():N}"[..3].ToUpperInvariant();
        country.PhoneCode = "49";
        country = await DictionaryManager.SaveRecordAsync(country);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = "ACME GmbH";
        legalEntity.RegistrationNumber = $"REG-CT-{Guid.NewGuid():N}"[..16];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"CT-{Guid.NewGuid():N}"[..10];
        divisionType.Name = "SalesPoint";
        divisionType = await DictionaryManager.SaveRecordAsync(divisionType);

        var division = DictionaryManager.NewRecord<Division>();
        division.Name = "Shop";
        division.LegalEntity = legalEntity.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await DictionaryManager.SaveRecordAsync(division);

        var store = DictionaryManager.NewRecord<Store>();
        store.Name = "Shop WH";
        store.Division = division.MetaId;
        store.IsSimple = true;
        store = await DictionaryManager.SaveRecordAsync(store);

        var zone = DictionaryManager.NewRecord<StoreZone>();
        zone.Name = "Zone";
        zone.Store = store.MetaId;
        zone.IsBarcodeTracking = false;
        zone = await DictionaryManager.SaveRecordAsync(zone);

        var cellType = DictionaryManager.NewRecord<StoreCellType>();
        cellType.Code = $"PICK-{Guid.NewGuid():N}"[..12];
        cellType.Name = "Picking";
        cellType = await DictionaryManager.SaveRecordAsync(cellType);

        var cell = DictionaryManager.NewRecord<StoreCell>();
        cell.Name = "P-01";
        cell.Type = cellType.MetaId;
        cell.StoreZone = zone.MetaId;
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = 1;
        cell = await DictionaryManager.SaveRecordAsync(cell);

        var unit = DictionaryManager.NewRecord<UnitOfMeasure>();
        unit.Name = "Piece";
        unit.Code = "PCS";
        unit = await DictionaryManager.SaveRecordAsync(unit);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = "GOODS";
        group.Name = "Finished goods";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Bread";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = unit.MetaId;
        item.IsSellable = true;
        item.Image = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==");
        item = await DictionaryManager.SaveRecordAsync(item);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Net-1";
        customer.CustomerType = "B2B";
        customer.CreditLimit = 100m;
        customer = await DictionaryManager.SaveRecordAsync(customer);

        var other = DictionaryManager.NewRecord<Customer>();
        other.Name = "Net-2";
        other.CustomerType = "B2B";
        other = await DictionaryManager.SaveRecordAsync(other);

        var outlet = DictionaryManager.NewRecord<CustomerOutlet>();
        outlet.Name = "Shop A";
        outlet.Customer = customer.MetaId;
        outlet = await DictionaryManager.SaveRecordAsync(outlet);
        Assert.IsTrue(outlet.Customer == customer.MetaId, "точка хранит клиента");

        var otherOutlet = DictionaryManager.NewRecord<CustomerOutlet>();
        otherOutlet.Name = "Shop B";
        otherOutlet.Customer = other.MetaId;
        otherOutlet = await DictionaryManager.SaveRecordAsync(otherOutlet);

        var contract = DictionaryManager.NewRecord<SalesContract>();
        contract.Name = "A-2026";
        contract.Outlet = outlet.MetaId;
        contract.Currency = currency.MetaId;
        contract.SettlementKind = SettlementKind.Credit;
        contract.EffectiveFrom = new DateTime(2026, 1, 1);
        contract.LegalEntity = legalEntity.MetaId;
        contract = await DictionaryManager.SaveRecordAsync(contract);
        contract = await DictionaryManager.GetRecordAsync<SalesContract>(contract.MetaId)
            ?? throw new InvalidOperationException("договор не читается после сохранения");
        Assert.IsTrue(contract.Customer == customer.MetaId,
            "клиент договора штампуется с точки, факт {0}", contract.Customer);

        return new Setup
        {
            Location = cell.MetaId,
            Item = item.MetaId,
            Customer = customer.MetaId,
            OtherCustomer = other.MetaId,
            Outlet = outlet.MetaId,
            OtherOutlet = otherOutlet.MetaId,
            Currency = currency.MetaId,
            LegalEntity = legalEntity.MetaId,
            Contract = contract.MetaId,
        };
    }

    private async Task StockInAsync(Setup s, decimal qty)
    {
        var adjustment = await DocumentManager.NewDocumentAsync<StockAdjustment>();
        adjustment.Cell = s.Location;
        adjustment.Lines.Add(new StockAdjustmentLinesTablePartRow { Item = s.Item, Quantity = qty });
        await DocumentManager.SaveDocumentAsync(adjustment);
        var postId = await Db.FindCommandIdAsync("document", "PostStockAdjustment");
        var run = await Db.ExecuteDocumentCommandAsync(postId, adjustment.MetaId);
        Assert.IsTrue(run.Success, "приход: {0}", run.Message ?? string.Join("; ", run.ClientMessages));
    }

    [IntegrationTest("Пересекающиеся окна договоров одной точки отклоняются")]
    public async Task OverlappingWindowsAreRejected()
    {
        var s = await SetupAsync();
        var clash = DictionaryManager.NewRecord<SalesContract>();
        clash.Name = "A-overlap";
        clash.Outlet = s.Outlet;
        clash.Currency = s.Currency;
        clash.SettlementKind = SettlementKind.Credit;
        clash.EffectiveFrom = new DateTime(2026, 6, 1);
        try
        {
            await DictionaryManager.SaveRecordAsync(clash);
            Assert.IsTrue(false, "пересечение должно быть отклонено");
        }
        catch (Exception ex)
        {
            Assert.IsTrue(ex.Message.Contains("пересекается"), "текст отказа: {0}", ex.Message);
        }

        var open = await DictionaryManager.GetRecordAsync<SalesContract>(s.Contract);
        open!.EffectiveTo = new DateTime(2026, 12, 31);
        await DictionaryManager.SaveRecordAsync(open);

        var next = DictionaryManager.NewRecord<SalesContract>();
        next.Name = "A-2027";
        next.Outlet = s.Outlet;
        next.Currency = s.Currency;
        next.SettlementKind = SettlementKind.Credit;
        next.EffectiveFrom = new DateTime(2027, 1, 1);
        next = await DictionaryManager.SaveRecordAsync(next);
        Assert.IsTrue(next.MetaId != Guid.Empty, "смежное окно после закрытия предыдущего сохраняется");
    }

    [IntegrationTest("Заказ с точкой подставляет действующий договор")]
    public async Task OrderStampsActiveContract()
    {
        var s = await SetupAsync();
        var order = await DocumentManager.NewDocumentAsync<SalesOrder>();
        order.Customer = s.Customer;
        order.Outlet = s.Outlet;
        order.Location = s.Location;
        order.DeliveryDate = new DateTime(2026, 3, 1);
        await DocumentManager.SaveDocumentAsync(order);
        var reloaded = await DocumentManager.GetDocumentAsync<SalesOrder>(order.MetaId);
        Assert.IsTrue(reloaded!.Contract == s.Contract, "договор штампуется, факт {0}", reloaded.Contract);
        Assert.IsTrue(await Contracts.ResolveActiveAsync(s.Outlet, new DateTime(2026, 3, 1)) == s.Contract,
            "сервис находит тот же договор");
    }

    [IntegrationTest("Чужой договор на заказе отклоняется")]
    public async Task ForeignContractIsRejected()
    {
        var s = await SetupAsync();
        var foreign = DictionaryManager.NewRecord<SalesContract>();
        foreign.Name = "B-2026";
        foreign.Outlet = s.OtherOutlet;
        foreign.Currency = s.Currency;
        foreign.SettlementKind = SettlementKind.Credit;
        foreign.EffectiveFrom = new DateTime(2026, 1, 1);
        foreign = await DictionaryManager.SaveRecordAsync(foreign);

        var order = await DocumentManager.NewDocumentAsync<SalesOrder>();
        order.Customer = s.Customer;
        order.Outlet = s.Outlet;
        order.Contract = foreign.MetaId;
        order.Location = s.Location;
        order.DeliveryDate = new DateTime(2026, 3, 1);
        try
        {
            await DocumentManager.SaveDocumentAsync(order);
            Assert.IsTrue(false, "чужой договор должен быть отклонён");
        }
        catch (Exception ex)
        {
            Assert.IsTrue(ex.Message.Contains("другой"), "текст отказа: {0}", ex.Message);
        }
    }

    [IntegrationTest("Кредит сверх лимита на Approve отклоняется")]
    public async Task CreditOverLimitIsRejected()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 20m);

        var contract = await DictionaryManager.GetRecordAsync<SalesContract>(s.Contract);
        contract!.CreditLimit = 10m;
        await DictionaryManager.SaveRecordAsync(contract);

        var order = await DocumentManager.NewDocumentAsync<SalesOrder>();
        order.Customer = s.Customer;
        order.Outlet = s.Outlet;
        order.Contract = s.Contract;
        order.Location = s.Location;
        order.DeliveryDate = new DateTime(2026, 3, 1);
        order.Lines.Add(new SalesOrderLinesTablePartRow { Item = s.Item, Quantity = 5m, UnitPrice = 10m });
        await DocumentManager.SaveDocumentAsync(order);
        await RunCommandAsync("SubmitSalesOrder", order.MetaId);

        var approveId = await Db.FindCommandIdAsync("document", "ApproveSalesOrder");
        var run = await Db.ExecuteDocumentCommandAsync(approveId, order.MetaId);
        var after = await DocumentManager.GetDocumentAsync<SalesOrder>(order.MetaId);
        Assert.IsTrue(after!.Subtype == SalesOrder.Subtypes.Submitted,
            "заказ остаётся Submitted, факт {0}", after.Subtype ?? "<null>");
        Assert.IsTrue(!run.Success || string.Join("; ", run.ClientMessages).Contains("лимит"),
            "отказ про лимит: {0}", string.Join("; ", run.ClientMessages));
    }

    [IntegrationTest("Prepaid без аванса отклоняется; с авансом проходит")]
    public async Task PrepaidRequiresAdvance()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 20m);

        var contract = await DictionaryManager.GetRecordAsync<SalesContract>(s.Contract);
        contract!.SettlementKind = SettlementKind.Prepaid;
        contract.CreditLimit = 0m;
        await DictionaryManager.SaveRecordAsync(contract);

        var order = await DocumentManager.NewDocumentAsync<SalesOrder>();
        order.Customer = s.Customer;
        order.Outlet = s.Outlet;
        order.Contract = s.Contract;
        order.Location = s.Location;
        order.DeliveryDate = new DateTime(2026, 3, 1);
        order.Lines.Add(new SalesOrderLinesTablePartRow { Item = s.Item, Quantity = 2m, UnitPrice = 10m });
        await DocumentManager.SaveDocumentAsync(order);
        await RunCommandAsync("SubmitSalesOrder", order.MetaId);

        var approveId = await Db.FindCommandIdAsync("document", "ApproveSalesOrder");
        var denied = await Db.ExecuteDocumentCommandAsync(approveId, order.MetaId);
        var still = await DocumentManager.GetDocumentAsync<SalesOrder>(order.MetaId);
        Assert.IsTrue(still!.Subtype == SalesOrder.Subtypes.Submitted,
            "без аванса заказ Submitted, факт {0}", still.Subtype ?? "<null>");
        Assert.IsTrue(!denied.Success || string.Join("; ", denied.ClientMessages).Contains("аванс"),
            "отказ про аванс: {0}", string.Join("; ", denied.ClientMessages));

        var pay = await DocumentManager.NewDocumentAsync<CustomerPayment>();
        pay.Lines.Add(new CustomerPaymentLinesTablePartRow { Customer = s.Customer, Contract = s.Contract, Amount = 20m });
        await DocumentManager.SaveDocumentAsync(pay);
        await RunCommandAsync("ReceiveCustomerPayment", pay.MetaId);

        await RunCommandAsync("ApproveSalesOrder", order.MetaId);
        var approved = await DocumentManager.GetDocumentAsync<SalesOrder>(order.MetaId);
        Assert.IsTrue(approved!.Subtype == SalesOrder.Subtypes.Confirmed,
            "с авансом заказ Confirmed, факт {0}", approved.Subtype ?? "<null>");
    }

    [IntegrationTest("Долг другого договора не съедает кредитный лимит этого")]
    public async Task CreditLimitIsPerContract()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 200m);

        var contract = await DictionaryManager.GetRecordAsync<SalesContract>(s.Contract);
        contract!.CreditLimit = 100m;
        await DictionaryManager.SaveRecordAsync(contract);

        var other = await SecondOutletAsync(s);
        await IssueInvoiceAsync(s, other.Outlet, other.Contract, 8m, 10m);

        var order = await DocumentManager.NewDocumentAsync<SalesOrder>();
        order.Customer = s.Customer;
        order.Outlet = s.Outlet;
        order.Contract = s.Contract;
        order.Location = s.Location;
        order.DeliveryDate = new DateTime(2026, 3, 1);
        order.Lines.Add(new SalesOrderLinesTablePartRow { Item = s.Item, Quantity = 5m, UnitPrice = 10m });
        await DocumentManager.SaveDocumentAsync(order);
        await RunCommandAsync("SubmitSalesOrder", order.MetaId);
        await RunCommandAsync("ApproveSalesOrder", order.MetaId);
        var approved = await DocumentManager.GetDocumentAsync<SalesOrder>(order.MetaId);
        Assert.IsTrue(approved!.Subtype == SalesOrder.Subtypes.Confirmed,
            "лимит 100 на этом договоре, чужие 80 не считаются, факт {0}", approved.Subtype ?? "<null>");
    }

    [IntegrationTest("Аванс на другом договоре prepaid не открывает этот")]
    public async Task PrepaidAdvanceIsPerContract()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 20m);

        var contract = await DictionaryManager.GetRecordAsync<SalesContract>(s.Contract);
        contract!.SettlementKind = SettlementKind.Prepaid;
        contract.CreditLimit = 0m;
        await DictionaryManager.SaveRecordAsync(contract);

        var other = await SecondOutletAsync(s);
        var payOther = await DocumentManager.NewDocumentAsync<CustomerPayment>();
        payOther.Lines.Add(new CustomerPaymentLinesTablePartRow
        {
            Customer = s.Customer, Contract = other.Contract, Amount = 20m,
        });
        await DocumentManager.SaveDocumentAsync(payOther);
        await RunCommandAsync("ReceiveCustomerPayment", payOther.MetaId);

        var order = await DocumentManager.NewDocumentAsync<SalesOrder>();
        order.Customer = s.Customer;
        order.Outlet = s.Outlet;
        order.Contract = s.Contract;
        order.Location = s.Location;
        order.DeliveryDate = new DateTime(2026, 3, 1);
        order.Lines.Add(new SalesOrderLinesTablePartRow { Item = s.Item, Quantity = 2m, UnitPrice = 10m });
        await DocumentManager.SaveDocumentAsync(order);
        await RunCommandAsync("SubmitSalesOrder", order.MetaId);

        var approveId = await Db.FindCommandIdAsync("document", "ApproveSalesOrder");
        var denied = await Db.ExecuteDocumentCommandAsync(approveId, order.MetaId);
        var still = await DocumentManager.GetDocumentAsync<SalesOrder>(order.MetaId);
        Assert.IsTrue(still!.Subtype == SalesOrder.Subtypes.Submitted,
            "чужой аванс не считается, факт {0}", still.Subtype ?? "<null>");
        Assert.IsTrue(!denied.Success || string.Join("; ", denied.ClientMessages).Contains("аванс"),
            "отказ про аванс: {0}", string.Join("; ", denied.ClientMessages));

        var pay = await DocumentManager.NewDocumentAsync<CustomerPayment>();
        pay.Lines.Add(new CustomerPaymentLinesTablePartRow
        {
            Customer = s.Customer, Contract = s.Contract, Amount = 20m,
        });
        await DocumentManager.SaveDocumentAsync(pay);
        await RunCommandAsync("ReceiveCustomerPayment", pay.MetaId);

        await RunCommandAsync("ApproveSalesOrder", order.MetaId);
        var approved = await DocumentManager.GetDocumentAsync<SalesOrder>(order.MetaId);
        Assert.IsTrue(approved!.Subtype == SalesOrder.Subtypes.Confirmed,
            "аванс этого договора открывает заказ, факт {0}", approved.Subtype ?? "<null>");
    }

    [IntegrationTest("Без договора счёт не сохраняется")]
    public async Task InvoiceWithoutContractIsRejected()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 5m);

        var inv = await DocumentManager.NewDocumentAsync<SalesRealization>();
        inv.Customer = s.Customer;
        inv.Location = s.Location;
        inv.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = 1m, UnitPrice = 8m });
        var reason = string.Empty;
        try
        {
            await DocumentManager.SaveDocumentAsync(inv);
        }
        catch (Exception ex) { reason = ex.Message; }
        Assert.IsTrue(reason.Contains("договор", StringComparison.OrdinalIgnoreCase),
            "отказ про договор: {0}", reason);
    }

    private async Task<(Guid Outlet, Guid Contract)> SecondOutletAsync(Setup s)
    {
        var outlet = DictionaryManager.NewRecord<CustomerOutlet>();
        outlet.Name = "Shop C";
        outlet.Customer = s.Customer;
        outlet = await DictionaryManager.SaveRecordAsync(outlet);

        var contract = DictionaryManager.NewRecord<SalesContract>();
        contract.Name = "C-2026";
        contract.Outlet = outlet.MetaId;
        contract.Currency = s.Currency;
        contract.SettlementKind = SettlementKind.Credit;
        contract.EffectiveFrom = new DateTime(2020, 1, 1);
        contract.LegalEntity = s.LegalEntity;
        contract = await DictionaryManager.SaveRecordAsync(contract);
        return (outlet.MetaId, contract.MetaId);
    }

    private async Task IssueInvoiceAsync(Setup s, Guid outlet, Guid contract, decimal qty, decimal price)
    {
        var inv = await DocumentManager.NewDocumentAsync<SalesRealization>();
        inv.Customer = s.Customer;
        inv.Outlet = outlet;
        inv.Contract = contract;
        inv.Location = s.Location;
        inv.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = qty, UnitPrice = price });
        await DocumentManager.SaveDocumentAsync(inv);
        inv.Subtype = SalesRealization.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(inv);
    }

    private async Task RunCommandAsync(string name, Guid documentId)
    {
        var commandId = await Db.FindCommandIdAsync("document", name);
        var run = await Db.ExecuteDocumentCommandAsync(commandId, documentId);
        Assert.IsTrue(run.Success, "команда {0}: {1}", name, run.Message ?? string.Join("; ", run.ClientMessages));
    }
}
