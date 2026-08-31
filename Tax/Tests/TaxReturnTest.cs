using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Сборка налоговой декларации из леджера.
//
// Тест доказывает главное сомнение архитектуры: разрезы TaxLedger хранятся не
// колонками, а ссылкой на набор значений аналитик, и вопрос был в том, можно ли
// вообще получить из него разрез «по коду и направлению». Можно — движения
// разворачиваются через набор.
//
// Проверяется именно ДОКУМЕНТ, а не возвращённая сводка: декларация обязана
// сохраниться, иначе сданное невозможно предъявить.
public class TaxReturnTest : IntegrationTestScriptBase
{
    private static ITaxReturnService Svc => GetService<ITaxReturnService>();

    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();

    private string Uniq() => $"{Db.NewId():N}"[..8];

    private async Task<Guid> NewDirectionAsync(string code)
        => await NewRecordAsync<TaxDirection>(d => { d.Code = code; d.Name = code; });

    private async Task<Guid> NewCodeAsync(string uniq, string name)
    {
        var from = new DateTime(2020, 1, 1);
        var tax = await NewRecordAsync<Tax>(t =>
        {
            t.Code = $"T-{uniq}";
            t.Name = "Test tax";
            t.TaxType = Db.NewId();
            t.Authority = Db.NewId();
            t.Jurisdiction = Db.NewId();
            t.EffectiveFrom = from;
        });
        var rate = await NewRecordAsync<TaxRate>(r =>
        {
            r.Tax = tax;
            r.Code = $"R-{uniq}";
            r.Rate = 0.15m;
            r.EffectiveFrom = from;
        });
        var category = await NewRecordAsync<TaxCategory>(c =>
        {
            c.Tax = tax;
            c.Code = $"STD-{uniq}";
            c.Treatment = "STANDARD";
        });
        return await NewRecordAsync<TaxCode>(c =>
        {
            c.Code = $"C-{uniq}";
            c.Name = name;
            c.Tax = tax;
            c.TaxCategory = category;
            c.TaxRate = rate;
            c.EffectiveFrom = from;
        });
    }

    private async Task PostAsync(Guid legalEntity, Guid code, Guid direction, DateTime on, decimal taxBase, decimal amount)
        => await Db.PostMovementAsync("TaxLedger", on,
            new Dictionary<string, object?>
            {
                ["TaxCode"] = code,
                ["TaxDirection"] = direction,
                ["LegalEntity"] = legalEntity,
            },
            new Dictionary<string, decimal> { ["TaxBase"] = taxBase, ["TaxAmount"] = amount });

    private async Task<TaxReturn> BuildAsync(Guid legalEntity, DateTime from, DateTime to)
    {
        var id = await Svc.BuildAsync(legalEntity, from, to);
        var doc = await DocumentManager.GetDocumentAsync<TaxReturn>(id);
        Assert.IsNotNull(doc, "декларация должна сохраниться как документ");
        return doc!;
    }

    [IntegrationTest("Декларация за период: к уплате = выходной минус входной")]
    public async Task NetPayableIsOutputMinusInput()
    {
        var le = Db.NewId();
        var output = await NewDirectionAsync("OUTPUT");
        var input = await NewDirectionAsync("INPUT");
        var code = await NewCodeAsync(Uniq(), "Standard 15%");
        var day = new DateTime(2026, 3, 15);

        await PostAsync(le, code, output, day, 1000m, 150m);   // продажи
        await PostAsync(le, code, output, day, 200m, 30m);     // ещё продажи — сложатся
        await PostAsync(le, code, input, day, 400m, 60m);      // закупки

        var doc = await BuildAsync(le, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));

        Assert.IsTrue(doc.OutputTax == 180m, "выходной налог 150 + 30 = 180, факт {0}", doc.OutputTax);
        Assert.IsTrue(doc.InputTax == 60m, "входной налог 60, факт {0}", doc.InputTax);
        Assert.IsTrue(doc.NetPayable == 120m, "к уплате 180 − 60 = 120, факт {0}", doc.NetPayable);
        Assert.IsTrue(doc.Subtype == "Draft", "декларация создаётся черновиком, факт {0}", doc.Subtype);

