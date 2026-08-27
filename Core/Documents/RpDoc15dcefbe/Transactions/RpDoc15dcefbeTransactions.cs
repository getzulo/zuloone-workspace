using System.Collections.Generic;
using ZuloOne.Runtime.Documents;

public class RpTx15dcefbe : DocumentTransactionScriptBase
{
    public override void GetTransactions(DocumentContext document, List<RegisterMovementSpec> movements)
    {
        foreach (var row in document.TableParts["Items"])
        {
            movements.Add(new RegisterMovementSpec("RpReg15dcefbe")
                .Dim("Item", row.Str("Item"))
                .Res("Quantity", row.Dec("Quantity")));
        }
    }
}