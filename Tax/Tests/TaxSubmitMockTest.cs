using System;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Мок канала в госорган: сеть не зовётся, квитанция всегда MOCK-OK.
// Сдача декларации (Filed isReadOnly) не должна зависеть от ответа мока —
// иначе стенд без госсистемы не смог бы зафиксировать сданное.
public class TaxSubmitMockTest : IntegrationTestScriptBase
{
    private static ITaxAuthoritySubmitService Submit => GetService<ITaxAuthoritySubmitService>();
    private static ITaxReturnService Returns => GetService<ITaxReturnService>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();

    [IntegrationTest("SubmitReturnAsync возвращает квитанцию MOCK-OK:RETURN")]
    public async Task SubmitReturnReturnsMockOk()
    {
        var id = Db.NewId();
        var receipt = await Submit.SubmitReturnAsync(id);

        Assert.IsTrue(receipt.StartsWith("MOCK-OK:RETURN", StringComparison.Ordinal),
            "мок обязан принять декларацию, факт {0}", receipt);
        Assert.IsTrue(receipt.Contains(id.ToString("N"), StringComparison.OrdinalIgnoreCase),
            "квитанция несёт id документа, факт {0}", receipt);
    }

    [IntegrationTest("Сдача декларации в Filed проходит, мок вызывается")]
    public async Task FilingReturnStillWorks()
    {
        var le = Db.NewId();
        var id = await Returns.BuildAsync(le, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));

        var draft = await DocumentManager.GetDocumentAsync<TaxReturn>(id);
        Assert.IsTrue(draft!.Subtype == "Draft", "декларация собирается черновиком, факт {0}", draft.Subtype);

        // Filed isReadOnly: строки пишутся в Draft, перевод — отдельным шагом.
        await Db.ChangeSubtypeAsync("TaxReturn", id, "Filed");

        var filed = await DocumentManager.GetDocumentAsync<TaxReturn>(id);
        Assert.IsTrue(filed!.Subtype == "Filed", "декларация сдана, факт {0}", filed.Subtype);

        var receipt = await Submit.SubmitReturnAsync(id);
        Assert.IsTrue(receipt.StartsWith("MOCK-OK:RETURN", StringComparison.Ordinal),
            "мок вызывается после сдачи, факт {0}", receipt);
    }
}
