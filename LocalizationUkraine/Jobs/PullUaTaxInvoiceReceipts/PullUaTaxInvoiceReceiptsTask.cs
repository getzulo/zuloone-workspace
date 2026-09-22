#nullable enable
using System.Threading.Tasks;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Tasks;
using ZuloOne.Services.Contracts;

// Квитанція ЄРПН приходить НЕ у відповідь на подачу.
//
// Оператор відповідає «прийняв до обробки» за секунди, а ДПС реєструє накладну
// хвилинами, іноді годинами. Тобто «зареєстровано» — це ДРУГА подія, і жодного
// вхідного виклику ззовні платформа не приймає: колбеку немає, черга дренує
// лише саму себе. Отже квитанцію треба ЗАБИРАТИ, і робить це оце завдання.
//
// Спершу розбираємо власну чергу (чи взагалі пішло), потім питаємо оператора
// про долю ще не закритих накладних. Порядок саме такий: питати про накладну,
// яка не відправилася, немає сенсу.
//
// Раз на 7 хвилин, а не щохвилини: реєстрація не миттєва, а строк подачі
// обмежений законом — зайвий опит дешевший за прострочення.
public class PullUaTaxInvoiceReceiptsTask : TaskScriptBase
{
    public override async Task ExecuteAsync(TaskContext context)
    {
        var service = ScriptServices.Get<IUaEInvoice>();

        var refused = await service.ApplyOutcomesAsync();
        var closed = await service.PullReceiptsAsync();

        context.Log($"ЄРПН: відхилено під час відправлення {refused}, закрито квитанцією {closed}.");
    }
}
