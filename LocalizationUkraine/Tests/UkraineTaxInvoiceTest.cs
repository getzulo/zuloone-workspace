using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Податкова накладна до ЄРПН: конверт, ворота і те, що НЕ закриває накладну.
//
// ЩО ТУТ СВІДОМО НЕ ПЕРЕВІРЯЄТЬСЯ. Обмін з оператором — відправлення й
// отримання квитанції — не покритий, і не тому що забули: оператора тут немає.
// Черга віддає лише ЗАВЕРШЕНІ повідомлення, а позначити повідомлення
// завершеним зі скрипта не можна; відповідь оператора теж нізвідки взяти.
// Писати тест, який вдає, що обмін стався, було б гірше за відсутність тесту:
// він був би зеленим і на зламаному контурі. Застосування ісходу до конверта
// покрите окремо — TaxDocumentOutcomeTest.
public class UkraineTaxInvoiceTest : IntegrationTestScriptBase
{
    private const string FormCode = "J1201010";

    private static IDictionaryManager Dict => GetService<IDictionaryManager>();
    private static IDocumentManager Documents => GetService<IDocumentManager>();
    private static IUaEInvoice EInvoice => GetService<IUaEInvoice>();

    [IntegrationTest("Накладна постає з проведеного рахунку і лишається поданою до квитанції")]
    public async Task InvoiceProducesAnIssuedEnvelope()
    {
        var env = await SetupAsync();
        await ConfigureAsync(enabled: true, formCode: FormCode);
        var invoice = await InvoiceAsync(env);

        var id = await EInvoice.EnsureForInvoiceAsync(invoice);
        Assert.IsTrue(id is Guid, "конверт мав постати");

        var envelope = await Documents.GetDocumentAsync<TaxDocument>(id!.Value);

        // Issued, а НЕ Cleared: оператор ще нічого не відповів. Зареєстрованою
        // накладну робить квитанція ДПС, а не факт відправлення.
        Assert.IsTrue(envelope!.Subtype == "Issued", "підтип Issued, факт {0}", envelope.Subtype);
        Assert.IsTrue(envelope.EInvoiceKind == "INVOICE", "вид INVOICE, факт {0}", envelope.EInvoiceKind);
        Assert.IsTrue(envelope.InvoiceType == FormCode, "код форми на конверті, факт {0}", envelope.InvoiceType);
        Assert.IsTrue(envelope.Uuid != Guid.Empty, "UUID видано");

        var payload = Convert.ToString(envelope.Payload) ?? "";
        Assert.IsTrue(payload.Contains("\"NAME\":\"FIRM_NAME\""), "payload — клітинка MakeDoc FIRM_NAME з прикладу M.E.Doc");
        Assert.IsTrue(payload.Contains("\"NAME\":\"TAB1_A13\"") || payload.Contains("\"TAB\":0"),
            "payload — масив TAB/LINE/NAME/VALUE, не вигадані імена бланка");
        Assert.IsTrue(payload.StartsWith("["), "тіло MakeDoc — JSON-масив");
        Assert.IsTrue(!payload.Contains("<"), "payload — поля M.E.Doc, не XML: XML складає оператор");
    }

