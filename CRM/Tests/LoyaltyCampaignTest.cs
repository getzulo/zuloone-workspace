using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// A live campaign overlays tier and settings. Expired or disabled windows
// do not. Two live windows of the same ItemGroup may not overlap; different
// groups may share dates. Earn is priced per invoice line.
public class LoyaltyCampaignTest : IntegrationTestScriptBase
{
    private static ILoyaltyCampaignService Svc => GetService<ILoyaltyCampaignService>();
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private static readonly DateTime Origin = new(2020, 1, 1);

    private string Uniq() => $"{Db.NewId():N}"[..8];

    [IntegrationTest("Кампания перекрывает курс настроек")]
    public async Task CampaignOverlaysSettings()
    {
        await ConfigureLoyaltyAsync(true, 2m);
        await NewCampaignAsync(4m, Origin);
        var s = await IssueInvoiceAsync();
        Assert.IsTrue(await PointsBalanceAsync(s.Customer) == 60m,
            "3 × 5 × кампания 4 = 60, факт {0}", await PointsBalanceAsync(s.Customer));
    }

    [IntegrationTest("Кампания перекрывает курс достигнутого уровня")]
    public async Task CampaignOverlaysTier()
    {
        await ConfigureLoyaltyAsync(true, 2m);
        await TierAsync("Silver", 0m, 50m, 3m);
        await NewCampaignAsync(5m, Origin);
        var s = await IssueInvoiceAsync();
        Assert.IsTrue(await PointsBalanceAsync(s.Customer) == 75m,
            "кампания 5 бьёт Silver 3: 3 × 5 × 5 = 75, факт {0}", await PointsBalanceAsync(s.Customer));
    }

    [IntegrationTest("Истёкшая кампания не подменяет настройки")]
    public async Task ExpiredCampaignFallsBack()
    {
        await ConfigureLoyaltyAsync(true, 2m);
        await NewCampaignAsync(9m, Origin, new DateTime(2025, 12, 31));
        Assert.IsTrue(await Svc.EarnRateOfAsync(DateTime.UtcNow.Date, Guid.Empty) == 0m, "окно закрыто");
        var s = await IssueInvoiceAsync();
        Assert.IsTrue(await PointsBalanceAsync(s.Customer) == 30m,
            "без кампании курс настроек 2: 30, факт {0}", await PointsBalanceAsync(s.Customer));
    }

    [IntegrationTest("Пересечение живых окон одной группы отклоняется при вводе")]
    public async Task OverlappingCampaignIsRejected()
    {
        await NewCampaignAsync(2m, Origin);
        var clash = DictionaryManager.NewRecord<LoyaltyCampaign>();
        clash.Code = $"C-{Uniq()}";
        clash.Name = "Clash";
        clash.EarnRate = 3m;
        clash.EffectiveFrom = new DateTime(2026, 1, 1);
        var reason = string.Empty;
        try { await DictionaryManager.SaveRecordAsync(clash); }
        catch (Exception ex) { reason = ex.Message; }
        Assert.IsTrue(reason.Contains("уже есть другая кампания"),
            "пересечение обязано быть отклонено, факт: {0}", reason);
    }

    [IntegrationTest("Кампания группы начисляет только своим строкам")]
    public async Task CampaignForMatchingGroupOverlays()
    {
        await ConfigureLoyaltyAsync(true, 2m);
        var s = await SetupAsync();
        await NewCampaignAsync(4m, Origin, itemGroup: s.ItemGroup);
        await IssueInvoiceFromAsync(s);
        Assert.IsTrue(await PointsBalanceAsync(s.Customer) == 60m,
            "совпавшая группа: 3 × 5 × 4 = 60, факт {0}", await PointsBalanceAsync(s.Customer));
    }

    [IntegrationTest("Кампания чужой группы не бьёт настройки")]
    public async Task CampaignForOtherGroupFallsBack()
    {
        await ConfigureLoyaltyAsync(true, 2m);
        var s = await SetupAsync();
        var other = await ExtraItemAsync(s);
        await NewCampaignAsync(9m, Origin, itemGroup: other.Group);
        await IssueInvoiceFromAsync(s);
        Assert.IsTrue(await PointsBalanceAsync(s.Customer) == 30m,
            "чужая группа: курс настроек 2 → 30, факт {0}", await PointsBalanceAsync(s.Customer));
    }

    [IntegrationTest("Смешанный счёт считает курс по строке")]
    public async Task MixedInvoiceUsesPerLineRate()
    {
        await ConfigureLoyaltyAsync(true, 2m);
        var s = await SetupAsync();
        var other = await ExtraItemAsync(s);
        await NewCampaignAsync(4m, Origin, itemGroup: s.ItemGroup);
        await IssueInvoiceFromAsync(s, (other.Item, 1m, 5m));
        Assert.IsTrue(await PointsBalanceAsync(s.Customer) == 70m,
            "3 × 5 × 4 + 1 × 5 × 2 = 70, факт {0}", await PointsBalanceAsync(s.Customer));
    }

    [IntegrationTest("Кампании разных групп могут пересекаться по датам")]
    public async Task DifferentItemGroupsMayOverlap()
    {
        var a = await ExtraGroupAsync();
        var b = await ExtraGroupAsync();
        await NewCampaignAsync(2m, Origin, itemGroup: a);
        var second = DictionaryManager.NewRecord<LoyaltyCampaign>();
        second.Code = $"C-{Uniq()}";
        second.Name = "Other group";
        second.EarnRate = 3m;
        second.EffectiveFrom = Origin;
        second.ItemGroup = b;
        await DictionaryManager.SaveRecordAsync(second);
        Assert.IsTrue(await Svc.EarnRateOfAsync(Origin, a) == 2m, "группа A");
        Assert.IsTrue(await Svc.EarnRateOfAsync(Origin, b) == 3m, "группа B");
    }

