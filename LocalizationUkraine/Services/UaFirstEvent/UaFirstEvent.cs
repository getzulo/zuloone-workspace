#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Service "UaFirstEvent": перша подія украинского ПДВ (ПКУ 187.1) и база
// единого налога.
//
// ПРАВИЛО ПДВ. Обязательство возникает на дату того из двух событий, что
// случилось РАНЬШЕ: отгрузка или получение денег. В отличие от Саудовской
// Аравии, где налог привязан к счёту, здесь предоплата сама по себе рождает
// обязательство, а последующая отгрузка в её пределах — уже нет.
//
// КАК ЭТО СЧИТАЕТСЯ, И ПОЧЕМУ НЕ СОПОСТАВЛЕНИЕМ. Соблазн — связать конкретную
// оплату с конкретной отгрузкой и смотреть, что из них первое. Этого нельзя
// сделать честно: строка CustomerPayment несёт клиента, договор и сумму, но НЕ
// ссылку на заказ или счёт. Сопоставление пришлось бы выдумать, и оно разъехалось
// бы на первой же частичной оплате.
//
// Вместо этого по договору копятся два итога — сколько отгружено и сколько
// оплачено, — и облагается max из них: больший и есть «события, которые уже
// произошли». Accrued помнит, какая часть базы уже обложена, так что каждое
// событие берёт налог ТОЛЬКО с прироста. Отсюда само собой выходит верное
// поведение во всех четырёх случаях: предоплата облагается сразу; отгрузка после
// неё в её пределах не облагается второй раз; отгрузка без оплаты облагается
// сразу; оплата после отгрузки не облагается повторно.
//
// ЕДИНЫЙ НАЛОГ ЖИВЁТ РЯДОМ, НО СЧИТАЕТСЯ ИНАЧЕ: он кассовый, его база — только
// ДЕНЬГИ, безо всякого max. Поэтому у него свой счётчик обложенного, EpAccrued, и
// своя цель. Складывать их в один Accrued нельзя: у спрощенця 3% базы двух
// налогов расходятся — ЄП берётся с дохода БЕЗ ПДВ, а ПДВ с первого события.
//
// КЛЮЧ — ЮРЛИЦО, КЛИЕНТ И ДОГОВОР, БЕЗ ТОЧКИ. Договор принадлежит ровно одной
// торговой точке, поэтому точка в ключе избыточна. Практическая сторона той же
// медали: строка оплаты точку не несёт, а транзакционный скрипт синхронен и
// достать её из договора не может. В UaVatPayable точка остаётся — её подставляет
// драйвер, который асинхронен. Юрлицо, наоборот, в ключе НУЖНО: режим
// налогообложения — его свойство, а в одной системе живут ТОВ и несколько ФОП.
//
// ГДЕ ЭТО ЗОВУТ. Из драйвера итогов UaVatFirstEvent, уже ПОСЛЕ записи движений:
// прирост зависит от состояния, состояние читается асинхронно, а
// GetTransactions синхронна. Путь «посчитать в событии и проштамповать полем»
// закрыт — поля расширения не попадают в генерируемый класс документа.
public partial class UaFirstEvent
{
    private readonly ITotalsManager _totals;
    private readonly IDictionaryManager<SalesContract> _contracts;
    private readonly IDictionaryManager<TaxCode> _codes;
    private readonly IDictionaryManager<LocalizationUkraineSettings> _settings;
    private readonly IDataService _data;

    public UaFirstEvent(
        ITotalsManager totals,
        IDictionaryManager<SalesContract> contracts,
        IDictionaryManager<TaxCode> codes,
        IDictionaryManager<LocalizationUkraineSettings> settings,
        IDataService data)
    {
        _totals = totals;
        _contracts = contracts;
        _codes = codes;
        _settings = settings;
        _data = data;
    }

    /// <summary>
    /// Торговая точка договора. Нужна не здесь, а на выходе — в UaVatPayable,
    /// где срез по точкам одного клиента обязан не смешиваться.
    /// </summary>
    public async Task<Guid> OutletOfAsync(Guid contract)
    {
        if (contract == Guid.Empty) return Guid.Empty;
        var record = await _contracts.GetRecordAsync(contract);
        return record?.Outlet ?? Guid.Empty;
    }