    [IntegrationTest("Кредит-нота дає розрахунок коригування з посиланням на рахунок")]
    public async Task CreditNoteProducesAnAdjustmentEnvelope()
    {
        var env = await SetupAsync();
        await ConfigureAsync(enabled: true, formCode: FormCode);
        var invoice = await InvoiceAsync(env);
        var note = await CreditNoteAsync(env, invoice);

        var id = await EInvoice.EnsureForCreditNoteAsync(note);
        Assert.IsTrue(id is Guid, "конверт розрахунку мав постати");

        var envelope = await Documents.GetDocumentAsync<TaxDocument>(id!.Value);
        Assert.IsTrue(envelope!.Subtype == "Issued", "підтип Issued, факт {0}", envelope.Subtype);
        Assert.IsTrue(envelope.EInvoiceKind == "ADJUSTMENT", "вид ADJUSTMENT, факт {0}", envelope.EInvoiceKind);
        Assert.IsTrue(envelope.InvoiceType == "J12012010", "код форми коригування, факт {0}", envelope.InvoiceType);

        var payload = Convert.ToString(envelope.Payload) ?? "";
        Assert.IsTrue(payload.StartsWith("["), "тіло MakeDoc — JSON-масив");
        Assert.IsTrue(payload.Contains("\"NAME\":\"FIRM_NAME\""), "спільні клітинки продавця");
        Assert.IsTrue(payload.Contains("\"NAME\":\"CORRCMPL\""), "РК — CORRCMPL з прикладу MakeDoc: дата//номер початкової ПН");
        Assert.IsTrue(payload.Contains("\"NAME\":\"N15\""), "дата РК — N15, не N11 накладної");
        Assert.IsTrue(payload.Contains("\"NAME\":\"N1_13\""), "номер початкової ПН — N1_13");
        Assert.IsTrue(!payload.Contains("\"NAME\":\"TAB1_A13\""), "рядки РК — TAB1_A3, не клітинки ПН");
        Assert.IsTrue(!payload.Contains("<"), "payload — поля, а не XML");
    }

    [IntegrationTest("Без початкового рахунку розрахунок коригування не формується")]
    public async Task CreditNoteWithoutOriginalProducesNothing()
    {
        var env = await SetupAsync();
        await ConfigureAsync(enabled: true, formCode: FormCode);
        var note = await CreditNoteAsync(env, originalInvoice: Guid.Empty);

        Assert.IsTrue(await EInvoice.EnsureForCreditNoteAsync(note) is null,
            "розрахунок без початкового рахунку ДПС не прийме — конверта бути не повинно");
    }

    [IntegrationTest("Без коду форми коригування розрахунок не формується")]
    public async Task MissingAdjustmentFormCodeProducesNothing()
    {
        var env = await SetupAsync();
        await ConfigureAsync(enabled: true, formCode: FormCode, adjustmentFormCode: "");
        var invoice = await InvoiceAsync(env);
        var note = await CreditNoteAsync(env, invoice);

        Assert.IsTrue(await EInvoice.EnsureForCreditNoteAsync(note) is null,
            "без коду форми коригування конверта бути не повинно");
    }

    [IntegrationTest("Повторний виклик не плодить другий розрахунок на ту саму кредит-ноту")]
    public async Task SecondCreditNoteCallReturnsTheSameEnvelope()
    {
        var env = await SetupAsync();
        await ConfigureAsync(enabled: true, formCode: FormCode);
        var invoice = await InvoiceAsync(env);
        var note = await CreditNoteAsync(env, invoice);

        var first = await EInvoice.EnsureForCreditNoteAsync(note);
        var second = await EInvoice.EnsureForCreditNoteAsync(note);
        Assert.IsTrue(first == second, "має повернутися той самий конверт, факт {0} і {1}", first, second);
    }

    [IntegrationTest("Вимкнений контур накладних не формує")]
    public async Task DisabledContourProducesNothing()
    {
        var env = await SetupAsync();
        await ConfigureAsync(enabled: false, formCode: FormCode);
        var invoice = await InvoiceAsync(env);

        Assert.IsTrue(await EInvoice.EnsureForInvoiceAsync(invoice) is null,
            "при вимкненому контурі конверта бути не повинно");
    }

    [IntegrationTest("Без коду форми ДПС накладна не формується")]
    public async Task MissingFormCodeProducesNothing()
    {
        var env = await SetupAsync();
        await ConfigureAsync(enabled: true, formCode: "");
        var invoice = await InvoiceAsync(env);

        // Вигадати номер форми не можна: помилка в ньому — це помилка в
        // поданні, яку побачать уже в кабінеті.
        Assert.IsTrue(await EInvoice.EnsureForInvoiceAsync(invoice) is null,
            "без коду форми конверта бути не повинно");
    }