    private async Task NewCampaignAsync(
        decimal rate, DateTime from, DateTime? to = null, Guid? itemGroup = null)
    {
        var row = DictionaryManager.NewRecord<LoyaltyCampaign>();
        row.Code = $"C-{Uniq()}";
        row.Name = "Promo";
        row.EarnRate = rate;
        row.EffectiveFrom = from;
        row.EffectiveTo = to;
        if (itemGroup.HasValue) row.ItemGroup = itemGroup.Value;
        await DictionaryManager.SaveRecordAsync(row);
    }

    private static async Task ConfigureLoyaltyAsync(bool enabled, decimal pointsPerCurrencyUnit)
    {
        var rows = await DictionaryManager.GetRecordsAsync<CRMSettings>(null, 1);
        var settings = rows.Count > 0 ? rows[0] : DictionaryManager.NewRecord<CRMSettings>();
        settings.LoyaltyEnabled = enabled;
        settings.PointsPerCurrencyUnit = pointsPerCurrencyUnit;
        await DictionaryManager.SaveRecordAsync(settings);
    }

    private static async Task TierAsync(string name, decimal minPoints, decimal maxPerDoc, decimal earnRate)
    {
        var tier = DictionaryManager.NewRecord<LoyaltyTier>();
        tier.Name = name;
        tier.MinPoints = minPoints;
        tier.MaxRedemptionPerDocument = maxPerDoc;
        tier.DiscountPercent = 0m;
        tier.EarnRate = earnRate;
        await DictionaryManager.SaveRecordAsync(tier);
    }

    private static Task<decimal> PointsBalanceAsync(Guid customer)
        => TotalsManager.GetBalanceAsync("LoyaltyPoints", "Points",
            new Dictionary<string, object?> { ["Customer"] = customer });

    private sealed class Setup
    {
        public Guid Location;
        public Guid Item;
        public Guid ItemGroup;
        public Guid Unit;
        public Guid Customer;
        public Guid Outlet;
        public Guid Contract;
    }

    private async Task<Setup> IssueInvoiceAsync()
    {
        var s = await SetupAsync();
        await IssueInvoiceFromAsync(s);
        return s;
    }

    private async Task IssueInvoiceFromAsync(
        Setup s, params (Guid Item, decimal Qty, decimal Price)[] extra)
    {
        await StockAsync(s, s.Item);
        foreach (var line in extra)
            await StockAsync(s, line.Item);

        var invoice = await DocumentManager.NewDocumentAsync<SalesRealization>();
        invoice.Customer = s.Customer;
        invoice.Outlet = s.Outlet;
        invoice.Contract = s.Contract;
        invoice.Location = s.Location;
        invoice.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = 3m, UnitPrice = 5m });
        foreach (var line in extra)
            invoice.Lines.Add(new SalesInvoiceLinesTablePartRow
            {
                Item = line.Item,
                Quantity = line.Qty,
                UnitPrice = line.Price
            });
        await DocumentManager.SaveDocumentAsync(invoice);
        invoice.Subtype = SalesRealization.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(invoice);
    }

    private static Task StockAsync(Setup s, Guid item)
        => TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = item },
            new Dictionary<string, decimal> { ["Qty"] = 10m });

    private async Task<(Guid Item, Guid Group)> ExtraItemAsync(Setup s)
    {
        var groupId = await ExtraGroupAsync();
        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Other gadget";
        item.ItemGroup = groupId;
        item.UnitOfMeasure = s.Unit;
        item.IsSellable = true;
        item.Image = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==");
        item = await DictionaryManager.SaveRecordAsync(item);
        return (item.MetaId, groupId);
    }

    private async Task<Guid> ExtraGroupAsync()
    {
        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"G-{Uniq()}";
        group.Name = "Other goods";
        group = await DictionaryManager.SaveRecordAsync(group);
        return group.MetaId;
    }

    private async Task<Setup> SetupAsync()
    {
        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Euro";
        currency.Code = "EUR";
        currency.Symbol = "€";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Germany";
        country.CodeISO2 = "DE";
        country.CodeISO3 = "DEU";
        country.PhoneCode = "49";
        country = await DictionaryManager.SaveRecordAsync(country);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = "ACME GmbH";
        legalEntity.RegistrationNumber = $"REG-{Uniq()}";
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"SP-{Uniq()}";
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
        zone.Name = "Зона";
        zone.Store = store.MetaId;
        zone.IsBarcodeTracking = false;
        zone = await DictionaryManager.SaveRecordAsync(zone);

        var cellType = DictionaryManager.NewRecord<StoreCellType>();
        cellType.Code = $"PK-{Uniq()}";
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
        unit.Code = $"U-{Uniq()}";
        unit = await DictionaryManager.SaveRecordAsync(unit);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"G-{Uniq()}";
        group.Name = "Finished goods";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Gadget";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = unit.MetaId;
        item.IsSellable = true;
        item.Image = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==");
        item = await DictionaryManager.SaveRecordAsync(item);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Buyer Ltd";
        customer.CustomerType = "B2B";
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
        contract.EffectiveFrom = Origin;
        contract.LegalEntity = legalEntity.MetaId;
        contract = await DictionaryManager.SaveRecordAsync(contract);

        return new Setup
        {
            Location = cell.MetaId,
            Item = item.MetaId,
            ItemGroup = group.MetaId,
            Unit = unit.MetaId,
            Customer = customer.MetaId,
            Outlet = outlet.MetaId,
            Contract = contract.MetaId,
        };
    }
}
