using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Соцстрах: взносы с фонда оплаты труда. Ставки и потолок базы — НАСТРОЙКИ
// (HRSettings), а не константы в коде: у каждой страны свои, и они меняются
// законом чаще, чем код. Для КСА (GOSI) типично 9.75% с работника и 11.75% с
// работодателя за граждан, 2% с работодателя за иностранцев, потолок базы
// 45 000 SAR — но это данные стенда, а не знание сервиса.
//
// Гражданство сотрудника сравнивается со страной регистрации работодателя
// (HRSettings.HomeCountry): «свой» — полная ставка, «иностранец» — только
// ставка работодателя за иностранцев (у работника взноса нет).
public partial class SocialInsuranceService
{
    private readonly IDictionaryManager<HRSettings> _settings;
    private readonly IDictionaryManager<Employee> _employees;
    private readonly IDocumentManager _documents;
    private readonly IDocumentPostingService _posting;

    public SocialInsuranceService(
        IDictionaryManager<HRSettings> settings,
        IDictionaryManager<Employee> employees,
        IDocumentManager documents,
        IDocumentPostingService posting)
    {
        _settings = settings;
        _employees = employees;
        _documents = documents;
        _posting = posting;
    }

    /// <summary>Тип документа SocialInsuranceAccrual — цель перевода в Posted.</summary>
    private static readonly Guid SocialInsuranceAccrualType = Guid.Parse("a0d03063-af77-4fd0-886b-223a9731f105");

    /// <summary>Взносы с одной базы: (работник, работодатель). Оба нуля — контур не настроен.</summary>
    public async Task<(decimal Employee, decimal Employer)> CalculateAsync(Guid employee, decimal grossAmount)
    {
        if (grossAmount <= 0m) return (0m, 0m);

        var s = (await _settings.GetRecordsAsync("1 = 1")).FirstOrDefault();
        if (s is null) return (0m, 0m);

        // Потолок базы: взнос считается с меньшего из зарплаты и потолка.
        // Ноль/отсутствие потолка = потолка нет, а не «база ноль».
        var ceiling = s.SocialInsuranceWageCeiling;
        var contributoryBase = ceiling > 0m && grossAmount > ceiling ? ceiling : grossAmount;

        var isLocal = await IsLocalNationalAsync(employee, s);
        var scale = GlobalConstants.Get<int?>("AmountScale") ?? 2;

        if (!isLocal)
        {
            var foreignRate = s.SocialInsuranceForeignEmployerRate;
            return (0m, Math.Round(contributoryBase * foreignRate, scale, MidpointRounding.AwayFromZero));
        }

        return (
            Math.Round(contributoryBase * s.SocialInsuranceEmployeeRate, scale, MidpointRounding.AwayFromZero),
            Math.Round(contributoryBase * s.SocialInsuranceEmployerRate, scale, MidpointRounding.AwayFromZero));
    }

    /// <summary>База взноса после применения потолка — для отображения в строке начисления.</summary>
    public async Task<decimal> ContributoryBaseAsync(decimal grossAmount)
    {
        var s = (await _settings.GetRecordsAsync("1 = 1")).FirstOrDefault();
        var ceiling = s?.SocialInsuranceWageCeiling ?? 0m;
        return ceiling > 0m && grossAmount > ceiling ? ceiling : grossAmount;
    }

    /// <summary>
    /// Порождает ПРОВЕДЁННОЕ начисление взносов по парам «сотрудник → начислено».
    /// null, если контур не настроен или взносы вышли нулевыми: соцстрах —
    /// необязательный контур, без него начисление ФОТ проводится как раньше.
    /// </summary>
    public async Task<Guid?> CreateAccrualAsync(Guid division, IEnumerable<KeyValuePair<Guid, decimal>> gross)
    {
        var s = (await _settings.GetRecordsAsync("1 = 1")).FirstOrDefault();
        if (s is null) return null;

        var rows = new List<SocialInsuranceAccrualLinesTablePartRow>();
        foreach (var kv in gross)
        {
            var (employee, employer) = await CalculateAsync(kv.Key, kv.Value);
            if (employee == 0m && employer == 0m) continue;
            rows.Add(new SocialInsuranceAccrualLinesTablePartRow
            {
                Employee = kv.Key,
                ContributoryBase = await ContributoryBaseAsync(kv.Value),
                EmployeeContribution = employee,
                EmployerContribution = employer,
            });
        }
        if (rows.Count == 0) return null;

        var doc = await _documents.NewDocumentAsync<SocialInsuranceAccrual>("Draft",
            new Dictionary<string, object?> { ["Division"] = division });
        foreach (var r in rows) doc.Lines.Add(r);

        // Сохраняем ЧЕРНОВИКОМ и только потом переводим: подтип Posted заперт
        // (isReadOnly), и строки, записанные уже в нём, гард отклонит.
        await _documents.SaveDocumentAsync(doc);
        await _posting.SetSubtypeAsync(SocialInsuranceAccrualType, doc.MetaId, "Posted");
        return doc.MetaId;
    }

    /// <summary>Гражданин страны регистрации работодателя? Без данных считаем «своим».</summary>
    private async Task<bool> IsLocalNationalAsync(Guid employee, HRSettings settings)
    {
        var home = settings.HomeCountry;
        if (home == Guid.Empty) return true;

        var e = await _employees.GetRecordAsync(employee);
        var nationality = e?.Nationality ?? Guid.Empty;
        // Гражданство не заполнено — не повод лишать сотрудника взносов: считаем
        // своим, а не иностранцем. Иначе пустое поле молча урезало бы отчисления.
        return nationality == Guid.Empty || nationality == home;
    }
}