        // Разрез, ради которого регистр и заведён: строка на пару (код, направление).
        Assert.IsTrue(doc.Lines.Count == 2, "две строки — по одной на направление, факт {0}", doc.Lines.Count);
        var outLine = doc.Lines.First(l => l.Direction == output);
        Assert.IsTrue(outLine.TaxBase == 1200m, "база выходного 1000 + 200 = 1200, факт {0}", outLine.TaxBase);
        Assert.IsTrue(outLine.TaxCode == code, "строка ссылается на налоговый код");
    }

    [IntegrationTest("Границы периода включительные, а соседние месяцы не попадают")]
    public async Task PeriodBoundsAreInclusive()
    {
        var le = Db.NewId();
        var output = await NewDirectionAsync("OUTPUT");
        var code = await NewCodeAsync(Uniq(), "Standard");

        await PostAsync(le, code, output, new DateTime(2026, 3, 1), 100m, 15m);    // первый день
        await PostAsync(le, code, output, new DateTime(2026, 3, 31), 100m, 15m);   // последний день
        await PostAsync(le, code, output, new DateTime(2026, 2, 28), 999m, 99m);   // прошлый месяц
        await PostAsync(le, code, output, new DateTime(2026, 4, 1), 999m, 99m);    // следующий

        var doc = await BuildAsync(le, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));

        Assert.IsTrue(doc.OutputTax == 30m,
            "в марте только два движения по 15; потерянный последний день — потерянные документы, факт {0}", doc.OutputTax);
    }

    [IntegrationTest("Чужое юрлицо в декларацию не попадает")]
    public async Task OtherLegalEntityIsExcluded()
    {
        var mine = Db.NewId();
        var other = Db.NewId();
        var output = await NewDirectionAsync("OUTPUT");
        var code = await NewCodeAsync(Uniq(), "Standard");
        var day = new DateTime(2026, 3, 10);

        await PostAsync(mine, code, output, day, 100m, 15m);
        await PostAsync(other, code, output, day, 900m, 135m);

        var doc = await BuildAsync(mine, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));

        Assert.IsTrue(doc.OutputTax == 15m, "только своё юрлицо, факт {0}", doc.OutputTax);
        Assert.IsTrue(doc.Lines.Count == 1, "одна строка, факт {0}", doc.Lines.Count);
    }

    [IntegrationTest("Пустой период даёт нулевую декларацию, а не отсутствие ответа")]
    public async Task EmptyPeriodIsZero()
    {
        var doc = await BuildAsync(Db.NewId(), new DateTime(2026, 7, 1), new DateTime(2026, 7, 31));

        Assert.IsTrue(doc.Lines.Count == 0 && doc.NetPayable == 0m,
            "период без движений — нулевая декларация, а не ошибка; строк {0}, к уплате {1}",
            doc.Lines.Count, doc.NetPayable);
    }

    [IntegrationTest("Сданная декларация заморожена — задним числом её не правят")]
    public async Task FiledReturnIsReadOnly()
    {
        var le = Db.NewId();
        var output = await NewDirectionAsync("OUTPUT");
        var code = await NewCodeAsync(Uniq(), "Standard");

        await PostAsync(le, code, output, new DateTime(2026, 3, 10), 100m, 15m);
        var doc = await BuildAsync(le, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));

        await Db.ChangeSubtypeAsync("TaxReturn", doc.MetaId, "Filed");

        var filed = await DocumentManager.GetDocumentAsync<TaxReturn>(doc.MetaId);
        Assert.IsTrue(filed!.Subtype == "Filed", "декларация перешла в «Сдана», факт {0}", filed.Subtype);

        // Сданное в налоговый орган нельзя тихо переписать: подтип помечен
        // isReadOnly, и платформа обязана отказать в записи.
        filed.NetPayable = 999m;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DocumentManager.SaveDocumentAsync(filed),
            "правка сданной декларации должна отклоняться");
    }
}
