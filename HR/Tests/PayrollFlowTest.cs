using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

public class PayrollFlowTest : IntegrationTestScriptBase
{
    [IntegrationTest("Начисление ФОТ пишет в Payroll и PayrollLiability")]
    public async Task AccrualPostsToRegisters()
    {
        var div = Db.NewId();
        var emp1 = Db.NewId();
        var emp2 = Db.NewId();

        var doc = await Db.CreateDocumentAsync("PayrollAccrual",
            new Dictionary<string, object?> { ["Division"] = div },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            {
                ["Lines"] = new[]
                {
                    new Dictionary<string, object?> { ["Employee"] = emp1, ["Amount"] = 100m },
                    new Dictionary<string, object?> { ["Employee"] = emp2, ["Amount"] = 60m },
                },
            });
        await Db.ChangeSubtypeAsync("PayrollAccrual", doc, "Posted");

        decimal payroll = 0m;
        foreach (var r in await Db.QueryBalancesAsync("Payroll")) payroll += Convert.ToDecimal(r["Amount"]);
        Assert.IsTrue(payroll == 160m, "Payroll итого 160, факт {0}", payroll);

        decimal liab = 0m;
        foreach (var r in await Db.QueryBalancesAsync("PayrollLiability")) liab += Convert.ToDecimal(r["Amount"]);
        Assert.IsTrue(liab == 160m, "PayrollLiability итого 160, факт {0}", liab);
    }

    [IntegrationTest("Выплата гасит задолженность в ноль")]
    public async Task PaymentSettlesLiability()
    {
        var div = Db.NewId();
        var emp = Db.NewId();

        var acc = await Db.CreateDocumentAsync("PayrollAccrual",
            new Dictionary<string, object?> { ["Division"] = div },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            { ["Lines"] = new[] { new Dictionary<string, object?> { ["Employee"] = emp, ["Amount"] = 100m } } });
        await Db.ChangeSubtypeAsync("PayrollAccrual", acc, "Posted");

        decimal afterAccrual = 0m;
        foreach (var r in await Db.QueryBalancesAsync("PayrollLiability")) afterAccrual += Convert.ToDecimal(r["Amount"]);
        Assert.IsTrue(afterAccrual == 100m, "после начисления задолженность 100, факт {0}", afterAccrual);

        var pay = await Db.CreateDocumentAsync("PayrollPayment",
            new Dictionary<string, object?>(),
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            { ["Lines"] = new[] { new Dictionary<string, object?> { ["Employee"] = emp, ["Amount"] = 100m } } });
        await Db.ChangeSubtypeAsync("PayrollPayment", pay, "Paid");

        decimal afterPayment = 0m;
        foreach (var r in await Db.QueryBalancesAsync("PayrollLiability")) afterPayment += Convert.ToDecimal(r["Amount"]);
        Assert.IsTrue(afterPayment == 0m, "задолженность 0 после выплаты, факт {0}", afterPayment);
    }

    [IntegrationTest("Переплата отклоняется (PayrollLiability allowNegativeBalance=false)")]
    public async Task OverpaymentRejected()
    {
        var div = Db.NewId();
        var emp = Db.NewId();

        var acc = await Db.CreateDocumentAsync("PayrollAccrual",
            new Dictionary<string, object?> { ["Division"] = div },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            { ["Lines"] = new[] { new Dictionary<string, object?> { ["Employee"] = emp, ["Amount"] = 100m } } });
        await Db.ChangeSubtypeAsync("PayrollAccrual", acc, "Posted");

        var pay = await Db.CreateDocumentAsync("PayrollPayment",
            new Dictionary<string, object?>(),
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            { ["Lines"] = new[] { new Dictionary<string, object?> { ["Employee"] = emp, ["Amount"] = 150m } } });

        var rejected = false;
        try
        {
            await Db.ChangeSubtypeAsync("PayrollPayment", pay, "Paid");
        }
        catch (Exception)
        {
            rejected = true;
        }
        Assert.IsTrue(rejected, "выплата 150 при начислении 100 должна быть отклонена");
    }
}
