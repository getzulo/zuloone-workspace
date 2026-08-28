public partial class TimeSheetTransactionsScript
{
    protected override void GetTransactions(TimeSheet document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        // Typed register rows (entity classes named after registers), MIQS style:
        // dimension = property, decimal property = signed resource delta.
        // foreach (var line in document.Items)
        // {
        //     transactionPairs.Add(                                  // double-entry pair (sub, add)
        //         new SomeRegister { Warehouse = document.Warehouse, Amount = -(line.Amount ?? 0m) },
        //         new OtherRegister { Customer = document.Customer, Amount = line.Amount ?? 0m });
        //     transactions.Add(                                      // single movement
        //         new SomeRegister { Warehouse = document.Warehouse, Quantity = line.Quantity ?? 0m });
        // }
        //
        // Dynamic ANALYTICS (гибкие разрезы без колонок; привязываются к регистру
        // на вкладке «Аналитики»). Fluent-спека: .Dim сам маршрутизирует имя в
        // физическое измерение ИЛИ привязанную аналитику, .An задаёт аналитику явно:
        // transactions.Add(new RegisterMovementSpec("SomeRegister")
        //     .Dim("Warehouse", document.Warehouse)   // физическое измерение
        //     .Dim("Товар", line.Item)                // привязанная аналитика — можно так
        //     .An("ТипЦены", "розница")               // …или явно через .An
        //     .Res("Amount", line.Amount ?? 0m));
        //
        // У typed-строки (new SomeRegister { ... }) свойств-аналитик НЕТ — в
        // сгенерированном классе регистра только физические измерения и ресурсы.
        // Чтобы к такому движению добавить аналитику, преобразуйте строку в
        // спеку через RegisterMovementSpec.From(...) и допишите .An(...):
        // transactions.Add(RegisterMovementSpec
        //     .From(new SomeRegister { Warehouse = document.Warehouse, Quantity = -(line.Quantity ?? 0m) })
        //     .An("Товар", line.Item));
    }
}
