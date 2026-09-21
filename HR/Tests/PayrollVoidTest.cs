using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;

public class PayrollVoidTest : IntegrationTestScriptBase
{
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private static async Task<decimal> LiabilityAsync(Guid employee)
        => await TotalsManager.GetBalanceAsync("PayrollLiability", "Amount",
            new Dictionary<string, object?> { ["Employee"] = employee });

    private static async Task<decimal> PayrollAsync(Guid employee)
        => await TotalsManager.GetBalanceAsync("Payroll", "Amount",
            new Dictionary<string, object?> { ["Employee"] = employee });

    private async Task RunCommandAsync(string name, Guid documentId)
    {
        var commandId = await Db.FindCommandIdAsync("document", name);
        var run = await Db.ExecuteDocumentCommandAsync(commandId, documentId);
        Assert.IsTrue(run.Success, "команда {0}: {1}", name, run.Message ?? string.Join("; ", run.ClientMessages));
    }

    private async Task<PayrollAccrual> AccrueAsync(Guid division, Guid employee, decimal amount)
    {
        var doc = await DocumentManager.NewDocumentAsync<PayrollAccrual>();
        doc.Division = division;
        doc.Lines.Add(new PayrollAccrualLinesTablePartRow { Employee = employee, Amount = amount });
        await DocumentManager.SaveDocumentAsync(doc);
        doc.Subtype = PayrollAccrual.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(doc);
        return (await DocumentManager.GetDocumentAsync<PayrollAccrual>(doc.MetaId))!;
    }

    [IntegrationTest("Аннулирование снимает Payroll и задолженность")]
    public async Task VoidPostedClearsRegisters()
    {
        var emp = Db.NewId();
        var acc = await AccrueAsync(Db.NewId(), emp, 100m);
        Assert.IsTrue(await PayrollAsync(emp) == 100m, "Payroll 100, факт {0}", await PayrollAsync(emp));
        Assert.IsTrue(await LiabilityAsync(emp) == 100m, "задолженность 100, факт {0}", await LiabilityAsync(emp));

        await RunCommandAsync("VoidPayrollAccrual", acc.MetaId);

        var stored = await DocumentManager.GetDocumentAsync<PayrollAccrual>(acc.MetaId);
        Assert.IsTrue(stored!.Subtype == PayrollAccrual.Subtypes.Voided,
            "Voided, факт {0}", stored.Subtype);
        Assert.IsTrue(await PayrollAsync(emp) == 0m, "Payroll 0 после аннулирования, факт {0}", await PayrollAsync(emp));
        Assert.IsTrue(await LiabilityAsync(emp) == 0m, "задолженность 0, факт {0}", await LiabilityAsync(emp));
    }

    [IntegrationTest("Выплаченное начисление аннулировать нельзя")]
    public async Task VoidAfterPaymentIsRefused()
    {
        var emp = Db.NewId();
        var acc = await AccrueAsync(Db.NewId(), emp, 100m);

        var pay = await DocumentManager.NewDocumentAsync<PayrollPayment>();
        pay.Lines.Add(new PayrollPaymentLinesTablePartRow { Employee = emp, Amount = 100m });
        await DocumentManager.SaveDocumentAsync(pay);
        pay.Subtype = PayrollPayment.Subtypes.Paid;
        await DocumentManager.SaveDocumentAsync(pay);

        var commandId = await Db.FindCommandIdAsync("document", "VoidPayrollAccrual");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, acc.MetaId);
        var text = (run.Message ?? "") + " " + string.Join("; ", run.ClientMessages);
        Assert.IsTrue(text.Contains("погашена"), "отказ про выплату, факт: {0}", text);

        var stored = await DocumentManager.GetDocumentAsync<PayrollAccrual>(acc.MetaId);
        Assert.IsTrue(stored!.Subtype == PayrollAccrual.Subtypes.Posted,
            "остаётся Posted, факт {0}", stored.Subtype);
        Assert.IsTrue(await LiabilityAsync(emp) == 0m, "выплата не тронута, задолженность 0");
    }

    [IntegrationTest("Сторно выплаты, затем начисления, обнуляет регистры")]
    public async Task VoidPaymentThenAccrual()
    {
        var emp = Db.NewId();
        var acc = await AccrueAsync(Db.NewId(), emp, 80m);

        var pay = await DocumentManager.NewDocumentAsync<PayrollPayment>();
        pay.Lines.Add(new PayrollPaymentLinesTablePartRow { Employee = emp, Amount = 80m });
        await DocumentManager.SaveDocumentAsync(pay);
        pay.Subtype = PayrollPayment.Subtypes.Paid;
        await DocumentManager.SaveDocumentAsync(pay);
        Assert.IsTrue(await LiabilityAsync(emp) == 0m, "после выплаты 0");

        await RunCommandAsync("VoidPayrollPayment", pay.MetaId);
        var paid = await DocumentManager.GetDocumentAsync<PayrollPayment>(pay.MetaId);
        Assert.IsTrue(paid!.Subtype == PayrollPayment.Subtypes.Voided,
            "выплата Voided, факт {0}", paid.Subtype);
        Assert.IsTrue(await LiabilityAsync(emp) == 80m, "задолженность вернулась 80, факт {0}", await LiabilityAsync(emp));

        await RunCommandAsync("VoidPayrollAccrual", acc.MetaId);
        Assert.IsTrue(await LiabilityAsync(emp) == 0m, "после аннулирования начисления 0");
        Assert.IsTrue(await PayrollAsync(emp) == 0m, "Payroll 0");
    }
}
