using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// «Провести взносы»: суммы строк сверяются с ISocialInsuranceService
// (ставки и потолок из HRSettings). CreateAccrualAsync — порождение из ФОТ,
// не кнопка этого документа.
public partial class PostSocialInsuranceAccrualCommand
{
    public override async Task ExecuteAsync(SocialInsuranceAccrual document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SocialInsuranceAccrual>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя провести пустое начисление взносов: добавьте строки."));
            return;
        }

        var si = context.GetService<ISocialInsuranceService>();
        foreach (var line in full.Lines)
        {
            var (employee, employer) = await si.CalculateAsync(line.Employee, line.ContributoryBase);
            if (employee == 0m && employer == 0m) continue;
            if (employee != line.EmployeeContribution || employer != line.EmployerContribution)
            {
                context.AddClientAction(ClientAction.Message(
                    $"Взносы строки не сходятся с расчётом сервиса: работник {line.EmployeeContribution}≠{employee}, работодатель {line.EmployerContribution}≠{employer}."));
                return;
            }
        }

        full.Subtype = SocialInsuranceAccrual.Subtypes.Posted;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Взносы проведены."));
    }
}