    [IntegrationTest("Спрощенець без ПДВ накладних не виписує")]
    public async Task NonVatRegimeProducesNothing()
    {
        var env = await SetupAsync(UaTaxRegime.SimplifiedNoVat);
        await ConfigureAsync(enabled: true, formCode: FormCode);
        var invoice = await InvoiceAsync(env);

        Assert.IsTrue(await EInvoice.EnsureForInvoiceAsync(invoice) is null,
            "неплатник ПДВ накладну не складає");
    }

    [IntegrationTest("Повторний виклик не плодить другу накладну на той самий рахунок")]
    public async Task SecondCallReturnsTheSameEnvelope()
    {
        var env = await SetupAsync();
        await ConfigureAsync(enabled: true, formCode: FormCode);
        var invoice = await InvoiceAsync(env);

        var first = await EInvoice.EnsureForInvoiceAsync(invoice);
        var second = await EInvoice.EnsureForInvoiceAsync(invoice);

        // Проведення відпрацьовує по документу не один раз, і друга накладна на
        // той самий рахунок — це друга реєстрація в ЄРПН, тобто штраф.
        Assert.IsTrue(first == second, "має повернутися той самий конверт, факт {0} і {1}", first, second);

        var all = await Documents.QueryDocumentsAsync<TaxDocument>($"SourceDocumentId = '{invoice}'");
        Assert.IsTrue(all.Count == 1, "конверт рівно один, факт {0}", all.Count);
    }

    // ---- обстановка -------------------------------------------------------

    private async Task<(Guid LegalEntity, Guid Customer, Guid Contract, Guid Outlet, Guid Location)> SetupAsync(
        UaTaxRegime regime = UaTaxRegime.VatPayer)
    {
        var currency = Dict.NewRecord<Currency>();
        currency.Name = "Hryvnia";
        currency.Code = $"{Db.NewId():N}"[..3].ToUpperInvariant();
        currency.Symbol = "₴";
        currency = await Dict.SaveRecordAsync(currency);

        var country = Dict.NewRecord<Country>();
        country.Name = "Ukraine";
        country.CodeISO2 = "UA";
        country.CodeISO3 = "UKR";
        country.PhoneCode = "380";
        country = await Dict.SaveRecordAsync(country);

        var entity = Dict.NewRecord<LegalEntity>();
        entity.Name = "ТОВ Накладна";
        entity.RegistrationNumber = $"REG-{Db.NewId():N}"[..12];
        entity.TaxRegistrationNumber = "123456789012";
        entity.Country = country.MetaId;
        entity.Currency = currency.MetaId;
        entity = await Dict.SaveRecordAsync(entity);

        // Режим живе в полі-розширенні, тому пишеться мішком і ЧИСЛОМ: у
        // згенерований клас юрособи це поле не потрапляє.
        if (regime != UaTaxRegime.VatPayer)
            // Країна й валюта йдуть у тому ж мішку навмисно: подія оновлення
            // бачить ЛИШЕ ті колонки, що пишуться, і без них перевірка юрособи
            // вважає їх порожніми.
            await Db.UpdateAsync("LegalEntity", entity.MetaId,
                new System.Collections.Generic.Dictionary<string, object?>
                {
                    ["UaTaxRegime"] = (int)regime,
                    ["Country"] = country.MetaId,
                    ["Currency"] = currency.MetaId,
                });

        var divisionType = Dict.NewRecord<DivisionType>();
        divisionType.Code = $"HQ-{Db.NewId():N}"[..12];
        divisionType.Name = "Head office";
        divisionType = await Dict.SaveRecordAsync(divisionType);

        var division = Dict.NewRecord<Division>();
        division.Name = "Київ";
        division.LegalEntity = entity.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await Dict.SaveRecordAsync(division);

        var store = Dict.NewRecord<Store>();
        store.Name = "Склад";
        store.Division = division.MetaId;
        store.IsSimple = true;
        store = await Dict.SaveRecordAsync(store);

        var zone = Dict.NewRecord<StoreZone>();
        zone.Name = "Зона";
        zone.Store = store.MetaId;
        zone.IsBarcodeTracking = false;
        zone = await Dict.SaveRecordAsync(zone);

        var cellType = Dict.NewRecord<StoreCellType>();
        cellType.Code = $"PICK-{Db.NewId():N}"[..12];
        cellType.Name = "Picking";
        cellType = await Dict.SaveRecordAsync(cellType);

        var cell = Dict.NewRecord<StoreCell>();
        cell.Name = "P-01";
        cell.Type = cellType.MetaId;
        cell.StoreZone = zone.MetaId;
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = 1;
        cell = await Dict.SaveRecordAsync(cell);

        var priceType = Dict.NewRecord<PriceType>();
        priceType.Name = "Гурт";
        priceType.Direction = PriceDirection.Sale;
        priceType = await Dict.SaveRecordAsync(priceType);

        var customer = Dict.NewRecord<Customer>();
        customer.Name = "ТОВ Покупець";
        customer.CustomerType = "B2B";
        customer.PriceType = priceType.MetaId;
        customer = await Dict.SaveRecordAsync(customer);

        var outlet = Dict.NewRecord<CustomerOutlet>();
        outlet.Name = "Магазин";
        outlet.Customer = customer.MetaId;
        outlet = await Dict.SaveRecordAsync(outlet);

        var contract = Dict.NewRecord<SalesContract>();
        contract.Name = $"D-{Db.NewId():N}"[..10];
        contract.Outlet = outlet.MetaId;
        contract.Currency = currency.MetaId;
        contract.SettlementKind = SettlementKind.Credit;
        contract.EffectiveFrom = new DateTime(2020, 1, 1);
        contract.LegalEntity = entity.MetaId;
        contract.PriceType = priceType.MetaId;
        contract = await Dict.SaveRecordAsync(contract);

        return (entity.MetaId, customer.MetaId, contract.MetaId, outlet.MetaId, cell.MetaId);
    }

