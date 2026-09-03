public partial class DeliveryTripTransactionsScript
{
    protected override void GetTransactions(DeliveryTrip document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
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
        // на вкладке «Аналитики»). Имена ТИПИЗИРОВАНЫ — генерируется класс
        // Analytics: Analytics.<Аналитика> — весь каталог, а
        // Analytics.<Регистр>.<Аналитика> — только привязанные к этому регистру,
        // так что опечатка и непривязанная аналитика перестают компилироваться
        // вместо того, чтобы уронить проведение. Голая строка тоже принимается.
        // Fluent-спека: .Dim сам маршрутизирует имя в физическое измерение ИЛИ
        // в привязанную аналитику, .An задаёт аналитику явно:
        // transactions.Add(new RegisterMovementSpec("SomeRegister")
        //     .Dim("Warehouse", document.Warehouse)            // физическое измерение
        //     .An(Analytics.SomeRegister.Товар, line.Item)     // аналитика
        //     .An(Analytics.SomeRegister.ТипЦены, "розница")
        //     .Res("Amount", line.Amount ?? 0m));
        //
        // У typed-строки (new SomeRegister { ... }) свойств-аналитик НЕТ — в
        // сгенерированном классе регистра только физические измерения и ресурсы
        // (аналитика намеренно не колонка: привязать новую можно без миграции).
        // Чтобы к такому движению добавить аналитику, преобразуйте строку в
        // спеку через RegisterMovementSpec.From(...) и допишите .An(...):
        // transactions.Add(RegisterMovementSpec
        //     .From(new SomeRegister { Warehouse = document.Warehouse, Quantity = -(line.Quantity ?? 0m) })
        //     .An(Analytics.SomeRegister.Товар, line.Item));
    }
}
