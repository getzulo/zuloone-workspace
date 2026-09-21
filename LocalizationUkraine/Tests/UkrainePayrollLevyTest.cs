using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Зарплатные платежи Украины: ЄСВ, ПДФО, военный сбор.
//
// ТРИ ПЛАТЕЖА ЖИВУТ В ДВУХ РАЗНЫХ МЕСТАХ, И ТЕСТ ПРОВЕРЯЕТ ИМЕННО ЭТО.
// ЄСВ — взнос РАБОТОДАТЕЛЯ, его полностью покрывает контур соцстраха HR:
// ставки лежат данными в HRSettings, кода под Украину не написано ни строчки.
// ПДФО и военный сбор — УДЕРЖАНИЯ у работника, их считает эта модель.
//
// Разделение не косметическое: у ЄСВ есть максимальная база (15 минимальных
// зарплат), у ПДФО и ВЗ потолка нет вовсе. Тест DeductionsIgnoreTheEsvCeiling
// существует ровно затем, чтобы кто-нибудь однажды не «переиспользовал» готовую
// базу соцвзноса и не занизил удержания всем, кто зарабатывает выше потолка.
public class UkrainePayrollLevyTest : IntegrationTestScriptBase
{
    private const decimal Esv = 0.22m;
    private const decimal Pdfo = 0.18m;
    private const decimal Vz = 0.05m;

    // 15 минимальных зарплат при мінімалці 8000 — порядок величины настоящий.
    private const decimal EsvCeiling = 120000m;

    private static IDictionaryManager Dict => GetService<IDictionaryManager>();
    private static IDocumentManager Documents => GetService<IDocumentManager>();
    private static ITotalsManager Totals => GetService<ITotalsManager>();

    [IntegrationTest("Украина: ЄСВ платит работодатель, с работника не удерживается ничего")]
    public async Task EsvIsEmployerOnly()
    {
        var env = await SetupAsync();
        await ConfigureHrAsync();

        await AccrueAsync(env.Division, env.Employee, 10000m);

        var fund = await FundAsync(env.Employee);
        Assert.IsTrue(fund.Employer == 2200m,
            "ЄСВ 22% от 10000 = 2200 со стороны работодателя, факт {0}", fund.Employer);
        Assert.IsTrue(fund.Employee == 0m,
            "с работника ЄСВ не удерживается — ожидали 0, факт {0}", fund.Employee);
    }

    [IntegrationTest("Украина: из зарплаты удержаны ПДФО и военный сбор, каждый своим кодом")]
    public async Task IncomeTaxAndLevyAreWithheldSeparately()
    {
        var env = await SetupAsync();
        await ConfigureHrAsync();
        var codes = await ConfigureUaAsync();

        await AccrueAsync(env.Division, env.Employee, 10000m);

        var pdfo = await LevyAsync(env.Entity, env.Employee, codes.IncomeTax);
        Assert.IsTrue(pdfo.Amount == 1800m, "ПДФО 18% от 10000 = 1800, факт {0}", pdfo.Amount);
        Assert.IsTrue(pdfo.Base == 10000m, "база ПДФО = начисленному, факт {0}", pdfo.Base);

        var levy = await LevyAsync(env.Entity, env.Employee, codes.MilitaryLevy);
        Assert.IsTrue(levy.Amount == 500m, "військовий збір 5% от 10000 = 500, факт {0}", levy.Amount);

        // Свернуть их в одну сумму 2300 арифметически можно, подать — нельзя.
        Assert.IsTrue(codes.IncomeTax != codes.MilitaryLevy,
            "ПДФО и ВЗ обязаны идти разными кодами налога");
    }

    [IntegrationTest("Украина: на руки идёт начисленное минус удержанное, а не начисленное")]
    public async Task NetPayIsGrossLessWithholdings()
    {
        var env = await SetupAsync();
        await ConfigureHrAsync();
        await ConfigureUaAsync();

        await AccrueAsync(env.Division, env.Employee, 10000m);

        // 10000 − 1800 ПДФО − 500 ВЗ = 7700. ЄСВ здесь не вычитается: он сверху.
        var liability = await LiabilityAsync(env.Employee);
        Assert.IsTrue(liability == 7700m,
            "к выплате 10000 − 1800 − 500 = 7700, факт {0}", liability);
    }

