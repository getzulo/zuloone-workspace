using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

public class DeliveryFleetTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static IDeliveryService Delivery => GetService<IDeliveryService>();

    private static readonly DateTime WaveDay = new DateTime(2026, 9, 16);

    private sealed class Setup
    {
        public Guid Location;
        public Guid Store;
        public Guid Item;
        public Guid Unit;
        public Guid Customer;
        public Guid Outlet;
        public Guid OtherOutlet;
        public Guid Contract;
        public Guid OtherContract;
        public Guid Vehicle;
        public Guid OtherVehicle;
        public Guid Driver;
        public Guid OtherDriver;
        public Guid Route;
    }

    private async Task<Setup> SetupAsync()
    {
        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Germany";
        country.CodeISO2 = $"{Guid.NewGuid():N}"[..2].ToUpperInvariant();
        country.CodeISO3 = $"{Guid.NewGuid():N}"[..3].ToUpperInvariant();
        country.PhoneCode = "49";
        country = await DictionaryManager.SaveRecordAsync(country);

        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Euro";
        currency.Code = $"E{Guid.NewGuid():N}"[..2].ToUpperInvariant();
        currency.Symbol = "€";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = "ACME";
        legalEntity.RegistrationNumber = $"REG-DL-{Guid.NewGuid():N}"[..16];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"DL-{Guid.NewGuid():N}"[..10];
        divisionType.Name = "Depot";
        divisionType = await DictionaryManager.SaveRecordAsync(divisionType);

        var division = DictionaryManager.NewRecord<Division>();
        division.Name = "Depot";
        division.LegalEntity = legalEntity.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await DictionaryManager.SaveRecordAsync(division);

        var store = DictionaryManager.NewRecord<Store>();
        store.Name = "Depot WH";
        store.Division = division.MetaId;
        store.IsSimple = true;
        store = await DictionaryManager.SaveRecordAsync(store);

        var zone = DictionaryManager.NewRecord<StoreZone>();
        zone.Name = "Zone";
        zone.Store = store.MetaId;
        zone = await DictionaryManager.SaveRecordAsync(zone);

        var cellType = DictionaryManager.NewRecord<StoreCellType>();
        cellType.Code = $"P-{Guid.NewGuid():N}"[..12];
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
        group.Code = "G";
        group.Name = "Goods";
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
        customer = await DictionaryManager.SaveRecordAsync(customer);

        var outlet = DictionaryManager.NewRecord<CustomerOutlet>();
        outlet.Name = "Shop A";
        outlet.Customer = customer.MetaId;
        outlet = await DictionaryManager.SaveRecordAsync(outlet);

        var otherOutlet = DictionaryManager.NewRecord<CustomerOutlet>();
        otherOutlet.Name = "Shop B";
        otherOutlet.Customer = customer.MetaId;
        otherOutlet = await DictionaryManager.SaveRecordAsync(otherOutlet);

        var vehicle = DictionaryManager.NewRecord<Vehicle>();
        vehicle.Name = "Van-1";
        vehicle.PlateNumber = $"A{Guid.NewGuid():N}"[..8];
        vehicle.Kind = VehicleKind.Van;
        vehicle.CapacityQty = 100m;
        vehicle = await DictionaryManager.SaveRecordAsync(vehicle);

        var otherVehicle = DictionaryManager.NewRecord<Vehicle>();
        otherVehicle.Name = "Van-2";
        otherVehicle.PlateNumber = $"B{Guid.NewGuid():N}"[..8];
        otherVehicle.Kind = VehicleKind.Van;
        otherVehicle = await DictionaryManager.SaveRecordAsync(otherVehicle);

        var driver = DictionaryManager.NewRecord<Driver>();
        driver.Name = "Ivan";
        driver.LicenseNumber = $"L{Guid.NewGuid():N}"[..10];
        driver = await DictionaryManager.SaveRecordAsync(driver);

        var otherDriver = DictionaryManager.NewRecord<Driver>();
        otherDriver.Name = "Petr";
        otherDriver.LicenseNumber = $"M{Guid.NewGuid():N}"[..10];
        otherDriver = await DictionaryManager.SaveRecordAsync(otherDriver);

        var contract = DictionaryManager.NewRecord<SalesContract>();
        contract.Name = "Route-2026";
        contract.Outlet = outlet.MetaId;
        contract.Currency = currency.MetaId;
        contract.SettlementKind = SettlementKind.CashOnDelivery;
        contract.EffectiveFrom = new DateTime(2026, 1, 1);
        contract.LegalEntity = legalEntity.MetaId;
        contract = await DictionaryManager.SaveRecordAsync(contract);

        var otherContract = DictionaryManager.NewRecord<SalesContract>();
        otherContract.Name = "Route-B-2026";
        otherContract.Outlet = otherOutlet.MetaId;
        otherContract.Currency = currency.MetaId;
        otherContract.SettlementKind = SettlementKind.CashOnDelivery;
        otherContract.EffectiveFrom = new DateTime(2026, 1, 1);
        otherContract.LegalEntity = legalEntity.MetaId;
        otherContract = await DictionaryManager.SaveRecordAsync(otherContract);

        var route = DictionaryManager.NewRecord<DeliveryRoute>();
        route.Name = "Morning-1";
        route.Depot = store.MetaId;
        route.DefaultVehicle = vehicle.MetaId;
        route.DefaultDriver = driver.MetaId;
        route = await DictionaryManager.SaveRecordAsync(route);

        var settings = (await GetService<IDictionaryManager<SalesSettings>>().GetRecordsAsync("1 = 1"))
            .FirstOrDefault();
        if (settings is not null)
        {
            settings.AllowBackorder = true;
            await DictionaryManager.SaveRecordAsync(settings);
        }

        var stop = DictionaryManager.NewRecord<DeliveryRouteStop>();
        stop.Route = route.MetaId;
        stop.Sequence = 1;
        stop.Outlet = outlet.MetaId;
        stop.DwellMinutes = 10;
        stop.WindowFromMinutes = 480;
        stop.WindowToMinutes = 540;
        await DictionaryManager.SaveRecordAsync(stop);

        return new Setup
        {
            Location = cell.MetaId,
            Store = store.MetaId,
            Item = item.MetaId,
            Unit = unit.MetaId,
            Customer = customer.MetaId,
            Outlet = outlet.MetaId,
            OtherOutlet = otherOutlet.MetaId,
            Contract = contract.MetaId,
            OtherContract = otherContract.MetaId,
            Vehicle = vehicle.MetaId,
            OtherVehicle = otherVehicle.MetaId,
            Driver = driver.MetaId,
            OtherDriver = otherDriver.MetaId,
            Route = route.MetaId,
        };
    }

    private async Task StockInAsync(Setup s, decimal qty)
    {
        var adjustment = await DocumentManager.NewDocumentAsync<StockAdjustment>();
        adjustment.Cell = s.Location;
        adjustment.Lines.Add(new StockAdjustmentLinesTablePartRow { Item = s.Item, Quantity = qty });
        await DocumentManager.SaveDocumentAsync(adjustment);
        await RunCommandAsync("PostStockAdjustment", adjustment.MetaId);
    }

    private async Task<Guid> ConfirmOrderAsync(Setup s, Guid outlet, DateTime day, decimal qty)
    {
        await StockInAsync(s, qty);
        var order = await DocumentManager.NewDocumentAsync<SalesOrder>();
        order.Customer = s.Customer;
        order.Outlet = outlet;
        order.Contract = outlet == s.OtherOutlet ? s.OtherContract : s.Contract;
        order.Route = s.Route;
        order.Location = s.Location;
        order.DeliveryDate = day;
        order.Lines.Add(new SalesOrderLinesTablePartRow
        {
            Item = s.Item,
            Unit = s.Unit,
            Quantity = qty,
            UnitPrice = 10m,
        });
        await DocumentManager.SaveDocumentAsync(order);
        var saved = await DocumentManager.GetDocumentAsync<SalesOrder>(order.MetaId);
        Assert.IsTrue(saved!.Contract != Guid.Empty,
            "договор должен остаться на заказе после save, факт {0} outlet={1}",
            saved.Contract, saved.Outlet);
        Assert.IsTrue(saved.Lines.Count == 1 && saved.Lines[0].Quantity == qty,
            "количество строки после save: строк {0} qty {1}",
            saved.Lines.Count,
            saved.Lines.Count > 0 ? saved.Lines[0].Quantity : -1m);
        await RunCommandAsync("SubmitSalesOrder", order.MetaId);
        await GetService<IDocumentPostingService>().SetSubtypeAsync(
            Guid.Parse("23643b1b-b959-4206-83ab-948c713276c9"),
            order.MetaId,
            SalesOrder.Subtypes.Confirmed);
        var confirmed = await DocumentManager.GetDocumentAsync<SalesOrder>(order.MetaId);
        Assert.IsTrue(confirmed!.Subtype == SalesOrder.Subtypes.Confirmed,
            "заказ Confirmed, факт {0}", confirmed.Subtype ?? "<null>");
        return order.MetaId;
    }

    [IntegrationTest("Дубль госномера и повтор порядка остановки отклоняются")]
    public async Task MastersRejectDuplicates()
    {
        var s = await SetupAsync();
        var clone = DictionaryManager.NewRecord<Vehicle>();
        clone.Name = "Clone";
        clone.PlateNumber = (await DictionaryManager.GetRecordAsync<Vehicle>(s.Vehicle))!.PlateNumber;
        clone.Kind = VehicleKind.Van;
        try
        {
            await DictionaryManager.SaveRecordAsync(clone);
            Assert.IsTrue(false, "дубль номера должен быть отклонён");
        }
        catch (Exception ex)
        {
            Assert.IsTrue(
                ex.Message.Contains("занят", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("уже", StringComparison.OrdinalIgnoreCase),
                "текст отказа: {0}", ex.Message);
        }

        var clash = DictionaryManager.NewRecord<DeliveryRouteStop>();
        clash.Route = s.Route;
        clash.Sequence = 1;
        clash.Outlet = s.OtherOutlet;
        try
        {
            await DictionaryManager.SaveRecordAsync(clash);
            Assert.IsTrue(false, "дубль порядка должен быть отклонён");
        }
        catch (Exception ex)
        {
            Assert.IsTrue(ex.Message.Contains("порядком"), "текст отказа: {0}", ex.Message);
        }
    }

    [IntegrationTest("Рейс штампует машину и водителя с маршрута")]
    public async Task TripStampsCrewFromRoute()
    {
        var s = await SetupAsync();
        var trip = await DocumentManager.NewDocumentAsync<DeliveryTrip>();
        trip.DeliveryDate = WaveDay;
        trip.Route = s.Route;
        await DocumentManager.SaveDocumentAsync(trip);
        var reloaded = await DocumentManager.GetDocumentAsync<DeliveryTrip>(trip.MetaId);
        Assert.IsTrue(reloaded!.Vehicle == s.Vehicle, "машина с маршрута, факт {0}", reloaded.Vehicle);
        Assert.IsTrue(reloaded.Driver == s.Driver, "водитель с маршрута, факт {0}", reloaded.Driver);
        Assert.IsTrue(reloaded.Depot == s.Store, "склад с маршрута, факт {0}", reloaded.Depot);
    }

    [IntegrationTest("Набор с маршрута кладёт подтверждённый заказ точки")]
    public async Task FillFromRoutePicksConfirmedOrder()
    {
        var s = await SetupAsync();
        var orderId = await ConfirmOrderAsync(s, s.Outlet, WaveDay, 2m);

        var trip = await DocumentManager.NewDocumentAsync<DeliveryTrip>();
        trip.DeliveryDate = WaveDay;
        trip.Route = s.Route;
        await DocumentManager.SaveDocumentAsync(trip);
        var added = await Delivery.FillTripFromRouteAsync(trip.MetaId);
        Assert.IsTrue(added == 1, "набрана одна точка, факт {0}", added);

        var filled = await DocumentManager.GetDocumentAsync<DeliveryTrip>(trip.MetaId);
        Assert.IsTrue(filled!.Lines.Count == 1, "строк {0}", filled.Lines.Count);
        Assert.IsTrue(filled.Lines[0].SalesOrder == orderId, "заказ в строке");
        Assert.IsTrue(filled.Lines[0].Outlet == s.Outlet, "точка штампуется");
        Assert.IsTrue(filled.Lines[0].StopSequence == 1, "порядок с маршрута");
        Assert.IsTrue(filled.Lines[0].DwellMinutes == 10, "стоянка 10, факт {0}", filled.Lines[0].DwellMinutes);
        Assert.IsTrue(filled.Lines[0].PlannedFromMinutes == 480, "окно с 8:00, факт {0}", filled.Lines[0].PlannedFromMinutes);
        Assert.IsTrue(filled.Lines[0].PlannedToMinutes == 540, "окно по 9:00, факт {0}", filled.Lines[0].PlannedToMinutes);
    }

    [IntegrationTest("Чужая точка на рейсе маршрута отклоняется при отправке")]
    public async Task ForeignOutletIsRejected()
    {
        var s = await SetupAsync();
        var orderId = await ConfirmOrderAsync(s, s.OtherOutlet, WaveDay, 1m);

        var trip = await NewTripAsync(s, s.Vehicle, s.Driver, orderId, s.OtherOutlet, 9);
        var reason = await Delivery.ValidateTripAsync(trip.MetaId);
        Assert.IsTrue(reason != null && reason.Contains("остановке"), "отказ про остановку: {0}", reason);
    }

    [IntegrationTest("Машина не едет в двух рейсах в один день")]
    public async Task CrewCannotOverlap()
    {
        var s = await SetupAsync();
        var firstOrder = await ConfirmOrderAsync(s, s.Outlet, WaveDay, 1m);
        var secondOrder = await ConfirmOrderAsync(s, s.Outlet, WaveDay, 1m);

        var first = await NewTripAsync(s, s.Vehicle, s.Driver, firstOrder, s.Outlet, 1);
        await RunCommandAsync("DispatchTrip", first.MetaId);

        var second = await NewTripAsync(s, s.Vehicle, s.OtherDriver, secondOrder, s.Outlet, 1);
        var reason = await Delivery.ValidateTripAsync(second.MetaId);
        Assert.IsTrue(reason != null && reason.Contains("Машина"), "отказ про машину: {0}", reason);
    }

    [IntegrationTest("Волна по расписанию создаёт черновик рейса")]
    public async Task PlanWaveCreatesDraft()
    {
        var s = await SetupAsync();
        var schedule = DictionaryManager.NewRecord<DeliverySchedule>();
        schedule.Name = "Wed morning";
        schedule.Route = s.Route;
        schedule.Weekday = Weekday.Wednesday;
        schedule.DepartHour = 6;
        schedule.DepartMinute = 30;
        schedule.Vehicle = s.OtherVehicle;
        schedule.Driver = s.OtherDriver;
        await DictionaryManager.SaveRecordAsync(schedule);

        Assert.IsTrue(Delivery.WeekdayOf(WaveDay) == 3, "16.09.2026 — среда");
        Assert.IsTrue(await Delivery.FindScheduleAsync(s.Route, WaveDay) != Guid.Empty, "расписание находится");

        var created = await Delivery.PlanWaveAsync(WaveDay);
        Assert.IsTrue(created == 1, "создан один рейс, факт {0}", created);
        var again = await Delivery.PlanWaveAsync(WaveDay);
        Assert.IsTrue(again == 0, "повтор волны не плодит рейс, факт {0}", again);
    }

    [IntegrationTest("Вместимость машины режет отправку")]
    public async Task CapacityIsEnforced()
    {
        var s = await SetupAsync();
        var tiny = DictionaryManager.NewRecord<Vehicle>();
        tiny.Name = "Bike-1";
        tiny.PlateNumber = $"C{Guid.NewGuid():N}"[..8];
        tiny.Kind = VehicleKind.Bike;
        tiny.CapacityQty = 1m;
        tiny = await DictionaryManager.SaveRecordAsync(tiny);

        var orderId = await ConfirmOrderAsync(s, s.Outlet, WaveDay, 3m);
        var trip = await NewTripAsync(s, tiny.MetaId, s.Driver, orderId, s.Outlet, 1);
        var reason = await Delivery.ValidateTripAsync(trip.MetaId);
        Assert.IsTrue(reason != null && reason.Contains("вместимость"), "отказ про вместимость: {0}", reason);
    }

    [IntegrationTest("Два живых расписания на один день недели отклоняются")]
    public async Task ScheduleWeekdayIsUnique()
    {
        var s = await SetupAsync();
        var first = DictionaryManager.NewRecord<DeliverySchedule>();
        first.Name = "Wed-A";
        first.Route = s.Route;
        first.Weekday = Weekday.Wednesday;
        first.DepartHour = 6;
        await DictionaryManager.SaveRecordAsync(first);

        var second = DictionaryManager.NewRecord<DeliverySchedule>();
        second.Name = "Wed-B";
        second.Route = s.Route;
        second.Weekday = Weekday.Wednesday;
        second.DepartHour = 8;
        try
        {
            await DictionaryManager.SaveRecordAsync(second);
            Assert.IsTrue(false, "второе расписание среды должно быть отклонено");
        }
        catch (Exception ex)
        {
            Assert.IsTrue(ex.Message.Contains("день недели") || ex.Message.Contains("расписание"),
                "текст отказа: {0}", ex.Message);
        }
    }

    [IntegrationTest("Завершение рейса закрывает заказ")]
    public async Task CompleteClosesOrder()
    {
        var s = await SetupAsync();
        var orderId = await ConfirmOrderAsync(s, s.Outlet, WaveDay, 1m);

        var trip = await NewTripAsync(s, s.Vehicle, s.Driver, orderId, s.Outlet, 1);
        await RunCommandAsync("DispatchTrip", trip.MetaId);
        await RunCommandAsync("CompleteTrip", trip.MetaId);

        var closed = await DocumentManager.GetDocumentAsync<SalesOrder>(orderId);
        Assert.IsTrue(closed!.Subtype == SalesOrder.Subtypes.Delivered,
            "заказ Delivered, факт {0}", closed.Subtype ?? "<null>");
        var done = await DocumentManager.GetDocumentAsync<DeliveryTrip>(trip.MetaId);
        Assert.IsTrue(done!.Subtype == DeliveryTrip.Subtypes.Completed,
            "рейс Completed, факт {0}", done.Subtype ?? "<null>");
        Assert.IsTrue(done.ActualDepart.Year >= 2026, "факт выезда, {0}", done.ActualDepart);
        Assert.IsTrue(done.ActualComplete.Year >= 2026, "факт завершения, {0}", done.ActualComplete);
        Assert.IsTrue(done.ActualComplete >= done.ActualDepart, "завершение не раньше выезда");
    }

    [IntegrationTest("Отправка штампует план выезда из расписания")]
    public async Task DispatchStampsPlanFromSchedule()
    {
        var s = await SetupAsync();
        var schedule = DictionaryManager.NewRecord<DeliverySchedule>();
        schedule.Name = "Wed 6:30";
        schedule.Route = s.Route;
        schedule.Weekday = Weekday.Wednesday;
        schedule.DepartHour = 6;
        schedule.DepartMinute = 30;
        await DictionaryManager.SaveRecordAsync(schedule);

        var orderId = await ConfirmOrderAsync(s, s.Outlet, WaveDay, 1m);
        var trip = await NewTripAsync(s, s.Vehicle, s.Driver, orderId, s.Outlet, 1);
        await RunCommandAsync("DispatchTrip", trip.MetaId);

        var dispatched = await DocumentManager.GetDocumentAsync<DeliveryTrip>(trip.MetaId);
        Assert.IsTrue(dispatched!.PlannedDepart.Year == 2026
            && dispatched.PlannedDepart.Month == 9
            && dispatched.PlannedDepart.Day == 16
            && dispatched.PlannedDepart.Hour == 6
            && dispatched.PlannedDepart.Minute == 30,
            "план 16.09.2026 06:30, факт {0}", dispatched.PlannedDepart);
        Assert.IsTrue(dispatched.ActualDepart.Year >= 2026, "факт выезда, {0}", dispatched.ActualDepart);
    }

    // ── факт по каждой точке ────────────────────────────────────────────────
    //
    // Окно у точки было всегда (PlannedFromMinutes/ToMinutes), а факта не было:
    // попали в него или нет — сказать было нечем. Трип-уровневые ActualDepart /
    // ActualComplete отвечают только «когда выехали и когда всё закончилось».

    [IntegrationTest("Прибытие позже окна: рейс завершается, но называет опоздание")]
    public async Task LateArrivalIsNamedOnComplete()
    {
        var s = await SetupAsync();
        var orderId = await ConfirmOrderAsync(s, s.Outlet, WaveDay, 1m);
        var trip = await NewTripAsync(s, s.Vehicle, s.Driver, orderId, s.Outlet, 1);
        await RunCommandAsync("DispatchTrip", trip.MetaId);

        // Окно 09:00–10:00, приехали в 11:30 — опоздание на 90 минут.
        await StampStopAsync(trip.MetaId, from: 540, to: 600, arriveAt: 11 * 60 + 30, departAfter: 15);

        var messages = await RunCommandForMessagesAsync("CompleteTrip", trip.MetaId);
        Assert.IsTrue(messages.Contains("опоздание 90 мин"),
            "завершение обязано назвать опоздание в минутах. Факт: {0}", messages);
        Assert.IsTrue(messages.Contains("точка 1"),
            "и НОМЕР точки — иначе её не найти. Факт: {0}", messages);

        // Опоздание — факт, а не ошибка ввода: рейс всё равно завершён.
        var done = await DocumentManager.GetDocumentAsync<DeliveryTrip>(trip.MetaId);
        Assert.IsTrue(done!.Subtype == DeliveryTrip.Subtypes.Completed,
            "рейс всё равно Completed, факт {0}", done.Subtype ?? "<null>");
    }

    [IntegrationTest("Прибытие внутри окна: завершение молчит про опоздания")]
    public async Task OnTimeArrivalIsSilent()
    {
        var s = await SetupAsync();
        var orderId = await ConfirmOrderAsync(s, s.Outlet, WaveDay, 1m);
        var trip = await NewTripAsync(s, s.Vehicle, s.Driver, orderId, s.Outlet, 1);
        await RunCommandAsync("DispatchTrip", trip.MetaId);

        // То же окно 09:00–10:00, приехали в 09:30.
        await StampStopAsync(trip.MetaId, from: 540, to: 600, arriveAt: 9 * 60 + 30, departAfter: 10);

        var messages = await RunCommandForMessagesAsync("CompleteTrip", trip.MetaId);
        Assert.IsTrue(!messages.Contains("Вне окна"),
            "в окне — про окно ни слова. Факт: {0}", messages);
        Assert.IsTrue(messages.Contains("завершён"),
            "рейс завершён. Факт: {0}", messages);
    }

    [IntegrationTest("Убытие раньше прибытия отклоняет завершение с номером точки")]
    public async Task DepartureBeforeArrivalIsRejected()
    {
        var s = await SetupAsync();
        var orderId = await ConfirmOrderAsync(s, s.Outlet, WaveDay, 1m);
        var trip = await NewTripAsync(s, s.Vehicle, s.Driver, orderId, s.Outlet, 4);
        await RunCommandAsync("DispatchTrip", trip.MetaId);

        // departAfter отрицательный: убыли за 20 минут ДО прибытия.
        await StampStopAsync(trip.MetaId, from: null, to: null, arriveAt: 10 * 60, departAfter: -20);

        var reason = await Delivery.ValidateTripAsync(trip.MetaId);
        Assert.IsTrue(reason != null && reason.Contains("раньше прибытия"),
            "отказ про порядок времени. Факт: {0}", reason ?? "<null>");
        Assert.IsTrue(reason!.Contains("Точка 4"),
            "и номер точки. Факт: {0}", reason);
    }

    [IntegrationTest("Точка с окном, но без отметки, вердикта не даёт (пустое ≠ полночь)")]
    public async Task TripWithoutStopFactCompletesAsBefore()
    {
        var s = await SetupAsync();
        var orderId = await ConfirmOrderAsync(s, s.Outlet, WaveDay, 1m);
        var trip = await NewTripAsync(s, s.Vehicle, s.Driver, orderId, s.Outlet, 1);
        await RunCommandAsync("DispatchTrip", trip.MetaId);

        // Окно есть, факта нет — так ведут рейсы сегодня, и это обязано работать.
        // Кейс ловит РОВНО ту ловушку, из-за которой пришлось смотреть на тип:
        // будь ArrivedAt не-nullable, пустое значение было бы 0001-01-01, то есть
        // «полночь», и точка отрапортовала бы «раньше окна на 540 мин».
        var full = await DocumentManager.GetDocumentAsync<DeliveryTrip>(trip.MetaId);
        full!.Lines[0].PlannedFromMinutes = 540;
        full.Lines[0].PlannedToMinutes = 600;
        await DocumentManager.SaveDocumentAsync(full);

        var messages = await RunCommandForMessagesAsync("CompleteTrip", trip.MetaId);
        Assert.IsTrue(!messages.Contains("окн"),
            "пустой факт не даёт вердикта ни в какую сторону. Факт: {0}", messages);

        var done = await DocumentManager.GetDocumentAsync<DeliveryTrip>(trip.MetaId);
        Assert.IsTrue(done!.Subtype == DeliveryTrip.Subtypes.Completed,
            "рейс Completed, факт {0}", done.Subtype ?? "<null>");
    }

    [IntegrationTest("Километр на север от 50.4501, 30.5234 — около 1001 м")]
    public async Task OneKilometreNorthIsAbout1001Meters()
    {
        var meters = Delivery.PinDistanceMeters(50.450100m, 30.523400m, 50.459100m, 30.523400m);
        Assert.IsTrue(meters >= 990 && meters <= 1010, "около 1001 м, факт {0}", meters);
        await Task.CompletedTask;
    }

    [IntegrationTest("Набор с маршрута копирует координаты остановки")]
    public async Task FillCopiesTheStopPin()
    {
        var s = await SetupAsync();
        var stops = await GetService<IDictionaryManager<DeliveryRouteStop>>()
            .GetRecordsAsync($"Route = '{s.Route}'");
        var stop = stops.Single(x => x.Sequence == 1);
        stop.Lat = 50.450100m;
        stop.Lng = 30.523400m;
        await DictionaryManager.SaveRecordAsync(stop);

        await ConfirmOrderAsync(s, s.Outlet, WaveDay, 1m);
        var trip = await DocumentManager.NewDocumentAsync<DeliveryTrip>();
        trip.DeliveryDate = WaveDay;
        trip.Route = s.Route;
        await DocumentManager.SaveDocumentAsync(trip);
        await Delivery.FillTripFromRouteAsync(trip.MetaId);

        var filled = await DocumentManager.GetDocumentAsync<DeliveryTrip>(trip.MetaId);
        Assert.IsTrue(filled!.Lines[0].PlannedLat == 50.450100m, "широта плана, факт {0}", filled.Lines[0].PlannedLat);
        Assert.IsTrue(filled.Lines[0].PlannedLng == 30.523400m, "долгота плана, факт {0}", filled.Lines[0].PlannedLng);
    }

    [IntegrationTest("Факт дальше допуска: рейс завершается и называет метры")]
    public async Task FarPinIsNamedOnComplete()
    {
        var s = await SetupAsync();
        var route = await GetService<IDictionaryManager<DeliveryRoute>>().GetRecordAsync(s.Route);
        route!.ArriveRadiusMeters = 300;
        await DictionaryManager.SaveRecordAsync(route);

        var orderId = await ConfirmOrderAsync(s, s.Outlet, WaveDay, 1m);
        var trip = await NewTripAsync(s, s.Vehicle, s.Driver, orderId, s.Outlet, 1);
        await RunCommandAsync("DispatchTrip", trip.MetaId);

        var full = await DocumentManager.GetDocumentAsync<DeliveryTrip>(trip.MetaId);
        full!.Lines[0].PlannedLat = 50.450100m;
        full.Lines[0].PlannedLng = 30.523400m;
        full.Lines[0].ActualLat = 50.459100m;
        full.Lines[0].ActualLng = 30.523400m;
        await DocumentManager.SaveDocumentAsync(full);

        var reloaded = await DocumentManager.GetDocumentAsync<DeliveryTrip>(trip.MetaId);
        var line = reloaded!.Lines[0];
        Assert.IsTrue(line.PlannedLat == 50.450100m, "широта плана после записи, факт {0}", line.PlannedLat);
        Assert.IsTrue(line.ActualLat == 50.459100m, "широта факта после записи, факт {0}", line.ActualLat);
        var radius = (await GetService<IDictionaryManager<DeliveryRoute>>().GetRecordAsync(s.Route))!.ArriveRadiusMeters;
        Assert.IsTrue(radius == 300, "допуск маршрута, факт {0}", radius);
        var summary = await Delivery.PinOffSummaryAsync(trip.MetaId);
        Assert.IsTrue(summary.Contains("от плана"), "сводка до команды: {0}", summary);

        var messages = await RunCommandForMessagesAsync("CompleteTrip", trip.MetaId);
        Assert.IsTrue(messages.Contains("м от плана"), "завершение называет расстояние. Факт: {0}", messages);
        Assert.IsTrue(messages.Contains("точка 1"), "и номер точки. Факт: {0}", messages);
        var done = await DocumentManager.GetDocumentAsync<DeliveryTrip>(trip.MetaId);
        Assert.IsTrue(done!.Subtype == DeliveryTrip.Subtypes.Completed,
            "рейс всё равно Completed, факт {0}", done.Subtype ?? "<null>");
    }

    [IntegrationTest("Допуск 0 и пустой факт координату не обсуждают")]
    public async Task ZeroRadiusAndMissingPinStaySilent()
    {
        var s = await SetupAsync();
        var orderId = await ConfirmOrderAsync(s, s.Outlet, WaveDay, 1m);
        var trip = await NewTripAsync(s, s.Vehicle, s.Driver, orderId, s.Outlet, 1);
        await RunCommandAsync("DispatchTrip", trip.MetaId);

        var full = await DocumentManager.GetDocumentAsync<DeliveryTrip>(trip.MetaId);
        full!.Lines[0].PlannedLat = 50.450100m;
        full.Lines[0].PlannedLng = 30.523400m;
        await DocumentManager.SaveDocumentAsync(full);

        var messages = await RunCommandForMessagesAsync("CompleteTrip", trip.MetaId);
        Assert.IsTrue(!messages.Contains("от плана"),
            "без факта и без допуска — тишина. Факт: {0}", messages);
    }

    [IntegrationTest("Широта 95 на остановке не сохраняется")]
    public async Task LatitudeOutOfRangeIsRejected()
    {
        var s = await SetupAsync();
        var stops = await GetService<IDictionaryManager<DeliveryRouteStop>>()
            .GetRecordsAsync($"Route = '{s.Route}'");
        var stop = stops.Single(x => x.Sequence == 1);
        stop.Lat = 95m;
        stop.Lng = 30.523400m;
        try
        {
            await DictionaryManager.SaveRecordAsync(stop);
            Assert.IsTrue(false, "широта 95 должна быть отказана");
        }
        catch (Exception ex)
        {
            Assert.IsTrue(ex.Message.Contains("Широта"), "текст отказа: {0}", ex.Message);
        }
    }

    /// <summary>Окно и факт на единственной точке рейса. Минуты — от полуночи дня доставки.</summary>
    private static async Task StampStopAsync(
        Guid tripId, int? from, int? to, int arriveAt, int departAfter)
    {
        var trip = await DocumentManager.GetDocumentAsync<DeliveryTrip>(tripId);
        var line = trip!.Lines[0];
        line.PlannedFromMinutes = from;
        line.PlannedToMinutes = to;
        line.ArrivedAt = WaveDay.Date.AddMinutes(arriveAt);
        line.DepartedAt = WaveDay.Date.AddMinutes(arriveAt + departAfter);
        await DocumentManager.SaveDocumentAsync(trip);
    }

    /// <summary>Как RunCommandAsync, но отдаёт текст: вердикт по окну живёт именно в нём.</summary>
    private async Task<string> RunCommandForMessagesAsync(string name, Guid documentId)
    {
        var commandId = await Db.FindCommandIdAsync("document", name);
        var run = await Db.ExecuteDocumentCommandAsync(commandId, documentId);
        return (run.Message ?? "") + " " + string.Join("; ", run.ClientMessages);
    }

    private async Task<DeliveryTrip> NewTripAsync(Setup s, Guid vehicle, Guid driver, Guid orderId, Guid outlet, int seq)
    {
        var trip = await DocumentManager.NewDocumentAsync<DeliveryTrip>();
        trip.DeliveryDate = WaveDay;
        trip.Route = s.Route;
        trip.Vehicle = vehicle;
        trip.Driver = driver;
        await DocumentManager.SaveDocumentAsync(trip);
        var saved = await DocumentManager.GetDocumentAsync<DeliveryTrip>(trip.MetaId)
            ?? throw new InvalidOperationException("рейс не читается после сохранения");
        saved.Lines.Add(new DeliveryTripLinesTablePartRow
        {
            SalesOrder = orderId,
            Outlet = outlet,
            StopSequence = seq,
        });
        await DocumentManager.SaveDocumentAsync(saved);
        return (await DocumentManager.GetDocumentAsync<DeliveryTrip>(trip.MetaId))!;
    }

    private async Task RunCommandAsync(string name, Guid documentId)
    {
        var commandId = await Db.FindCommandIdAsync("document", name);
        var run = await Db.ExecuteDocumentCommandAsync(commandId, documentId);
        Assert.IsTrue(run.Success, "команда {0}: {1}", name, run.Message ?? string.Join("; ", run.ClientMessages));
    }
}