    private async Task ConfigureAsync(bool enabled, string formCode, string adjustmentFormCode = "J12012010")
    {
        var rows = await Dict.GetRecordsAsync<LocalizationUkraineSettings>(null, 1);
        var settings = rows.Count > 0 ? rows[0] : Dict.NewRecord<LocalizationUkraineSettings>();
        settings.EInvoiceEnabled = enabled;
        settings.TaxInvoiceFormCode = formCode;
        settings.TaxInvoiceAdjustmentFormCode = adjustmentFormCode;
        await Dict.SaveRecordAsync(settings);
    }

    /// <summary>
    /// Рахунок у чернетці. Сервіс читає ДОКУМЕНТ, а не проводки, тому тягти
    /// сюди склад і весь цикл продажу означало б перевіряти заразом і їх.
    /// Зв'язку «проведення → накладна» тримає обробник, і це окрема річ.
    /// </summary>
    private async Task<Guid> InvoiceAsync(
        (Guid LegalEntity, Guid Customer, Guid Contract, Guid Outlet, Guid Location) env)
    {
        var invoice = await Documents.NewDocumentAsync<SalesRealization>();
        invoice.LegalEntity = env.LegalEntity;
        invoice.Customer = env.Customer;
        invoice.Contract = env.Contract;
        invoice.Outlet = env.Outlet;
        invoice.Location = env.Location;
        invoice.TaxRateApplied = 0.20m;
        await Documents.SaveDocumentAsync(invoice);
        return invoice.MetaId;
    }

    private async Task<Guid> CreditNoteAsync(
        (Guid LegalEntity, Guid Customer, Guid Contract, Guid Outlet, Guid Location) env,
        Guid originalInvoice)
    {
        var note = await Documents.NewDocumentAsync<SalesCreditNote>();
        note.LegalEntity = env.LegalEntity;
        note.Customer = env.Customer;
        note.Contract = env.Contract;
        note.Outlet = env.Outlet;
        note.OriginalInvoice = originalInvoice;
        note.TaxRateApplied = 0.20m;
        await Documents.SaveDocumentAsync(note);
        return note.MetaId;
    }
}
