using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

public class ContractPricingTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ILinkTableManager Links => GetService<ILinkTableManager>();
    private static IPricingService Pricing => GetService<IPricingService>();

    private static readonly DateTime March = new DateTime(2026, 3, 15);

    private sealed class Setup
    {
        public Guid Location;
        public Guid Item;
        public Guid Piece;
        public Guid Customer;
        public Guid Outlet;
        public Guid Contract;
        public Guid CustomerType;
        public Guid ContractType;
        public Guid Currency;
        public Guid LegalEntity;
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
        legalEntity.RegistrationNumber = $"REG-PX-{Guid.NewGuid():N}"[..16];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"PX-{Guid.NewGuid():N}"[..10];
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
        unit.Code = $"P{Guid.NewGuid():N}"[..8];
        unit = await DictionaryManager.SaveRecordAsync(unit);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"G{Guid.NewGuid():N}"[..8];
        group.Name = "Goods";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Widget";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = unit.MetaId;
        item.IsSellable = true;
        item.Image = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==");
        item = await DictionaryManager.SaveRecordAsync(item);

        var customerType = DictionaryManager.NewRecord<PriceType>();
        customerType.Name = "Розница";
        customerType.Direction = PriceDirection.Sale;
        customerType = await DictionaryManager.SaveRecordAsync(customerType);
        await Pricing.SetPriceAsync(customerType.MetaId, item.MetaId, unit.MetaId, 110m, null, null);

        var contractType = DictionaryManager.NewRecord<PriceType>();
        contractType.Name = "Оптовая";
        contractType.Direction = PriceDirection.Sale;
        contractType = await DictionaryManager.SaveRecordAsync(contractType);
        await Pricing.SetPriceAsync(contractType.MetaId, item.MetaId, unit.MetaId, 100m, null, null);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Buyer";
        customer.CustomerType = "B2B";
        customer.PriceType = customerType.MetaId;
        customer = await DictionaryManager.SaveRecordAsync(customer);

        var outlet = DictionaryManager.NewRecord<CustomerOutlet>();
        outlet.Name = "Shop A";
        outlet.Customer = customer.MetaId;
        outlet = await DictionaryManager.SaveRecordAsync(outlet);

        var contract = DictionaryManager.NewRecord<SalesContract>();
        contract.Name = "A-2026";
        contract.Outlet = outlet.MetaId;
        contract.Currency = currency.MetaId;
        contract.SettlementKind = SettlementKind.Credit;
        contract.EffectiveFrom = new DateTime(2020, 1, 1);
        contract.LegalEntity = legalEntity.MetaId;
        contract.PriceType = contractType.MetaId;
        contract = await DictionaryManager.SaveRecordAsync(contract);

        return new Setup
        {
            Location = cell.MetaId,
            Item = item.MetaId,
            Piece = unit.MetaId,
            Customer = customer.MetaId,
            Outlet = outlet.MetaId,
            Contract = contract.MetaId,
            CustomerType = customerType.MetaId,
            ContractType = contractType.MetaId,
            Currency = currency.MetaId,
            LegalEntity = legalEntity.MetaId,
        };
    }

    private async Task<SalesRealization> NewInvoiceAsync(Setup s, decimal qty, decimal unitPrice = 0m)
    {
        var inv = await DocumentManager.NewDocumentAsync<SalesRealization>();
        inv.Customer = s.Customer;
        inv.Outlet = s.Outlet;
        inv.Contract = s.Contract;
        inv.Location = s.Location;
        inv.DocumentDate = March;
        inv.Lines.Add(new SalesInvoiceLinesTablePartRow
        {
            Item = s.Item,
            Quantity = qty,
            Unit = s.Piece,
            UnitPrice = unitPrice,
        });
        await DocumentManager.SaveDocumentAsync(inv);
        return (await DocumentManager.GetDocumentAsync<SalesRealization>(inv.MetaId))!;
    }

    [IntegrationTest("Тип цен договора 100 перебивает тип клиента 110 на строке счёта")]
    public async Task ContractTypeBeatsCustomerTypeOnLine()
    {
        var s = await SetupAsync();
        var inv = await NewInvoiceAsync(s, 1m);
        var line = inv.Lines[0];
        Assert.IsTrue(line.UnitPrice == 100m, "ожидалась цена договора 100, факт {0}", line.UnitPrice);
        Assert.IsTrue((line.PriceExplanation ?? "").Contains("Оптовая"),
            "пояснение про тип договора, факт: {0}", line.PriceExplanation);
    }

    [IntegrationTest("Цена товара по договору 90 перебивает тип 100")]
    public async Task ContractItemOverlayBeatsType()
    {
        var s = await SetupAsync();
        await Links.SaveRecordAsync(new LT_SalesContractPrice
        {
            SalesContract = s.Contract,
            Item = s.Item,
            Unit = s.Piece,
            Price = 90m,
            EffectiveFrom = new DateTime(2026, 1, 1),
        });
        var inv = await NewInvoiceAsync(s, 1m);
        Assert.IsTrue(inv.Lines[0].UnitPrice == 90m, "overlay 90, факт {0}", inv.Lines[0].UnitPrice);
        Assert.IsTrue((inv.Lines[0].PriceExplanation ?? "").Contains("90.00"),
            "пояснение содержит 90, факт: {0}", inv.Lines[0].PriceExplanation);
    }

    [IntegrationTest("Пересекающиеся окна цены товара по договору отклоняются")]
    public async Task OverlappingContractPriceWindowsAreRejected()
    {
        var s = await SetupAsync();
        await Links.SaveRecordAsync(new LT_SalesContractPrice
        {
            SalesContract = s.Contract,
            Item = s.Item,
            Unit = s.Piece,
            Price = 90m,
            EffectiveFrom = new DateTime(2026, 1, 1),
            EffectiveTo = new DateTime(2026, 6, 30),
        });
        try
        {
            await Links.SaveRecordAsync(new LT_SalesContractPrice
            {
                SalesContract = s.Contract,
                Item = s.Item,
                Unit = s.Piece,
                Price = 80m,
                EffectiveFrom = new DateTime(2026, 6, 1),
            });
            Assert.IsTrue(false, "пересечение должно быть отклонено");
        }
        catch (Exception ex)
        {
            Assert.IsTrue(ex.Message.Contains("пересекающийся период"),
                "текст отказа: {0}", ex.Message);
        }
    }

    [IntegrationTest("Fill не затирает введённую цену; ручная помечается «Вручную»")]
    public async Task FillSkipsTypedPriceAndMarksManual()
    {
        var s = await SetupAsync();
        var inv = await DocumentManager.NewDocumentAsync<SalesRealization>();
        inv.Customer = s.Customer;
        inv.Outlet = s.Outlet;
        inv.Contract = s.Contract;
        inv.Location = s.Location;
        inv.DocumentDate = March;
        inv.Lines.Add(new SalesInvoiceLinesTablePartRow
        {
            Item = s.Item, Quantity = 1m, Unit = s.Piece,
        });
        inv.Lines.Add(new SalesInvoiceLinesTablePartRow
        {
            Item = s.Item, Quantity = 1m, Unit = s.Piece, UnitPrice = 50m,
            PriceExplanation = "Вручную",
        });
        await DocumentManager.SaveDocumentAsync(inv);

        var commandId = await Db.FindCommandIdAsync("document", "FillSalesPrices");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, inv.MetaId);
        Assert.IsTrue(run.Success, "Fill: {0}", run.Message ?? "");

        var saved = await DocumentManager.GetDocumentAsync<SalesRealization>(inv.MetaId);
        var auto = saved!.Lines.First(l => l.UnitPrice != 50m);
        var typed = saved.Lines.First(l => l.UnitPrice == 50m);
        Assert.IsTrue(auto.UnitPrice == 100m, "пустая строка заполнилась 100, факт {0}", auto.UnitPrice);
        Assert.IsTrue(typed.UnitPrice == 50m, "ручная 50 не затёрлась");
        Assert.IsTrue(typed.PriceExplanation == "Вручную", "ручная пометка, факт {0}", typed.PriceExplanation);
    }

    [IntegrationTest("Порог 100 шт применяет 5%; «Вручную» при смене количества не двигается")]
    public async Task QtyBreakReappliesUnlessManual()
    {
        var s = await SetupAsync();
        await Links.SaveRecordAsync(new LT_SalesContractQtyBreak
        {
            SalesContract = s.Contract,
            MinQty = 100m,
            DiscountPercent = 5m,
        });

        var auto = await NewInvoiceAsync(s, 1m);
        Assert.IsTrue(auto.Lines[0].UnitPrice == 100m, "qty 1 → 100, факт {0}", auto.Lines[0].UnitPrice);
        auto.Lines[0].Quantity = 100m;
        // Line-only SaveDocumentAsync skips header OnBeforeSave (unchanged
        // scalars). Draft pricing lives there — touch Notes so Mix BeforeSave
        // sees the new qty. Live grid OnFieldChanged already has the header.
        auto.Notes = (auto.Notes ?? "") + ".";
        await DocumentManager.SaveDocumentAsync(auto);
        auto = (await DocumentManager.GetDocumentAsync<SalesRealization>(auto.MetaId))!;
        Assert.IsTrue(auto.Lines[0].UnitPrice == 95m, "qty 100 → 5% = 95, факт {0}", auto.Lines[0].UnitPrice);

        var manual = await NewInvoiceAsync(s, 1m, 70m);
        manual.Lines[0].PriceExplanation = "Вручную";
        manual.Lines[0].UnitPrice = 70m;
        await DocumentManager.SaveDocumentAsync(manual);
        manual.Lines[0].Quantity = 100m;
        manual.Notes = (manual.Notes ?? "") + ".";
        await DocumentManager.SaveDocumentAsync(manual);
        manual = (await DocumentManager.GetDocumentAsync<SalesRealization>(manual.MetaId))!;
        Assert.IsTrue(manual.Lines[0].UnitPrice == 70m,
            "ручная цена не двигается при смене qty, факт {0}", manual.Lines[0].UnitPrice);
    }

    [IntegrationTest("Скидка шапки идёт в LineAmount — одна база для суммы")]
    public async Task HeaderDiscountStillUsesLineAmount()
    {
        var s = await SetupAsync();
        Assert.IsTrue(Pricing.LineAmount(10m, 100m, 15m) == 850m, "10×100−15%=850");
        var inv = await NewInvoiceAsync(s, 10m);
        inv.DiscountPercent = 15m;
        await DocumentManager.SaveDocumentAsync(inv);
        inv = (await DocumentManager.GetDocumentAsync<SalesRealization>(inv.MetaId))!;
        var line = inv.Lines[0];
        Assert.IsTrue(
            Pricing.LineAmount(line.Quantity, line.UnitPrice, inv.DiscountPercent) == 850m,
            "скидка шапки считает ту же базу, факт {0}",
            Pricing.LineAmount(line.Quantity, line.UnitPrice, inv.DiscountPercent));
    }

    [IntegrationTest("Заказ на черновике тоже берёт тип цен договора")]
    public async Task OrderDraftLineUsesContractType()
    {
        var s = await SetupAsync();
        var order = await DocumentManager.NewDocumentAsync<SalesOrder>();
        order.Customer = s.Customer;
        order.Outlet = s.Outlet;
        order.Contract = s.Contract;
        order.Location = s.Location;
        order.DeliveryDate = March;
        order.Lines.Add(new SalesOrderLinesTablePartRow
        {
            Item = s.Item,
            Quantity = 1m,
            Unit = s.Piece,
        });
        await DocumentManager.SaveDocumentAsync(order);
        order = (await DocumentManager.GetDocumentAsync<SalesOrder>(order.MetaId))!;
        Assert.IsTrue(order.Lines[0].UnitPrice == 100m, "заказ 100 по договору, факт {0}", order.Lines[0].UnitPrice);
        Assert.IsTrue((order.Lines[0].PriceExplanation ?? "").Contains("Оптовая"),
            "пояснение заказа, факт: {0}", order.Lines[0].PriceExplanation);
    }

    [IntegrationTest("Дубль порога количества на том же товаре отклоняется")]
    public async Task DuplicateQtyBreakIsRejected()
    {
        var s = await SetupAsync();
        await Links.SaveRecordAsync(new LT_SalesContractQtyBreak
        {
            SalesContract = s.Contract,
            MinQty = 100m,
            DiscountPercent = 5m,
        });
        try
        {
            await Links.SaveRecordAsync(new LT_SalesContractQtyBreak
            {
                SalesContract = s.Contract,
                MinQty = 100m,
                DiscountPercent = 10m,
            });
            Assert.IsTrue(false, "дубль порога должен быть отклонён");
        }
        catch (Exception ex)
        {
            Assert.IsTrue(ex.Message.Contains("уже есть скидка"), "текст отказа: {0}", ex.Message);
        }
    }

    [IntegrationTest("ApplyFieldAsync: Item ставит цену договора; UnitPrice помечает вручную")]
    public async Task ApplyFieldMarksManualAndResolvesItem()
    {
        var s = await SetupAsync();
        var fill = GetService<ISalesLinePricing>();
        var auto = await fill.ApplyFieldAsync(
            "Item", s.Item, s.Piece, 1m, 0m, "", s.Customer, s.Contract, March);
        Assert.IsTrue(Convert.ToDecimal(auto["UnitPrice"]) == 100m, "Item → 100, факт {0}", auto["UnitPrice"]);
        Assert.IsTrue((auto["PriceExplanation"] as string ?? "").Contains("Оптовая"),
            "пояснение типа, факт: {0}", auto["PriceExplanation"]);

        var typed = await fill.ApplyFieldAsync(
            "UnitPrice", s.Item, s.Piece, 1m, 50m, "", s.Customer, s.Contract, March);
        Assert.IsTrue((string)typed["PriceExplanation"]! == "Вручную", "пометка вручную, факт {0}", typed["PriceExplanation"]);
        Assert.IsTrue(Convert.ToDecimal(typed["UnitPrice"]) == 50m, "введённая 50, факт {0}", typed["UnitPrice"]);
    }
}
