using System;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Core Tax never blocks buyer release. On a stand with LocalizationSaudiArabia
// the wrap is on top — this test only pins the contract exists. The ZATCA
// block itself is ZatcaEInvoiceTest.
public class EInvoiceReleaseTest : IntegrationTestScriptBase
{
    [IntegrationTest("IEInvoiceRelease есть и на пустом id не бросает")]
    public async Task EmptySourceIsNotBlocked()
    {
        var block = await GetService<IEInvoiceRelease>().BuyerReleaseBlockAsync(Guid.Empty);
        Assert.IsTrue(block == null, "ядро не блокирует пустой id, факт '{0}'", block);
    }
}