    [IntegrationTest("Украина: потолок ЄСВ не занижает ПДФО и военный сбор")]
    public async Task DeductionsIgnoreTheEsvCeiling()
    {
        var env = await SetupAsync();
        await ConfigureHrAsync();
        var codes = await ConfigureUaAsync();

        // Зарплата ВЫШЕ потолка ЄСВ: взнос считается от потолка, удержания — от
        // всей суммы. Если однажды кто-то возьмёт для ПДФО готовую базу
        // соцвзноса, красным станет именно этот тест.
        await AccrueAsync(env.Division, env.Employee, 150000m);

        var fund = await FundAsync(env.Employee);
        Assert.IsTrue(fund.Employer == EsvCeiling * Esv,
            "ЄСВ обязан считаться от потолка {0}, факт {1}", EsvCeiling * Esv, fund.Employer);

        var pdfo = await LevyAsync(env.Entity, env.Employee, codes.IncomeTax);
        Assert.IsTrue(pdfo.Base == 150000m,
            "база ПДФО — всё начисление, потолка у неё нет; факт {0}", pdfo.Base);
        Assert.IsTrue(pdfo.Amount == 150000m * Pdfo,
            "ПДФО 18% от 150000 = {0}, факт {1}", 150000m * Pdfo, pdfo.Amount);
    }

    [IntegrationTest("Украина: ставка военного сбора берётся на дату начисления, а не на сегодня")]
    public async Task LevyRateFollowsTheAccrualDate()
    {
        var env = await SetupAsync();
        await ConfigureHrAsync();
        var codes = await ConfigureUaAsync();

        var tax = GetService<ITaxService>();

        // До 01.12.2024 военный сбор был 1,5%, после — 5%. Перерасчёт за старый
        // период обязан взять СТАРУЮ ставку: иначе доначислим людям то, чего
        // тогда не существовало.
        var before = await tax.ResolveRateAsync(codes.MilitaryLevy, new DateTime(2024, 11, 30));
        Assert.IsTrue(before == 0.015m, "на 30.11.2024 ВЗ = 1,5%, факт {0}", before?.ToString() ?? "null");

        var after = await tax.ResolveRateAsync(codes.MilitaryLevy, new DateTime(2024, 12, 1));
        Assert.IsTrue(after == Vz, "на 01.12.2024 ВЗ = 5%, факт {0}", after?.ToString() ?? "null");
    }

    [IntegrationTest("Украина: удержание не мешает аннулировать начисление")]
    public async Task WithholdingDoesNotBlockVoid()
    {
        var env = await SetupAsync();
        await ConfigureHrAsync();
        await ConfigureUaAsync();

        var accrual = await AccrueAsync(env.Division, env.Employee, 10000m);
        Assert.IsTrue(await LiabilityAsync(env.Employee) == 7700m, "предпосылка: удержание прошло");

        // Аннулирование смотрит на остаток долга и раньше объясняло недостачу
        // ТОЛЬКО соцвзносом. Появление второго удерживающего делало каждое
        // украинское начисление неаннулируемым — с сообщением про выплату,
        // которой не было.
        var error = await GetService<IPayrollVoidService>().VoidAccrualAsync(accrual);
        Assert.IsTrue(error == null, "аннулирование должно пройти, а вернуло «{0}»", error ?? "");

        var liability = await LiabilityAsync(env.Employee);
        Assert.IsTrue(liability == 0m, "после аннулирования долг обнуляется, факт {0}", liability);
    }

    // ---- обстановка -------------------------------------------------------

    private async Task<(Guid Division, Guid Entity, Guid Employee)> SetupAsync()
    {
        var currency = Dict.NewRecord<Currency>();
        currency.Name = "Hryvnia";
        currency.Code = "UAH";
        currency.Symbol = "₴";
        currency = await Dict.SaveRecordAsync(currency);

        var country = Dict.NewRecord<Country>();
        country.Name = "Ukraine";
        country.CodeISO2 = "UA";
        country.CodeISO3 = "UKR";
        country.PhoneCode = "380";
        country = await Dict.SaveRecordAsync(country);

        var entity = Dict.NewRecord<LegalEntity>();
        entity.Name = "ТОВ Тест";
        entity.RegistrationNumber = $"REG-{Db.NewId():N}"[..12];
        entity.Country = country.MetaId;
        entity.Currency = currency.MetaId;
        entity = await Dict.SaveRecordAsync(entity);

        var divisionType = Dict.NewRecord<DivisionType>();
        divisionType.Code = $"HQ-{Db.NewId():N}"[..12];
        divisionType.Name = "Head office";
        divisionType = await Dict.SaveRecordAsync(divisionType);

        var division = Dict.NewRecord<Division>();
        division.Name = "Київ";
        division.LegalEntity = entity.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await Dict.SaveRecordAsync(division);

        var position = Dict.NewRecord<Position>();
        position.Name = $"Dev-{Db.NewId():N}"[..12];
        position.HourlyRate = 50m;
        position = await Dict.SaveRecordAsync(position);

        var employee = Dict.NewRecord<Employee>();
        employee.Name = "Петренко";
        employee.Division = division.MetaId;
        employee.Position = position.MetaId;
        employee.HireDate = new DateTime(2024, 1, 1);
        employee.IsActive = true;
        employee.Nationality = country.MetaId;
        employee = await Dict.SaveRecordAsync(employee);

        return (division.MetaId, entity.MetaId, employee.MetaId);
    }

