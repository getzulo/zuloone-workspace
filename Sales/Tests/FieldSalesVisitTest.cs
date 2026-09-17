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
}
