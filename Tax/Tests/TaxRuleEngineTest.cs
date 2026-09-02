using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// ДВИЖОК ПРАВИЛ ОПРЕДЕЛЕНИЯ НАЛОГА.
//
// До него налоговый код на документе был один на весь стенд — строка
// TaxSettings.DefaultTaxCode. То есть «какой налог применить» решал не учёт, а
// человек, один раз и для всех сделок сразу. Правило переносит это решение в
// ДАННЫЕ: условия по контексту сделки (кто покупатель, что за товар, на сколько)
// → налоговый код.
//
// Почему проверок так много. Одиночная проверка «правило сработало» проходит
// одинаково и когда движок действительно разбирает условия, и когда он просто
// берёт первое попавшееся правило. Различают эти два мира только пары:
// несовпавшее правило ОТКАТЫВАЕТСЯ к умолчанию, из двух подходящих выигрывает
// заданное приоритетом, при равном приоритете — более специфичное, истёкшее и
// выключенное не выбираются вовсе. Плюс отдельно — что операторы сравнивают
// именно так, как написано в их описании, и что группы условий дают ИЛИ.
public class TaxRuleEngineTest : IntegrationTestScriptBase
{
    private static ITaxService Svc => GetService<ITaxService>();

    private string Uniq() => $"{Db.NewId():N}"[..8];

    private static readonly DateTime Origin = new(2020, 1, 1);
    private static readonly DateTime Today = new(2026, 6, 15);

    /// <summary>Налоговый код со своим налогом, ставкой и категорией. Каждый вызов
    /// даёт НОВЫЙ код — тесту нужно различать, какой именно выбрал движок.</summary>
    private async Task<Guid> NewTaxCodeAsync(decimal rate)
    {
        var uniq = Uniq();
        var taxId = await NewRecordAsync<Tax>(t =>
        {
            t.Code = $"T-{uniq}";
            t.Name = "Rule engine tax";
            t.Authority = Db.NewId();
            t.Jurisdiction = Db.NewId();
            t.EffectiveFrom = Origin;
        });
        var rateId = await NewRecordAsync<TaxRate>(r =>
        {
            r.Tax = taxId;
            r.Code = $"R-{uniq}";
            r.Rate = rate;
            r.EffectiveFrom = Origin;
        });
        var category = await NewRecordAsync<TaxCategory>(c =>
        {
            c.Tax = taxId;
            c.Code = $"STD-{uniq}";
            c.Treatment = "STANDARD";
        });
        return await NewRecordAsync<TaxCode>(c =>
        {
            c.Code = $"VAT-{uniq}";
            c.Name = "Rule engine code";
            c.Tax = taxId;
            c.TaxCategory = category;
            c.TaxRate = rateId;
            c.EffectiveFrom = Origin;
        });
    }

    private async Task<Guid> NewRuleAsync(
        Guid taxCode, int priority,
        DateTime? from = null, DateTime? to = null, bool disabled = false)
        => await NewRecordAsync<TaxRule>(r =>
        {
            r.Code = $"RULE-{Uniq()}";
            r.Name = "Rule";
            r.Priority = priority;
            r.TaxCode = taxCode;
            r.EffectiveFrom = from ?? Origin;
            r.EffectiveTo = to;
            r.IsDisabled = disabled;
        });

    private async Task NewConditionAsync(
        Guid rule, string field, TaxRuleOperator op, string? value = null, int group = 0)
        => await NewRecordAsync<TaxRuleCondition>(c =>
        {
            c.TaxRule = rule;
            c.Field = field;
            c.Operator = op;
            c.Value = value ?? string.Empty;
            c.ConditionGroup = group;
        });

    /// <summary>Код по умолчанию в настройках модуля — то, что движок обязан
    /// перебить, когда правило сработало, и вернуть, когда не сработало.</summary>
    private static async Task SetDefaultAsync(string code)
    {
        var manager = GetService<ZuloOne.Managers.IDictionaryManager<TaxSettings>>();
        var rows = await RecordsAsync<TaxSettings>(null);
        var settings = rows.Count > 0 ? rows[0] : await manager.NewRecordAsync();
        settings.DefaultTaxCode = code;
        settings.PricesIncludeTax = false;
        await manager.SaveRecordAsync(settings);
    }