    /// <summary>
    /// ЄСВ целиком настройками HR: 22% на работодателе, НОЛЬ на работнике,
    /// максимальная база — 15 минимальных зарплат. Украинского кода тут нет и
    /// не должно быть; если однажды понадобится — значит, контур соцстраха
    /// перестал описывать ЄСВ, и это отдельный разговор.
    /// </summary>
    private async Task ConfigureHrAsync()
    {
        var settings = Dict.NewRecord<HRSettings>();
        settings.PayrollRunDay = 25;
        settings.WorkHoursPerDay = 8m;
        settings.SocialInsuranceEmployeeRate = 0m;
        settings.SocialInsuranceEmployerRate = Esv;
        settings.SocialInsuranceForeignEmployerRate = Esv;
        settings.SocialInsuranceWageCeiling = EsvCeiling;
        await Dict.SaveRecordAsync(settings);
    }

    /// <summary>
    /// Включение украинских удержаний — два кода налога в настройках модели.
    /// Коды поставляются пакетом vat-UA, тест их НЕ создаёт: сид, который никто
    /// не читает, годами лежит нерабочим и никто этого не замечает.
    /// </summary>
    private async Task<(Guid IncomeTax, Guid MilitaryLevy)> ConfigureUaAsync()
    {
        var codes = GetService<IDictionaryManager<TaxCode>>();

        var pdfo = (await codes.GetRecordsAsync("Code = 'UA-PDFO'")).FirstOrDefault();
        Assert.IsTrue(pdfo != null, "код UA-PDFO обязан быть в поставке");

        var vz = (await codes.GetRecordsAsync("Code = 'UA-VZ'")).FirstOrDefault();
        Assert.IsTrue(vz != null, "код UA-VZ обязан быть в поставке");

        var rows = await Dict.GetRecordsAsync<LocalizationUkraineSettings>(null, 1);
        var ua = rows.Count > 0 ? rows[0] : Dict.NewRecord<LocalizationUkraineSettings>();
        ua.IncomeTaxCode = pdfo!.Code;
        ua.MilitaryLevyCode = vz!.Code;
        await Dict.SaveRecordAsync(ua);

        return (pdfo.MetaId, vz.MetaId);
    }

    private async Task<Guid> AccrueAsync(Guid division, Guid employee, decimal amount)
    {
        var doc = await Documents.NewDocumentAsync<PayrollAccrual>();
        doc.Division = division;
        doc.Lines.Add(new PayrollAccrualLinesTablePartRow { Employee = employee, Amount = amount });
        await Documents.SaveDocumentAsync(doc);

        // Документ postOnSave: пока он черновик, не двигается ничего. Проверка
        // до перехода не даёт утверждению пройти вхолостую.
        Assert.IsTrue(await LiabilityAsync(employee) == 0m, "черновик не двигает регистры");

        doc.Subtype = PayrollAccrual.Subtypes.Posted;
        await Documents.SaveDocumentAsync(doc);
        return doc.MetaId;
    }

    // ---- чтение -----------------------------------------------------------

    private static async Task<decimal> LiabilityAsync(Guid employee)
        => await Totals.GetBalanceAsync("PayrollLiability", "Amount",
            new Dictionary<string, object?> { ["Employee"] = employee });

    private static async Task<(decimal Employee, decimal Employer)> FundAsync(Guid employee)
    {
        var row = await Totals.GetBalanceAsync("SocialInsurance",
            new Dictionary<string, object?> { ["Employee"] = employee });
        if (row == null) return (0m, 0m);
        return (Convert.ToDecimal(row["EmployeeContribution"] ?? 0m),
                Convert.ToDecimal(row["EmployerContribution"] ?? 0m));
    }

    private static async Task<(decimal Base, decimal Amount)> LevyAsync(
        Guid entity, Guid employee, Guid taxCode)
    {
        var row = await Totals.GetBalanceAsync("UaPayrollLevy",
            new Dictionary<string, object?>
            {
                ["LegalEntity"] = entity,
                ["Employee"] = employee,
                ["TaxCode"] = taxCode,
            });
        if (row == null) return (0m, 0m);
        return (Convert.ToDecimal(row["Base"] ?? 0m), Convert.ToDecimal(row["Amount"] ?? 0m));
    }
}
