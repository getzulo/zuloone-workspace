using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

public class LearningPublishTest : IntegrationTestScriptBase
{
    private static IDictionaryManager Dictionaries => GetService<IDictionaryManager>();
    private static IDocumentManager Documents => GetService<IDocumentManager>();

    [IntegrationTest("Вторая публикация страницы оставляет текст первой")]
    public async Task SecondPageKeepsFirstText()
    {
        var catalog = GetService<ILearningCatalog>();
        var moduleRevision = await catalog.PublishModuleAsync(
            "learn.getting-started", "Начало работы", 15, "/docs/getting-started", "a1");
        var first = await catalog.PublishUnitAsync(
            "learn.getting-started.welcome", moduleRevision, "Привет", "Текст первой", 1);
        var second = await catalog.PublishUnitAsync(
            "learn.getting-started.welcome", moduleRevision, "Привет", "Текст второй", 1);

        Assert.IsTrue(first != second, "вторая публикация должна дать новую версию");
        var old = await Dictionaries.GetRecordAsync<UnitRevision>(first);
        Assert.AreEqual("Текст первой", old!.Body);
        var card = (await Dictionaries.GetRecordsAsync<Unit>(
            "StableId = 'learn.getting-started.welcome'")).First();
        Assert.AreEqual(second, card.PublishedRevision);
    }

    [IntegrationTest("Новая версия экзамена не меняет снимок первой попытки")]
    public async Task NewExamVersionKeepsAttemptSnapshot()
    {
        var session = GetService<IExamSession>();
        var learner = Dictionaries.NewRecord<Learner>();
        learner.Email = "learner-" + Guid.NewGuid().ToString("N").Substring(0, 12) + "@example.com";
        learner.Name = "Учащийся";
        learner = await Dictionaries.SaveRecordAsync(learner);

        var exam = Dictionaries.NewRecord<Exam>();
        exam.Name = "Сложение";
        exam = await Dictionaries.SaveRecordAsync(exam);

        await session.PublishAsync(exam.MetaId, 70, 365, "Текст первой", "4", "5");
        var attemptId = await session.StartAttemptAsync(learner.MetaId, exam.MetaId);
        var attempt = await Documents.GetDocumentAsync<ExamAttempt>(attemptId);
        Assert.AreEqual("Текст первой", attempt!.Lines.First().PromptText);

        await session.PublishAsync(exam.MetaId, 70, 365, "Текст второй", "6", "7");
        var again = await Documents.GetDocumentAsync<ExamAttempt>(attemptId);
        Assert.AreEqual("Текст первой", again!.Lines.First().PromptText);

        var card = await Dictionaries.GetRecordAsync<Exam>(exam.MetaId);
        Assert.IsTrue(card!.PublishedRevision != attempt.ExamRevision,
            "карточка должна смотреть на новую версию");
    }
}