    /// <summary>
    /// Юрлицо договора. Нужно ОДНОМУ месту — проверке шапки оплаты: оператор
    /// выбирает юрлицо руками, и разойдись оно с договором, Shipped и Paid легли
    /// бы в разные координаты, а налог начислился бы дважды.
    /// Guid.Empty означает «у договора юрлицо не заполнено» — тогда сверять не с
    /// чем и проверка молчит.
    /// </summary>
    public async Task<Guid> LegalEntityOfAsync(Guid contract)
    {
        if (contract == Guid.Empty) return Guid.Empty;
        var record = await _contracts.GetRecordAsync(contract);
        return record?.LegalEntity ?? Guid.Empty;
    }

    /// <summary>
    /// Режим налогообложения юрлица.
    ///
    /// ЧИТАЕТСЯ СЫРЫМ МЕШКОМ, А НЕ ТИПИЗИРОВАННОЙ ЗАПИСЬЮ, и это вынужденно:
    /// UaTaxRegime — поле РАСШИРЕНИЯ чужого справочника, оно живёт в отдельном
    /// агрегате LegalEntity_LocalizationUkraine и в генерируемый класс
    /// LegalEntity не попадает. Тем же способом Саудовская Аравия достаёт свой
    /// CommercialRegistration.
    ///
    /// ПУСТО ИЛИ НЕ ПРОЧИТАЛОСЬ — ПЛАТЕЛЬЩИК ПДВ. Значение 0 перечисления
    /// выбрано платильщиком ПДВ намеренно: поле необязательное и ненулевое, у
    /// всех существующих юрлиц оно приедет нулём, и нуль обязан означать ровно то
    /// поведение, что было до появления режимов. Иначе обновление молча
    /// перестало бы начислять ПДВ всем.
    /// </summary>
    public async Task<UaTaxRegime> RegimeOfAsync(Guid legalEntity)
    {
        if (legalEntity == Guid.Empty) return UaTaxRegime.VatPayer;

        object? raw = null;
        try
        {
            var bag = await _data.GetByIdAsync("LegalEntity", legalEntity);
            raw = bag?["UaTaxRegime"];
        }
        catch
        {
            // Мешок расширения может не существовать вовсе — модель есть, а
            // строки расширения у этого юрлица ещё нет. Это не ошибка.
            return UaTaxRegime.VatPayer;
        }

        if (raw is null) return UaTaxRegime.VatPayer;
        if (raw is UaTaxRegime typed) return typed;

        // Перечисление переезжает границу данных то числом, то именем — зависит
        // от того, пришла запись через типизированный менеджер или через /api/data.
        if (raw is string name)
            return Enum.TryParse<UaTaxRegime>(name, ignoreCase: true, out var parsed)
                ? parsed
                : UaTaxRegime.VatPayer;

        try
        {
            var number = Convert.ToInt32(raw);
            return Enum.IsDefined(typeof(UaTaxRegime), number)
                ? (UaTaxRegime)number
                : UaTaxRegime.VatPayer;
        }
        catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException)
        {
            return UaTaxRegime.VatPayer;
        }
    }

    /// <summary>Режим начисляет ПДВ по первому событию?</summary>
    public bool ChargesVat(UaTaxRegime regime)
        => regime is UaTaxRegime.VatPayer or UaTaxRegime.SimplifiedWithVat;

    /// <summary>
    /// Код единого налога для режима — уже разрешённый в запись справочника.
    /// null означает одно из трёх и все три лечатся одинаково (молчанием):
    /// режим единый налог не платит; код в настройках не заполнен; код заполнен,
    /// но такого TaxCode на стенде нет.
    ///
    /// КОД БЕРЁТСЯ ИЗ НАСТРОЕК, А НЕ ЗАШИТ СТРОКОЙ. Зашей «UA-EP3» в скрипт — и
    /// правило заработает ровно на том стенде, где пакет данных ставился как
    /// есть; любой контур, заведённый руками или тестом, молча не найдётся, а
    /// выглядеть это будет как «единый налог просто не начисляется». Ровно так
    /// однажды уже простоял зелёным поставочный seed, который не мог разрешиться.
    /// </summary>
    public async Task<Guid?> SingleTaxCodeIdAsync(UaTaxRegime regime)
    {
        var settings = (await _settings.GetRecordsAsync("1 = 1")).FirstOrDefault();

        var code = regime switch
        {
            UaTaxRegime.SimplifiedWithVat => settings?.SingleTaxCode3,
            UaTaxRegime.SimplifiedNoVat => settings?.SingleTaxCode5,
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(code)) return null;

        return (await _codes.GetRecordsAsync($"Code = '{code}'")).FirstOrDefault()?.MetaId;
    }

    /// <summary>
    /// Какая часть базы договора облагается ПДВ ПРЯМО СЕЙЧАС: прирост
    /// max(Shipped, Paid) над уже обложенным. Зовётся ПОСЛЕ записи движений, так
    /// что остатки уже включают текущий документ.
    ///
    /// ВНИМАНИЕ НА АСИММЕТРИЮ. Shipped — база БЕЗ налога (столько стоит товар),
    /// Paid — деньги С налогом (столько пришло на счёт). Сравнивать их напрямую
    /// нельзя: предоплата 120 при ставке 20% закрывает базу 100, а не 120.
    /// Приведение делается здесь, а не в проводке, потому что для него нужна
    /// ставка, а ставка резолвится асинхронно.
    ///
    /// Ноль — законный и частый ответ: отгрузка, целиком закрытая предоплатой,
    /// не двигает max и не добавляет ничего. МИНУС тоже законный: кредит-нота
    /// уменьшает Shipped, цель опускается ниже обложенного, и налог должен
    /// освободиться.
    /// </summary>
    public async Task<decimal> TaxableIncrementAsync(
        Guid legalEntity, Guid customer, Guid contract, decimal rate)
    {
        if (contract == Guid.Empty) return 0m;

        var shipped = await BalanceAsync(legalEntity, customer, contract, "Shipped");
        var paidGross = await BalanceAsync(legalEntity, customer, contract, "Paid");
        var accrued = await BalanceAsync(legalEntity, customer, contract, "Accrued");

        // ЗНАКОВАЯ величина, и это существенно. Вверх её двигают отгрузка и
        // оплата, вниз — кредит-нота, уменьшающая Shipped. Зажми минус в ноль,
        // как было сначала, и налог по кредит-ноте не освободится, а планка
        // max(Shipped, Paid) останется завышенной: следующая отгрузка по
        // договору не начислит ничего, пока её не перекроет.
        return Math.Max(shipped, Net(paidGross, rate)) - accrued;
    }

    /// <summary>
    /// База единого налога, ещё не обложенная: прирост ПОЛУЧЕННЫХ ДЕНЕГ над
    /// EpAccrued. Ни отгрузка, ни кредит-нота сюда не попадают — ЄП кассовый,
    /// его рождает только приход денег.
    ///
    /// vatRate ≠ 0 ТОЛЬКО У СПРОЩЕНЦЯ 3%: он плательщик ПДВ, и единый налог
    /// берётся с дохода БЕЗ ПДВ — 120 полученных при ставке 20% дают базу ЄП 100.
    /// У пятипроцентника ПДВ в деньгах нет вовсе, база — вся полученная сумма,
    /// и vatRate сюда приходит нулём.
    /// </summary>
    public async Task<decimal> SingleTaxIncrementAsync(
        Guid legalEntity, Guid customer, Guid contract, decimal vatRate)
    {
        if (contract == Guid.Empty) return 0m;

        var paidGross = await BalanceAsync(legalEntity, customer, contract, "Paid");
        var taxed = await BalanceAsync(legalEntity, customer, contract, "EpAccrued");

        return Net(paidGross, vatRate) - taxed;
    }

    /// <summary>Деньги с налогом → база без него. Ставка 0 — возвращает как есть.</summary>
    private static decimal Net(decimal gross, decimal rate)
    {
        if (rate <= 0m) return gross;
        var scale = GlobalConstants.Get<int?>("AmountScale") ?? 2;
        return Math.Round(gross / (1m + rate), scale, MidpointRounding.AwayFromZero);
    }

    private Task<decimal> BalanceAsync(
        Guid legalEntity, Guid customer, Guid contract, string resource)
        => _totals.GetBalanceAsync("UaVatFirstEvent", resource,
            new Dictionary<string, object?>
            {
                ["LegalEntity"] = legalEntity,
                ["Customer"] = customer,
                ["SalesContract"] = contract,
            });
}
