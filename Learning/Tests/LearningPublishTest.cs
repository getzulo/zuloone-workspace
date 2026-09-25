using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
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

    [IntegrationTest("Повтор пакета не меняет версию, ключ не лежит в тексте")]
    public async Task ImportKeepsRevisionAndKeepsKeyOffThePage()
    {
        var catalog = GetService<ILearningCatalog>();
        const string package =
            "{\"tracks\":[{\"stableId\":\"learn.pkg.track\",\"name\":\"Путь\",\"modules\":[\"learn.pkg.sample\"]}]," +
            "\"modules\":[{\"stableId\":\"learn.pkg.sample\",\"name\":\"Образец\",\"minutes\":5," +
            "\"articlePath\":\"wiki/user/getting-started.md\",\"articleRevision\":\"\"," +
            "\"units\":[{\"stableId\":\"learn.pkg.sample.check\",\"name\":\"Проверка\",\"kind\":\"check\",\"sortOrder\":1," +
            "\"body\":\"Текст без ключа\",\"checks\":[{\"id\":\"q1\",\"text\":\"Вопрос\",\"options\":[" +
            "{\"id\":\"no\",\"text\":\"Нет\",\"correct\":false},{\"id\":\"yes\",\"text\":\"Да\",\"correct\":true}]}]}]}]}";

        await catalog.ImportAsync(package);
        var card = (await Dictionaries.GetRecordsAsync<Unit>("StableId = 'learn.pkg.sample.check'")).First();
        var first = card.PublishedRevision;
        var page = await Dictionaries.GetRecordAsync<UnitRevision>(first);
        Assert.AreEqual("Текст без ключа", page!.Body);
        Assert.IsTrue(page.Body.IndexOf("correct", StringComparison.Ordinal) < 0, "ключ не должен лежать в тексте");
        Assert.AreEqual(UnitKind.Check, page.Kind);

        var track = (await Dictionaries.GetRecordsAsync<Track>("StableId = 'learn.pkg.track'")).First();
        var trackRevision = track.PublishedRevision;
        var links = await GetService<ILinkTableManager>().GetRecordsAsync<LT_TrackRevisionModule>("TrackRevision", trackRevision);
        Assert.AreEqual(1, links.Count);

        await catalog.ImportAsync(package);
        card = (await Dictionaries.GetRecordsAsync<Unit>("StableId = 'learn.pkg.sample.check'")).First();
        Assert.AreEqual(first, card.PublishedRevision);
        track = (await Dictionaries.GetRecordsAsync<Track>("StableId = 'learn.pkg.track'")).First();
        Assert.AreEqual(trackRevision, track.PublishedRevision);

        var changed = package
            .Replace("\"Текст без ключа\"", "\"Текст второй\"")
            .Replace("\"id\":\"yes\",\"text\":\"Да\",\"correct\":true", "\"id\":\"yes\",\"text\":\"Да\",\"correct\":false");
        await catalog.ImportAsync(changed);

        var old = await Dictionaries.GetRecordAsync<UnitRevision>(first);
        Assert.AreEqual("Текст без ключа", old!.Body);
        var prompts = await Dictionaries.GetRecordsAsync<CheckPrompt>($"UnitRevision = '{first}'");
        var options = await Dictionaries.GetRecordsAsync<CheckOption>($"Prompt = '{prompts.First().MetaId}'");
        Assert.IsTrue(options.First(option => option.Name == "yes").IsCorrect, "первая версия хранит свой верный вариант");
        card = (await Dictionaries.GetRecordsAsync<Unit>("StableId = 'learn.pkg.sample.check'")).First();
        Assert.IsTrue(card.PublishedRevision != first, "другой текст даёт новую версию");
    }

    [IntegrationTest("Открытие страницы пишет, где остановились, и не отдаёт ключ")]
    public async Task OpenPageRecordsPlaceWithoutKey()
    {
        var catalog = GetService<ILearningCatalog>();
        var progress = GetService<ILearningProgress>();
        const string package =
            "{\"modules\":[{\"stableId\":\"learn.pkg.open\",\"name\":\"Образец\",\"minutes\":5," +
            "\"articlePath\":\"\",\"articleRevision\":\"\",\"units\":[{\"stableId\":\"learn.pkg.open.page\"," +
            "\"name\":\"Страница\",\"kind\":\"check\",\"sortOrder\":1,\"body\":\"Текст страницы\"," +
            "\"checks\":[{\"id\":\"q1\",\"text\":\"Вопрос\",\"options\":[" +
            "{\"id\":\"no\",\"text\":\"Нет\",\"correct\":false},{\"id\":\"yes\",\"text\":\"Да\",\"correct\":true}]}]}]}]}";
        await catalog.ImportAsync(package);

        var email = "open-" + Guid.NewGuid().ToString("N").Substring(0, 12) + "@example.com";
        var revision = await progress.OpenByEmailAsync(email, "learn.pkg.open.page");
        Assert.IsTrue(revision != Guid.Empty, "страница опубликована");

        var learner = (await Dictionaries.GetRecordsAsync<Learner>($"Email = '{email}'")).First();
        var slice = await GetService<IInformationRegisterService>().SliceLastAsync(
            "LearningEvent",
            DateTime.UtcNow.AddMinutes(1),
            new Dictionary<string, object?>
            {
                ["Learner"] = learner.MetaId,
                ["UnitRevision"] = revision,
            });
        Assert.IsTrue(slice.Count > 0, "в истории есть открытие");

        var payload = await catalog.ReadPageAsync("learn.pkg.open.page");
        Assert.IsTrue(payload.Contains("Текст страницы"), "текст страницы на месте");
        Assert.IsTrue(payload.Contains("\"Да\""), "варианты видны");
        Assert.IsTrue(payload.IndexOf("correct", StringComparison.Ordinal) < 0, "ключ не уходит читателю");
    }

    [IntegrationTest("Открытая страница видна после повторного входа и записывает на путь")]
    public async Task OpenedPageSurvivesRelogin()
    {
        var catalog = GetService<ILearningCatalog>();
        var progress = GetService<ILearningProgress>();
        const string package =
            "{\"tracks\":[{\"stableId\":\"learn.pkg.keep.track\",\"name\":\"Путь\",\"modules\":[\"learn.pkg.keep\"]}]," +
            "\"modules\":[{\"stableId\":\"learn.pkg.keep\",\"name\":\"Модуль\",\"minutes\":5," +
            "\"articlePath\":\"\",\"articleRevision\":\"\",\"units\":[{\"stableId\":\"learn.pkg.keep.page\"," +
            "\"name\":\"Страница\",\"kind\":\"lesson\",\"sortOrder\":1,\"body\":\"Текст\",\"checks\":[]}]}]}";
        await catalog.ImportAsync(package);

        var email = "keep-" + Guid.NewGuid().ToString("N").Substring(0, 12) + "@example.com";
        await progress.OpenByEmailAsync(email, "learn.pkg.keep.page");
        var first = await progress.PlaceAsync(email);
        var again = await progress.PlaceAsync(email);
        Assert.AreEqual(first, again);
        Assert.IsTrue(again.Contains("learn.pkg.keep.page"), "повторный вход видит открытую страницу");

        var learner = (await Dictionaries.GetRecordsAsync<Learner>($"Email = '{email}'")).First();
        var track = (await Dictionaries.GetRecordsAsync<Track>("StableId = 'learn.pkg.keep.track'")).First();
        var enrolled = await GetService<IInformationRegisterService>().SliceLastAsync(
            "Enrollment",
            DateTime.UtcNow.AddMinutes(1),
            new Dictionary<string, object?>
            {
                ["Learner"] = learner.MetaId,
                ["Track"] = track.MetaId,
            });
        Assert.IsTrue(enrolled.Count > 0, "открытие записывает на путь");
    }
}
