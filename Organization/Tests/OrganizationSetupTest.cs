using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Integration coverage for the Organization foundation: a legal entity is the taxable
// unit (country + currency mandatory), and a division always belongs to a legal entity.
public class OrganizationSetupTest : IntegrationTestScriptBase
{
    [IntegrationTest("Юрлицо со страной и валютой создаётся")]
    public async Task LegalEntityIsCreated()
    {
        var currency = await Db.InsertAsync("Currency", new Dictionary<string, object?>
            { ["Name"] = "US Dollar", ["Code"] = "USD", ["Symbol"] = "$" });
        var country = await Db.InsertAsync("Country", new Dictionary<string, object?>
            { ["Name"] = "United States", ["CodeISO2"] = "US", ["CodeISO3"] = "USA", ["PhoneCode"] = "1" });

        var le = await Db.InsertAsync("LegalEntity", new Dictionary<string, object?>
        {
            ["Name"] = "ACME LLC",
            ["RegistrationNumber"] = "REG-100",
            ["Country"] = country,
            ["Currency"] = currency,
        });

        Assert.IsTrue(le != Guid.Empty, "ожидался реальный id юрлица");
        var row = await Db.GetAsync("LegalEntity", le);
        Assert.IsTrue(row != null, "юрлицо должно читаться из базы");
        Assert.AreEqual("ACME LLC", row!["Name"]);
    }

    [IntegrationTest("Подразделение создаётся под юрлицом")]
    public async Task DivisionIsCreated()
    {
        var currency = await Db.InsertAsync("Currency", new Dictionary<string, object?>
            { ["Name"] = "Euro", ["Code"] = "EUR", ["Symbol"] = "€" });
        var country = await Db.InsertAsync("Country", new Dictionary<string, object?>
            { ["Name"] = "Germany", ["CodeISO2"] = "DE", ["CodeISO3"] = "DEU", ["PhoneCode"] = "49" });
        var le = await Db.InsertAsync("LegalEntity", new Dictionary<string, object?>
            { ["Name"] = "Muster GmbH", ["RegistrationNumber"] = "REG-200", ["Country"] = country, ["Currency"] = currency });

        var dt = await Db.InsertAsync("DivisionType", new Dictionary<string, object?>
            { ["Code"] = "WH", ["Name"] = "Warehouse" });
        var div = await Db.InsertAsync("Division", new Dictionary<string, object?>
            { ["Name"] = "Main warehouse", ["LegalEntity"] = le, ["DivisionType"] = dt });

        Assert.IsTrue(div != Guid.Empty, "ожидался реальный id подразделения");
        var row = await Db.GetAsync("Division", div);
        Assert.IsTrue(row != null, "подразделение должно читаться из базы");
        Assert.IsTrue(row!["LegalEntity"]?.ToString() == le.ToString(),
            "подразделение должно быть привязано к своему юрлицу, а не к {0}", row!["LegalEntity"]);
    }

    [IntegrationTest("Юрлицо без страны и валюты отклоняется")]
    public async Task LegalEntityWithoutRefsIsRejected()
    {
        var rejected = false;
        try
        {
            var bad = await Db.InsertAsync("LegalEntity", new Dictionary<string, object?>
                { ["Name"] = "No refs Ltd", ["RegistrationNumber"] = "REG-300" });
            rejected = bad == Guid.Empty;
        }
        catch
        {
            rejected = true;
        }
        Assert.IsTrue(rejected, "юрлицо без страны/валюты должно быть отклонено обработчиком");
    }
}
