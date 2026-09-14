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
        // Dynamic ANALYTICS (flexible slices without columns; bound to the register
        // on the Analytics tab). Names are TYPED — a class Analytics is generated:
        // Analytics.<Analytic> is the full catalog, and
        // Analytics.<Register>.<Analytic> is only those bound to this register,
        // so a typo or an unbound analytic fails to compile instead of failing
        // at posting. A bare string is still accepted.
        // Fluent spec: .Dim routes the name to a physical dimension OR
        // a bound analytic, .An sets the analytic explicitly:
        // transactions.Add(new RegisterMovementSpec("SomeRegister")
        //     .Dim("Warehouse", document.Warehouse)            // physical dimension
        //     .An(Analytics.SomeRegister.Item, line.Item)      // analytic
        //     .An(Analytics.SomeRegister.PriceType, "retail")
        //     .Res("Amount", line.Amount ?? 0m));
        //
        // A typed row (new SomeRegister { ... }) has NO analytic properties — the
        // generated register class has only physical dimensions and resources
        // (analytics are intentionally not columns: a new one can be bound without
        // a migration). To add an analytic to such a movement, convert the row to
        // a spec via RegisterMovementSpec.From(...) and append .An(...):
        // transactions.Add(RegisterMovementSpec
        //     .From(new SomeRegister { Warehouse = document.Warehouse, Quantity = -(line.Quantity ?? 0m) })
        //     .An(Analytics.SomeRegister.Item, line.Item));
    }
}
