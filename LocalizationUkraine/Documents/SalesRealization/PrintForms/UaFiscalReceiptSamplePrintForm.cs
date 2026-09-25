#nullable enable
using System.Threading.Tasks;

// Sample 80 mm tape that looks like a Ukrainian fiscal receipt.
// The layout is static. Nothing here talks to a PRRO.
public partial class UaFiscalReceiptSamplePrintForm : PrintFormBase
{
    public override SlimTable GetDataTemplate()
        => new SlimTable(new { Note = "" });

    public override Task<SlimTable> GetDataAsync(PrintFormContext context)
    {
        var table = new SlimTable("Report");
        table.Add(new { Note = "" });
        return Task.FromResult(table);
    }
}
