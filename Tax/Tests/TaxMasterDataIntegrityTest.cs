using System;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные классы сущностей. Тест-скриптам этот namespace НЕ приходит
// глобальным using'ом.
using ZuloOne.Runtime.Generated;

// ЦЕЛОСТНОСТЬ НАЛОГОВЫХ МАСТЕР-ДАННЫХ.
//
// Код налога ссылается на налог тремя путями — напрямую, через категорию и через
// ставку. Платформа проверяет только существование ссылок, но не то, что все три
// ведут к одному налогу. Ставки же обязаны образовывать НЕПЕРЕСЕКАЮЩУЮСЯ историю:
// на любую дату у налога действует ровно одна. Оба правила раньше не проверялись
// нигде — второе всплывало исключением уже при выпуске счёта.
//
// Каждый кейс проверяет ТЕКСТ отказа, а не факт исключения: голый catch зеленел бы
// от любой поломки, включая опечатку в имени справочника.
public class TaxMasterDataIntegrityTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    private static readonly DateTime From = new DateTime(2020, 1, 1);

    private async Task<Guid> NewTaxAsync(string label)
    {
        var authority = DictionaryManager.NewRecord<TaxAuthority>();
        authority.Code = $"AU-{Db.NewId():N}"[..10];
        authority.Name = "Authority " + label;
        authority.CountryCode = "SA";
        authority.IsActive = true;
        authority = await DictionaryManager.SaveRecordAsync(authority);

        var jurisdiction = DictionaryManager.NewRecord<TaxJurisdiction>();
        jurisdiction.Code = $"JU-{Db.NewId():N}"[..10];
        jurisdiction.Name = "Jurisdiction " + label;
        jurisdiction.CountryCode = "SA";
        jurisdiction.Level = 0;
        jurisdiction = await DictionaryManager.SaveRecordAsync(jurisdiction);

        var tax = DictionaryManager.NewRecord<Tax>();
        tax.Code = $"TX-{Db.NewId():N}"[..10];
        tax.Name = "Tax " + label;
        tax.Authority = authority.MetaId;
        tax.Jurisdiction = jurisdiction.MetaId;
        tax.EffectiveFrom = From;
        return (await DictionaryManager.SaveRecordAsync(tax)).MetaId;
    }

    private async Task<TaxRate> NewRateAsync(Guid tax, decimal rate, DateTime from, DateTime? to = null)
    {
        var r = DictionaryManager.NewRecord<TaxRate>();
        r.Tax = tax;
        r.Code = $"R-{Db.NewId():N}"[..10];
        r.Rate = rate;
        r.EffectiveFrom = from;
        if (to.HasValue) r.EffectiveTo = to.Value;
        return await DictionaryManager.SaveRecordAsync(r);
    }

    private async Task<TaxCategory> NewCategoryAsync(Guid tax, string name)
    {
        var c = DictionaryManager.NewRecord<TaxCategory>();
        c.Tax = tax;
        c.Code = $"CT-{Db.NewId():N}"[..10];
        c.Name = name;
        c.Treatment = "STANDARD";
        return await DictionaryManager.SaveRecordAsync(c);
    }

    [IntegrationTest("Код налога не принимает категорию чужого налога")]
    public async Task ForeignCategoryIsRejected()
    {
        var taxA = await NewTaxAsync("A");
        var taxB = await NewTaxAsync("B");
        var categoryOfB = await NewCategoryAsync(taxB, "Категория налога Б");

        var code = DictionaryManager.NewRecord<TaxCode>();
        code.Code = $"C-{Db.NewId():N}"[..10];
        code.Name = "Код с чужой категорией";
        code.Tax = taxA;
        code.TaxCategory = categoryOfB.MetaId;
        code.EffectiveFrom = From;

        var reason = string.Empty;
        try { await DictionaryManager.SaveRecordAsync(code); }
        catch (Exception ex) { reason = ex.Message; }

        Assert.IsTrue(reason.Contains("другому налогу"),
            "категория чужого налога обязана быть отклонена с внятной причиной, факт: {0}", reason);
    }

    [IntegrationTest("Код налога не принимает ставку чужого налога")]
    public async Task ForeignRateIsRejected()
    {
        var taxA = await NewTaxAsync("A");
        var taxB = await NewTaxAsync("B");
        var rateOfB = await NewRateAsync(taxB, 0.20m, From);

        var code = DictionaryManager.NewRecord<TaxCode>();
        code.Code = $"C-{Db.NewId():N}"[..10];
        code.Name = "Код с чужой ставкой";
        code.Tax = taxA;
        code.TaxRate = rateOfB.MetaId;
        code.EffectiveFrom = From;

        var reason = string.Empty;
        try { await DictionaryManager.SaveRecordAsync(code); }
        catch (Exception ex) { reason = ex.Message; }

        Assert.IsTrue(reason.Contains("другому налогу"),
            "ставка чужого налога обязана быть отклонена с внятной причиной, факт: {0}", reason);
    }

    [IntegrationTest("Пересекающиеся окна ставок одного налога отклоняются при вводе")]
    public async Task OverlappingRateWindowsAreRejected()
    {
        // Раньше это ловилось ИСКЛЮЧЕНИЕМ ПРИ ВЫПУСКЕ СЧЁТА: TaxService находил две
        // подходящие ставки и отказывался считать. Ошибка мастер-данных
        // останавливала операционную работу и всплывала не там, где её допустили.
        var tax = await NewTaxAsync("A");
        await NewRateAsync(tax, 0.15m, From);   // окно открыто справа: «с 2020 и далее»

        var overlapping = DictionaryManager.NewRecord<TaxRate>();
        overlapping.Tax = tax;
        overlapping.Code = $"R-{Db.NewId():N}"[..10];
        overlapping.Rate = 0.17m;
        overlapping.EffectiveFrom = new DateTime(2026, 1, 1);   // попадает внутрь открытого окна

        var reason = string.Empty;
        try { await DictionaryManager.SaveRecordAsync(overlapping); }
        catch (Exception ex) { reason = ex.Message; }

        Assert.IsTrue(reason.Contains("пересекается"),
            "пересечение окон обязано быть отклонено при вводе, факт: {0}", reason);
    }

    [IntegrationTest("Смежные окна ставок принимаются: закрыл предыдущую — заводи следующую")]
    public async Task AdjacentRateWindowsAreAccepted()
    {
        // Обратная сторона проверки: она не должна мешать нормальному сценарию
        // смены ставки. Закрываем старую датой окончания, следующая начинается на
        // следующий день — пересечения нет, обе ставки сосуществуют.
        var tax = await NewTaxAsync("A");
        await NewRateAsync(tax, 0.15m, From, new DateTime(2025, 12, 31));

        var next = DictionaryManager.NewRecord<TaxRate>();
        next.Tax = tax;
        next.Code = $"R-{Db.NewId():N}"[..10];
        next.Rate = 0.17m;
        next.EffectiveFrom = new DateTime(2026, 1, 1);
        var saved = await DictionaryManager.SaveRecordAsync(next);

        Assert.IsTrue(saved.MetaId != Guid.Empty,
            "смена ставки без пересечения окон обязана проходить");
    }

    [IntegrationTest("Перевёрнутое окно действия отклоняется")]
    public async Task InvertedWindowIsRejected()
    {
        var tax = await NewTaxAsync("A");

        var inverted = DictionaryManager.NewRecord<TaxRate>();
        inverted.Tax = tax;
        inverted.Code = $"R-{Db.NewId():N}"[..10];
        inverted.Rate = 0.15m;
        inverted.EffectiveFrom = new DateTime(2026, 12, 31);
        inverted.EffectiveTo = new DateTime(2026, 1, 1);

        var reason = string.Empty;
        try { await DictionaryManager.SaveRecordAsync(inverted); }
        catch (Exception ex) { reason = ex.Message; }

        Assert.IsTrue(reason.Contains("наоборот"),
            "окно с началом позже окончания обязано быть отклонено, факт: {0}", reason);
    }

    [IntegrationTest("Категория без налога отклоняется")]
    public async Task CategoryWithoutTaxIsRejected()
    {
        var orphan = DictionaryManager.NewRecord<TaxCategory>();
        orphan.Code = $"CT-{Db.NewId():N}"[..10];
        orphan.Name = "Категория без налога";
        orphan.Treatment = "STANDARD";

        var reason = string.Empty;
        try { await DictionaryManager.SaveRecordAsync(orphan); }
        catch (Exception ex) { reason = ex.Message; }

        Assert.IsTrue(reason.Contains("Укажите налог"),
            "категория без налога обязана быть отклонена, факт: {0}", reason);
    }
}