    private static Dictionary<string, object?> Ctx(params (string Key, object? Value)[] pairs)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in pairs) d[k] = v;
        return d;
    }

    // ───────────────────────────── сценарии ──────────────────────────────────

    [IntegrationTest("Сработавшее правило определяет код вместо настройки по умолчанию")]
    public async Task RuleOverridesDefaultCode()
    {
        var ruleCode = await NewTaxCodeAsync(0.05m);
        var rule = await NewRuleAsync(ruleCode, 10);
        await NewConditionAsync(rule, "buyer.type", TaxRuleOperator.Eq, "B2B");

        var matched = await Svc.ResolveRuleAsync(Ctx(("buyer.type", "B2B")), Today);
        Assert.IsNotNull(matched, "правило с совпавшим условием обязано сработать");
        Assert.IsTrue(matched!.MetaId == rule, "сработать обязано именно заведённое правило");
        Assert.IsTrue(matched.TaxCode == ruleCode, "правило приносит СВОЙ налоговый код");
    }

    [IntegrationTest("Несовпавшее правило не срабатывает — код останется по умолчанию")]
    public async Task NoMatchLeavesDefault()
    {
        var ruleCode = await NewTaxCodeAsync(0.05m);
        var rule = await NewRuleAsync(ruleCode, 10);
        await NewConditionAsync(rule, "buyer.type", TaxRuleOperator.Eq, "B2C");

        // Контекст говорит B2B — условие правила не выполнено.
        var matched = await Svc.ResolveRuleAsync(Ctx(("buyer.type", "B2B")), Today);
        Assert.IsTrue(matched is null || matched.MetaId != rule,
            "правило на B2C не должно срабатывать на B2B-сделке");
    }

    [IntegrationTest("Из двух подходящих правил выигрывает заданное приоритетом")]
    public async Task PriorityDecides()
    {
        var loser = await NewTaxCodeAsync(0.05m);
        var winner = await NewTaxCodeAsync(0.20m);

        // Заводим в «неудобном» порядке: сначала слабое правило. Если движок берёт
        // первое попавшееся, тест это увидит.
        var weak = await NewRuleAsync(loser, 50);
        await NewConditionAsync(weak, "buyer.type", TaxRuleOperator.Eq, "B2B");
        var strong = await NewRuleAsync(winner, 10);
        await NewConditionAsync(strong, "buyer.type", TaxRuleOperator.Eq, "B2B");

        var matched = await Svc.ResolveRuleAsync(Ctx(("buyer.type", "B2B")), Today);
        Assert.IsTrue(matched?.MetaId == strong,
            "меньший Priority выигрывает: ожидали правило с приоритетом 10");
        Assert.IsTrue(matched?.TaxCode == winner, "и приносит свой код");
    }

    [IntegrationTest("При равном приоритете выигрывает более специфичное правило")]
    public async Task EqualPriorityPrefersMoreSpecific()
    {
        var general = await NewTaxCodeAsync(0.05m);
        var specific = await NewTaxCodeAsync(0.20m);

        var broad = await NewRuleAsync(general, 10);
        await NewConditionAsync(broad, "buyer.type", TaxRuleOperator.Eq, "B2B");

        var narrow = await NewRuleAsync(specific, 10);
        await NewConditionAsync(narrow, "buyer.type", TaxRuleOperator.Eq, "B2B");
        await NewConditionAsync(narrow, "item.group", TaxRuleOperator.Eq, "MEDS");

        var matched = await Svc.ResolveRuleAsync(
            Ctx(("buyer.type", "B2B"), ("item.group", "MEDS")), Today);
        Assert.IsTrue(matched?.MetaId == narrow,
            "два условия описывают сделку точнее одного — выигрывает узкое правило");
    }

    [IntegrationTest("Правило вне окна действия и выключенное правило не выбираются")]
    public async Task ExpiredAndDisabledAreIgnored()
    {
        var expiredCode = await NewTaxCodeAsync(0.05m);
        var expired = await NewRuleAsync(expiredCode, 1, from: Origin, to: new DateTime(2021, 12, 31));
        await NewConditionAsync(expired, "buyer.type", TaxRuleOperator.Eq, "B2B");

        var futureCode = await NewTaxCodeAsync(0.07m);
        var future = await NewRuleAsync(futureCode, 1, from: new DateTime(2030, 1, 1));
        await NewConditionAsync(future, "buyer.type", TaxRuleOperator.Eq, "B2B");

        var disabledCode = await NewTaxCodeAsync(0.09m);
        var disabled = await NewRuleAsync(disabledCode, 1, disabled: true);
        await NewConditionAsync(disabled, "buyer.type", TaxRuleOperator.Eq, "B2B");

        var liveCode = await NewTaxCodeAsync(0.20m);
        var live = await NewRuleAsync(liveCode, 90);
        await NewConditionAsync(live, "buyer.type", TaxRuleOperator.Eq, "B2B");

        // Приоритет у трёх «мёртвых» правил ЛУЧШЕ, чем у живого: если движок их не
        // отсекает, он выберет одно из них, и подмена будет видна.
        var matched = await Svc.ResolveRuleAsync(Ctx(("buyer.type", "B2B")), Today);
        Assert.IsTrue(matched?.MetaId == live,
            "истёкшее, будущее и выключенное правила обязаны быть отброшены даже с лучшим приоритетом");
    }

    [IntegrationTest("Границы окна действия включительны")]
    public async Task EffectiveBoundsAreInclusive()
    {
        var code = await NewTaxCodeAsync(0.20m);
        var rule = await NewRuleAsync(code, 10, from: Today, to: Today);
        await NewConditionAsync(rule, "buyer.type", TaxRuleOperator.Eq, "B2B");

        var ctx = Ctx(("buyer.type", "B2B"));
        Assert.IsTrue((await Svc.ResolveRuleAsync(ctx, Today))?.MetaId == rule,
            "день начала и окончания входит в окно");
        Assert.IsTrue((await Svc.ResolveRuleAsync(ctx, Today.AddDays(-1)))?.MetaId != rule,
            "накануне правило ещё не действует");
        Assert.IsTrue((await Svc.ResolveRuleAsync(ctx, Today.AddDays(1)))?.MetaId != rule,
            "назавтра правило уже не действует");
    }

    [IntegrationTest("Операторы сравнения работают по своему описанию")]
    public async Task OperatorsBehaveAsDocumented()
    {
        var code = await NewTaxCodeAsync(0.20m);

        // In: значение из списка; Gt: число; Between: диапазон включительно;
        // NotExists: пути в контексте нет вовсе.
        var rule = await NewRuleAsync(code, 10);
        await NewConditionAsync(rule, "buyer.type", TaxRuleOperator.In, "B2B, B2G");
        await NewConditionAsync(rule, "amount", TaxRuleOperator.Gt, "1000");
        await NewConditionAsync(rule, "lines", TaxRuleOperator.Between, "1,5");
        await NewConditionAsync(rule, "exemption", TaxRuleOperator.NotExists);

        Assert.IsTrue((await Svc.ResolveRuleAsync(
            Ctx(("buyer.type", "B2G"), ("amount", 1500m), ("lines", 5)), Today))?.MetaId == rule,
            "все четыре условия выполнены — правило срабатывает");

        Assert.IsTrue((await Svc.ResolveRuleAsync(
            Ctx(("buyer.type", "B2C"), ("amount", 1500m), ("lines", 5)), Today))?.MetaId != rule,
            "B2C не входит в список B2B/B2G");

        Assert.IsTrue((await Svc.ResolveRuleAsync(
            Ctx(("buyer.type", "B2G"), ("amount", 1000m), ("lines", 5)), Today))?.MetaId != rule,
            "Gt строгий: ровно 1000 не больше 1000");

        Assert.IsTrue((await Svc.ResolveRuleAsync(
            Ctx(("buyer.type", "B2G"), ("amount", 1500m), ("lines", 6)), Today))?.MetaId != rule,
            "6 вне диапазона 1..5");

        Assert.IsTrue((await Svc.ResolveRuleAsync(
            Ctx(("buyer.type", "B2G"), ("amount", 1500m), ("lines", 5), ("exemption", "ART-12")), Today))?.MetaId != rule,
            "NotExists ломается, когда путь в контексте появился");
    }

    [IntegrationTest("Условия одной группы соединяются И, разные группы — ИЛИ")]
    public async Task ConditionGroupsAreOred()
    {
        var code = await NewTaxCodeAsync(0.20m);
        var rule = await NewRuleAsync(code, 10);

        // (buyer.type = B2B И amount > 1000) ИЛИ (buyer.type = B2G)
        await NewConditionAsync(rule, "buyer.type", TaxRuleOperator.Eq, "B2B", group: 0);
        await NewConditionAsync(rule, "amount", TaxRuleOperator.Gt, "1000", group: 0);
        await NewConditionAsync(rule, "buyer.type", TaxRuleOperator.Eq, "B2G", group: 1);

        Assert.IsTrue((await Svc.ResolveRuleAsync(Ctx(("buyer.type", "B2B"), ("amount", 1500m)), Today))?.MetaId == rule,
            "первая группа выполнена целиком");
        Assert.IsTrue((await Svc.ResolveRuleAsync(Ctx(("buyer.type", "B2G"), ("amount", 1m)), Today))?.MetaId == rule,
            "вторая группа выполнена — сумма первой группы уже не важна");
        Assert.IsTrue((await Svc.ResolveRuleAsync(Ctx(("buyer.type", "B2B"), ("amount", 1m)), Today))?.MetaId != rule,
            "первая группа неполна, вторая не выполнена — правило молчит");
    }

    [IntegrationTest("Правило без условий — законный общий случай")]
    public async Task RuleWithoutConditionsAlwaysMatches()
    {
        var code = await NewTaxCodeAsync(0.20m);
        var catchAll = await NewRuleAsync(code, 1000);

        var matched = await Svc.ResolveRuleAsync(Ctx(("buyer.type", "B2C")), Today);
        Assert.IsTrue(matched?.MetaId == catchAll,
            "правило без условий срабатывает на любом контексте — это замена коду по умолчанию");
    }

    [IntegrationTest("Расчёт берёт код из правила и запоминает, какое правило сработало")]
    public async Task CalculationRecordsMatchedRule()
    {
        var defaultCode = await NewTaxCodeAsync(0.05m);
        var defaultRecord = await RecordAsync<TaxCode>(defaultCode);
        await SetDefaultAsync(defaultRecord!.Code);

        var ruleCode = await NewTaxCodeAsync(0.20m);
        var rule = await NewRuleAsync(ruleCode, 10);
        await NewConditionAsync(rule, "buyer.type", TaxRuleOperator.Eq, "B2B");

        var direction = await NewRecordAsync<TaxDirection>(d =>
        {
            d.Code = "OUTPUT";
            d.Name = "Output";
        });
        Assert.IsTrue(direction != Guid.Empty, "направление заведено");

        var legalEntity = await NewLegalEntityAsync();

        var calcId = await Svc.CreateCalculationAsync(
            legalEntity, "OUTPUT", 1000m, $"Rule engine {Uniq()}", Today,
            Ctx(("buyer.type", "B2B")));
        Assert.IsNotNull(calcId, "расчёт создан");

        var calc = await GetService<ZuloOne.Managers.IDocumentManager>().GetDocumentAsync<TaxCalculation>(calcId!.Value);
        Assert.IsNotNull(calc, "расчёт читается");
        Assert.IsTrue(calc!.MatchedRule == rule,
            "расчёт помнит сработавшее правило — иначе «почему такая ставка» остаётся без ответа");
        Assert.IsTrue(calc.Lines.Count == 1, "одна строка налога, факт {0}", calc.Lines.Count);
        Assert.IsTrue(calc.Lines[0].TaxCode == ruleCode,
            "код взят из ПРАВИЛА, а не из настройки по умолчанию");
        // 20% правила против 5% умолчания: числа разные, и разница — это и есть
        // доказательство, что решило правило.
        Assert.IsTrue(calc.Lines[0].TaxAmount == 200m,
            "1000 × 20% = 200 по ставке правила, факт {0} (умолчание дало бы 50)", calc.Lines[0].TaxAmount);
    }

    [IntegrationTest("Без контекста расчёт ведёт себя как раньше — по коду из настроек")]
    public async Task WithoutContextFallsBackToDefault()
    {
        var defaultCode = await NewTaxCodeAsync(0.05m);
        var defaultRecord = await RecordAsync<TaxCode>(defaultCode);
        await SetDefaultAsync(defaultRecord!.Code);

        // Правило заведено и подошло БЫ — но контекст не передан, значит движок не
        // спрашивают вовсе. Это обратная совместимость: включение движка не должно
        // менять поведение уже работающих вызовов.
        var ruleCode = await NewTaxCodeAsync(0.20m);
        var rule = await NewRuleAsync(ruleCode, 10);
        await NewConditionAsync(rule, "buyer.type", TaxRuleOperator.Eq, "B2B");

        await NewRecordAsync<TaxDirection>(d =>
        {
            d.Code = "OUTPUT";
            d.Name = "Output";
        });
        var legalEntity = await NewLegalEntityAsync();

        var calcId = await Svc.CreateCalculationAsync(
            legalEntity, "OUTPUT", 1000m, $"No context {Uniq()}", Today);
        Assert.IsNotNull(calcId, "расчёт создан");

        var calc = await GetService<ZuloOne.Managers.IDocumentManager>().GetDocumentAsync<TaxCalculation>(calcId!.Value);
        Assert.IsTrue(calc!.MatchedRule == Guid.Empty || calc.MatchedRule == default,
            "правило не спрашивали — поле пустое");
        Assert.IsTrue(calc.Lines[0].TaxAmount == 50m,
            "1000 × 5% = 50 по коду из настроек, факт {0}", calc.Lines[0].TaxAmount);
    }

    /// <summary>Юрлицо со страной и валютой — обязательные ссылки расчёта.</summary>
    private async Task<Guid> NewLegalEntityAsync()
    {
        var currency = await NewRecordAsync<Currency>(c =>
        {
            c.Name = "Euro";
            c.Code = "EUR";
            c.Symbol = "€";
        });
        var country = await NewRecordAsync<Country>(c =>
        {
            c.Name = "Germany";
            c.CodeISO2 = "DE";
            c.CodeISO3 = "DEU";
            c.PhoneCode = "49";
        });
        return await NewRecordAsync<LegalEntity>(le =>
        {
            le.Name = "ACME GmbH";
            le.RegistrationNumber = $"REG-RULE-{Uniq()}";
            le.Country = country;
            le.Currency = currency;
        });
    }
}
