using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные классы (PayrollAccrual, PayrollAccrualLinesTablePartRow…).
// Тест-скрипты НЕ получают это пространство имён глобальным using'ом.
using ZuloOne.Runtime.Generated;

// ФОТ: начисление пишет в два регистра, выплата гасит задолженность, переплата
// отклоняется запретом отрицательного остатка.
//
// Сценарии собраны менеджерами и типизированными сущностями: документ через
// IDocumentManager (строки — типизированные строки табличной части, проведение —
// присваивание подтипа плюс сохранение), остатки — через ITotalsManager.
public class PayrollFlowTest : IntegrationTestScriptBase
{
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private async Task<decimal> RegisterTotalAsync(string register)
    {
        decimal total = 0m;
        foreach (var row in await TotalsManager.QueryBalancesAsync(register))
            total += Convert.ToDecimal(row["Amount"]);
        return total;
    }

    [IntegrationTest("Начисление ФОТ пишет в Payroll и PayrollLiability")]
    public async Task AccrualPostsToRegisters()
    {
        // Db.NewId() остаётся: измерения регистров — просто ключи, записи
        // сотрудников этому сценарию не нужны, а свежие id держат срез прогона
        // независимым.
        var div = Db.NewId();
        var emp1 = Db.NewId();
        var emp2 = Db.NewId();

        // Подтип не передаётся: документ стартует в НАЧАЛЬНОМ подтипе (Draft).
        var doc = await DocumentManager.NewDocumentAsync<PayrollAccrual>();
        doc.Division = div;
        doc.Lines.Add(new PayrollAccrualLinesTablePartRow { Employee = emp1, Amount = 100m });
        doc.Lines.Add(new PayrollAccrualLinesTablePartRow { Employee = emp2, Amount = 60m });
        await DocumentManager.SaveDocumentAsync(doc);

        // Тип документа помечен postOnSave — без снимка ДО перехода проверки
        // после него проходят даже когда переход ничего не сделал.
        Assert.IsTrue(await RegisterTotalAsync("Payroll") == 0m, "черновик не должен начислять ФОТ");
        Assert.IsTrue(await RegisterTotalAsync("PayrollLiability") == 0m, "черновик не должен создавать задолженность");

        doc.Subtype = PayrollAccrual.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(doc);

        var payroll = await RegisterTotalAsync("Payroll");
        Assert.IsTrue(payroll == 160m, "Payroll итого 160, факт {0}", payroll);

        var liab = await RegisterTotalAsync("PayrollLiability");
        Assert.IsTrue(liab == 160m, "PayrollLiability итого 160, факт {0}", liab);
    }

    [IntegrationTest("Выплата гасит задолженность в ноль")]
    public async Task PaymentSettlesLiability()
    {
        var div = Db.NewId();
        var emp = Db.NewId();

        var acc = await DocumentManager.NewDocumentAsync<PayrollAccrual>();
        acc.Division = div;
        acc.Lines.Add(new PayrollAccrualLinesTablePartRow { Employee = emp, Amount = 100m });
        await DocumentManager.SaveDocumentAsync(acc);

        Assert.IsTrue(await RegisterTotalAsync("PayrollLiability") == 0m,
            "до проведения задолженности нет");

        acc.Subtype = PayrollAccrual.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(acc);

        var afterAccrual = await RegisterTotalAsync("PayrollLiability");
        Assert.IsTrue(afterAccrual == 100m, "после начисления задолженность 100, факт {0}", afterAccrual);

        var pay = await DocumentManager.NewDocumentAsync<PayrollPayment>();
        pay.Lines.Add(new PayrollPaymentLinesTablePartRow { Employee = emp, Amount = 100m });
        await DocumentManager.SaveDocumentAsync(pay);

        Assert.IsTrue(await RegisterTotalAsync("PayrollLiability") == 100m,
            "черновик выплаты ничего не гасит");

        pay.Subtype = PayrollPayment.Subtypes.Paid;
        await DocumentManager.SaveDocumentAsync(pay);

        var afterPayment = await RegisterTotalAsync("PayrollLiability");
        Assert.IsTrue(afterPayment == 0m, "задолженность 0 после выплаты, факт {0}", afterPayment);
    }

    [IntegrationTest("Переплата отклоняется (PayrollLiability allowNegativeBalance=false)")]
    public async Task OverpaymentRejected()
    {
        var div = Db.NewId();
        var emp = Db.NewId();

        var acc = await DocumentManager.NewDocumentAsync<PayrollAccrual>();
        acc.Division = div;
        acc.Lines.Add(new PayrollAccrualLinesTablePartRow { Employee = emp, Amount = 100m });
        await DocumentManager.SaveDocumentAsync(acc);
        acc.Subtype = PayrollAccrual.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(acc);

        var pay = await DocumentManager.NewDocumentAsync<PayrollPayment>();
        pay.Lines.Add(new PayrollPaymentLinesTablePartRow { Employee = emp, Amount = 150m });
        await DocumentManager.SaveDocumentAsync(pay);

        // Отказ — это ИСКЛЮЧЕНИЕ, а бросок внутри окружающей транзакции раннера
        // обрекает её: любое обращение к базе после catch падает с «the operation
        // is not valid for the state of the transaction» и маскирует проверку.
        // Поэтому проверяется сам факт отказа, и к базе больше не обращаемся.
        var rejected = false;
        try
        {
            pay.Subtype = PayrollPayment.Subtypes.Paid;
            await DocumentManager.SaveDocumentAsync(pay);
        }
        catch (Exception)
        {
            rejected = true;
        }
        Assert.IsTrue(rejected, "выплата 150 при начислении 100 должна быть отклонена");
    }
}
