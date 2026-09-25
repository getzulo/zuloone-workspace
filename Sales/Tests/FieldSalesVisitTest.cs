using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;

public class FieldSalesVisitTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();

    private static readonly DateTime VisitDay = new DateTime(2026, 9, 17);

    private async Task<Guid> NewAgentAsync(string name)
    {
        var agent = DictionaryManager.NewRecord<SalesPerson>();
        agent.Name = name;
        agent.UserId = Guid.NewGuid();
        agent = await DictionaryManager.SaveRecordAsync(agent);
        return agent.MetaId;
    }

    private async Task<Guid> NewCustomerAsync(string name)
    {
        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = name;
        customer.CustomerType = "B2B";
        customer = await DictionaryManager.SaveRecordAsync(customer);
        return customer.MetaId;
    }

    [IntegrationTest("Чек-ин без GPS сохраняется со статусом NoGeo на открытом маршруте")]
    public async Task CheckInWithoutGpsKeepsNoGeo()
    {
        var agent = await NewAgentAsync("Ivan");
        var customer = await NewCustomerAsync("Kiosk on the corner");

        var route = await DocumentManager.NewDocumentAsync<VisitRoute>();
        route.Agent = agent;
        route.RouteDate = VisitDay;
        route.DocumentDate = VisitDay;
        route.Stops.Add(new VisitRouteStopsTablePartRow
        {
            Customer = customer,
            Sequence = 1,
            StopStatus = "Planned",
        });
        await DocumentManager.SaveDocumentAsync(route);

        var draft = await DocumentManager.GetDocumentAsync<VisitRoute>(route.MetaId);
        Assert.IsTrue(draft!.Subtype == VisitRoute.Subtypes.Draft,
            "маршрут рождается черновиком, факт {0}", draft.Subtype);
        Assert.IsTrue(draft.Stops.Count == 1, "одна точка объезда, факт {0}", draft.Stops.Count);

        await Db.ChangeSubtypeAsync("VisitRoute", route.MetaId, VisitRoute.Subtypes.Open);
        var opened = await DocumentManager.GetDocumentAsync<VisitRoute>(route.MetaId);
        Assert.IsTrue(opened!.Subtype == VisitRoute.Subtypes.Open,
            "маршрут открыт, факт {0}", opened.Subtype);

        var visit = await DocumentManager.NewDocumentAsync<AgentVisit>();
        visit.Customer = customer;
        visit.Route = route.MetaId;
        visit.StopSequence = 1;
        visit.DocumentDate = VisitDay;
        visit.CheckedInAt = VisitDay.AddHours(9);
        // DateTime.MinValue does not fit SQL datetime — an unset CheckedOutAt
        // would break the insert before the GeoStatus assert is reached.
        visit.CheckedOutAt = VisitDay.AddHours(9).AddMinutes(20);
        visit.GeoStatus = "NoGeo";
        visit.Lat = 0m;
        visit.Lng = 0m;
        await DocumentManager.SaveDocumentAsync(visit);

        var saved = await DocumentManager.GetDocumentAsync<AgentVisit>(visit.MetaId);
        Assert.IsTrue(saved != null, "визит без координат сохранён");
        Assert.IsTrue(saved!.GeoStatus == "NoGeo", "статус гео NoGeo, факт {0}", saved.GeoStatus);
        Assert.IsTrue(saved.Lat == 0m && saved.Lng == 0m,
            "координат нет, факт {0} / {1}", saved.Lat, saved.Lng);
        Assert.IsTrue(saved.Route == route.MetaId, "визит привязан к маршруту, факт {0}", saved.Route);
        Assert.IsTrue(saved.StopSequence == 1, "точка маршрута 1, факт {0}", saved.StopSequence);
    }

    [IntegrationTest("Клиент принадлежит своему агенту и не виден чужому")]
    public async Task CustomerBelongsToOwningSalesPersonOnly()
    {
        var personA = await NewAgentAsync("Anna");
        var personB = await NewAgentAsync("Boris");
        var customer = await NewCustomerAsync("Kiosk of Anna");

        var assigned = await Db.UpdateAsync("Customer", customer,
            new Dictionary<string, object?> { ["SalesPerson"] = personA });
        Assert.IsTrue(assigned, "агент клиенту назначен");

        var row = await Db.GetAsync("Customer", customer);
        Assert.IsTrue(row != null, "клиент читается после назначения агента");
        var owner = row!["SalesPerson"] is Guid g ? g : Guid.Parse(Convert.ToString(row["SalesPerson"])!);
        Assert.IsTrue(owner == personA, "владелец — агент A, факт {0}", owner);
        Assert.IsTrue(owner != personB, "агент B не владелец клиента");

        var mine = await Db.QueryAsync("Customer", $"SalesPerson = '{personA}'");
        Assert.IsTrue(mine.Any(r => (Guid)r["MetaId"]! == customer),
            "клиент попадает в выборку агента A, строк {0}", mine.Count);

        var theirs = await Db.QueryAsync("Customer", $"SalesPerson = '{personB}'");
        Assert.IsTrue(theirs.All(r => (Guid)r["MetaId"]! != customer),
            "клиент не попадает в выборку агента B, строк {0}", theirs.Count);
    }

    [IntegrationTest("Заказ визита виден на карточке визита и переезжает вместе с полем")]
    public async Task OrderOnVisitIsAChildAndMovesWithTheField()
    {
        var customer = await NewCustomerAsync("Visit shop");
        var outlet = DictionaryManager.NewRecord<CustomerOutlet>();
        outlet.Name = "Door";
        outlet.Customer = customer;
        outlet = await DictionaryManager.SaveRecordAsync(outlet);

        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Hryvnia";
        currency.Code = $"{Guid.NewGuid():N}"[..2].ToUpperInvariant();
        currency.Symbol = "₴";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Ukraine";
        country.CodeISO2 = $"{Guid.NewGuid():N}"[..2].ToUpperInvariant();
        country.CodeISO3 = $"{Guid.NewGuid():N}"[..3].ToUpperInvariant();
        country.PhoneCode = "380";
        country = await DictionaryManager.SaveRecordAsync(country);

        var legal = DictionaryManager.NewRecord<LegalEntity>();
        legal.Name = "Visit LLC";
        legal.RegistrationNumber = $"REG-V-{Guid.NewGuid():N}"[..16];
        legal.Country = country.MetaId;
        legal.Currency = currency.MetaId;
        legal = await DictionaryManager.SaveRecordAsync(legal);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"VT-{Guid.NewGuid():N}"[..10];
        divisionType.Name = "Visit point";
        divisionType = await DictionaryManager.SaveRecordAsync(divisionType);

        var division = DictionaryManager.NewRecord<Division>();
        division.Name = "Visit shop floor";
        division.LegalEntity = legal.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await DictionaryManager.SaveRecordAsync(division);

        var store = DictionaryManager.NewRecord<Store>();
        store.Name = "Visit WH";
        store.Division = division.MetaId;
        store.IsSimple = true;
        store = await DictionaryManager.SaveRecordAsync(store);

        var zone = DictionaryManager.NewRecord<StoreZone>();
        zone.Name = "Visit zone";
        zone.Store = store.MetaId;
        zone.IsBarcodeTracking = false;
        zone = await DictionaryManager.SaveRecordAsync(zone);

        var cellType = DictionaryManager.NewRecord<StoreCellType>();
        cellType.Code = $"VP-{Guid.NewGuid():N}"[..12];
        cellType.Name = "Visit pick";
        cellType = await DictionaryManager.SaveRecordAsync(cellType);

        var cell = DictionaryManager.NewRecord<StoreCell>();
        cell.Name = "V-01";
        cell.Type = cellType.MetaId;
        cell.StoreZone = zone.MetaId;
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = 1;
        cell = await DictionaryManager.SaveRecordAsync(cell);

        var contract = DictionaryManager.NewRecord<SalesContract>();
        contract.Name = "Visit-2026";
        contract.Outlet = outlet.MetaId;
        contract.Currency = currency.MetaId;
        contract.SettlementKind = SettlementKind.CashOnDelivery;
        contract.EffectiveFrom = new DateTime(2026, 1, 1);
        contract.LegalEntity = legal.MetaId;
        await DictionaryManager.SaveRecordAsync(contract);

        var visit = await DocumentManager.NewDocumentAsync<AgentVisit>();
        visit.Customer = customer;
        visit.Outlet = outlet.MetaId;
        visit.DocumentDate = VisitDay;
        visit.CheckedInAt = VisitDay.AddHours(10);
        visit.CheckedOutAt = VisitDay.AddHours(10).AddMinutes(15);
        visit.GeoStatus = "NoGeo";
        await DocumentManager.SaveDocumentAsync(visit);

        var other = await DocumentManager.NewDocumentAsync<AgentVisit>();
        other.Customer = customer;
        other.Outlet = outlet.MetaId;
        other.DocumentDate = VisitDay;
        other.CheckedInAt = VisitDay.AddHours(12);
        other.CheckedOutAt = VisitDay.AddHours(12).AddMinutes(10);
        other.GeoStatus = "NoGeo";
        await DocumentManager.SaveDocumentAsync(other);

        var order = await DocumentManager.NewDocumentAsync<SalesOrder>();
        order.Customer = customer;
        order.Outlet = outlet.MetaId;
        order.Contract = contract.MetaId;
        order.Location = cell.MetaId;
        order.DeliveryDate = VisitDay;
        order.Visit = visit.MetaId;
        await DocumentManager.SaveDocumentAsync(order);

        var children = await DocumentManager.GetDocumentChildrenAsync(visit.MetaId);
        Assert.IsTrue(children.Contains(order.MetaId), "визит называет заказ");
        var parents = await DocumentManager.GetDocumentParentsAsync(order.MetaId);
        Assert.IsTrue(parents.Contains(visit.MetaId), "заказ называет визит");

        var moved = await DocumentManager.GetDocumentAsync<SalesOrder>(order.MetaId);
        moved!.Visit = other.MetaId;
        await DocumentManager.SaveDocumentAsync(moved);

        var left = await DocumentManager.GetDocumentChildrenAsync(visit.MetaId);
        Assert.IsTrue(!left.Contains(order.MetaId), "со старого визита заказ ушёл");
        var arrived = await DocumentManager.GetDocumentChildrenAsync(other.MetaId);
        Assert.IsTrue(arrived.Contains(order.MetaId), "на новом визите заказ есть");
    }
}
