using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime;
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

    [IntegrationTest("Значок модуля только после верных ответов, сертификат не создаётся")]
    public async Task CheckAwardsBadgeOnlyWhenEveryAnswerIsRight()
    {
        var catalog = GetService<ILearningCatalog>();
        var check = GetService<IKnowledgeCheck>();
        const string package =
            "{\"tracks\":[{\"stableId\":\"learn.pkg.check.track\",\"name\":\"Путь\",\"modules\":[\"learn.pkg.check\"]}]," +
            "\"modules\":[{\"stableId\":\"learn.pkg.check\",\"name\":\"Модуль\",\"minutes\":5," +
            "\"articlePath\":\"\",\"articleRevision\":\"\",\"units\":[{\"stableId\":\"learn.pkg.check.page\"," +
            "\"name\":\"Проверка\",\"kind\":\"check\",\"sortOrder\":1,\"body\":\"Лист\"," +
            "\"checks\":[" +
            "{\"id\":\"q1\",\"text\":\"Первый\",\"options\":[{\"id\":\"no\",\"text\":\"Нет\",\"correct\":false},{\"id\":\"yes\",\"text\":\"Да\",\"correct\":true}]}," +
            "{\"id\":\"q2\",\"text\":\"Второй\",\"options\":[{\"id\":\"no\",\"text\":\"Нет\",\"correct\":true},{\"id\":\"yes\",\"text\":\"Да\",\"correct\":false}]}" +
            "]}]}]}";
        await catalog.ImportAsync(package);

        var email = "check-" + Guid.NewGuid().ToString("N").Substring(0, 12) + "@example.com";
        var wrong = await check.SubmitAsync(email, "learn.pkg.check.page",
            "[{\"prompt\":\"q1\",\"option\":\"no\"},{\"prompt\":\"q2\",\"option\":\"no\"}]");
        Assert.IsTrue(wrong.Contains("\"passed\":false"), "неверный лист не сдан");
        Assert.IsTrue(wrong.IndexOf("\"key\"", StringComparison.Ordinal) < 0, "ключ не показывается до сдачи");

        var learner = (await Dictionaries.GetRecordsAsync<Learner>($"Email = '{email}'")).First();
        var module = (await Dictionaries.GetRecordsAsync<Module>("StableId = 'learn.pkg.check'")).First();
        var info = GetService<IInformationRegisterService>();
        var before = await info.SliceLastAsync(
            "ModuleAward",
            DateTime.UtcNow.AddMinutes(1),
            new Dictionary<string, object?>
            {
                ["Learner"] = learner.MetaId,
                ["ModuleRevision"] = module.PublishedRevision,
            });
        Assert.AreEqual(0, before.Count);

        var right = await check.SubmitAsync(email, "learn.pkg.check.page",
            "[{\"prompt\":\"q1\",\"option\":\"yes\"},{\"prompt\":\"q2\",\"option\":\"no\"}]");
        Assert.IsTrue(right.Contains("\"badge\":true"), "верный лист даёт значок");
        Assert.IsTrue(right.Contains("\"option\":\"yes\""), "после сдачи виден верный вариант");
        var after = await info.SliceLastAsync(
            "ModuleAward",
            DateTime.UtcNow.AddMinutes(1),
            new Dictionary<string, object?>
            {
                ["Learner"] = learner.MetaId,
                ["ModuleRevision"] = module.PublishedRevision,
            });
        Assert.IsTrue(after.Count > 0, "значок записан");

        var track = (await Dictionaries.GetRecordsAsync<Track>("StableId = 'learn.pkg.check.track'")).First();
        var trophy = await info.SliceLastAsync(
            "TrackAward",
            DateTime.UtcNow.AddMinutes(1),
            new Dictionary<string, object?>
            {
                ["Learner"] = learner.MetaId,
                ["TrackRevision"] = track.PublishedRevision,
            });
        Assert.IsTrue(trophy.Count > 0, "последний модуль пути закрывает путь");

        var certificates = await Documents.QueryDocumentsAsync<Certificate>($"Learner = '{learner.MetaId}'");
        Assert.AreEqual(0, certificates.Count);
    }

    [IntegrationTest("Несданный экзамен не выдаёт сертификат и не показывает верный вариант")]
    public async Task FailedExamIssuesNoCertificateAndHidesTheKey()
    {
        var session = GetService<IExamSession>();
        var name = "Sum-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var exam = Dictionaries.NewRecord<Exam>();
        exam.Name = name;
        exam = await Dictionaries.SaveRecordAsync(exam);
        await session.PublishAsync(exam.MetaId, 100, 365, "Первый", "да", "нет");
        exam = (await Dictionaries.GetRecordAsync<Exam>(exam.MetaId))!;

        var extra = Dictionaries.NewRecord<ExamQuestion>();
        extra.ExamRevision = exam.PublishedRevision;
        extra.SortOrder = 2;
        extra.Name = "Второй";
        extra.Text = "Второй";
        extra = await Dictionaries.SaveRecordAsync(extra);
        var yes = Dictionaries.NewRecord<ExamOption>();
        yes.Question = extra.MetaId;
        yes.SortOrder = 1;
        yes.Name = "да2";
        yes.Text = "да";
        yes.IsCorrect = true;
        await Dictionaries.SaveRecordAsync(yes);
        var no = Dictionaries.NewRecord<ExamOption>();
        no.Question = extra.MetaId;
        no.SortOrder = 2;
        no.Name = "нет2";
        no.Text = "нет";
        no.IsCorrect = false;
        await Dictionaries.SaveRecordAsync(no);

        var email = "exam-" + Guid.NewGuid().ToString("N").Substring(0, 12) + "@example.com";
        var first = await session.AskAsync(email, name);
        Assert.IsTrue(first.Contains("Первый"), "сначала виден первый вопрос");
        Assert.IsTrue(first.IndexOf("Второй", StringComparison.Ordinal) < 0, "второй вопрос не показывается сразу");
        Assert.IsTrue(first.IndexOf("correct", StringComparison.OrdinalIgnoreCase) < 0, "верный вариант не подписан");

        var marker = "\"attempt\":\"";
        var at = first.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var attemptId = first.Substring(at, first.IndexOf('"', at) - at);

        var mid = await session.ReplyAsync(email, attemptId, "2");
        Assert.IsTrue(mid.Contains("Второй"), "после ответа открывается следующий вопрос");
        Assert.IsTrue(mid.Contains("\"done\":false"), "попытка ещё идёт");

        var fail = await session.ReplyAsync(email, attemptId, "2");
        Assert.IsTrue(fail.Contains("\"passed\":false"), "ошибка не сдаёт экзамен");
        Assert.IsTrue(fail.IndexOf("correct", StringComparison.OrdinalIgnoreCase) < 0, "итог не называет верный вариант");
        Assert.IsTrue(fail.IndexOf("\"options\"", StringComparison.Ordinal) < 0, "итог не повторяет варианты");

        var learner = (await Dictionaries.GetRecordsAsync<Learner>($"Email = '{email}'")).First();
        var none = await Documents.QueryDocumentsAsync<Certificate>($"Learner = '{learner.MetaId}'");
        Assert.AreEqual(0, none.Count);

        var again = await session.AskAsync(email, name);
        Assert.IsTrue(again.Contains("Первый"), "новая попытка начинается с первого вопроса");
        at = again.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        attemptId = again.Substring(at, again.IndexOf('"', at) - at);
        await session.ReplyAsync(email, attemptId, "1");
        var pass = await session.ReplyAsync(email, attemptId, "1");
        Assert.IsTrue(pass.Contains("\"passed\":true"), "оба верных ответа сдают экзамен");
        var issued = await Documents.QueryDocumentsAsync<Certificate>($"Learner = '{learner.MetaId}'");
        Assert.AreEqual(1, issued.Count);
    }

    [IntegrationTest("Сертификат получает номер, просроченный переводит задание")]
    public async Task CertificateGetsNumberAndJobExpiresIt()
    {
        var dueAttempt = await PassOneQuestionAsync("CertDue", 0);
        var due = (await Documents.QueryDocumentsAsync<Certificate>($"ExamAttempt = '{dueAttempt}'")).Single();
        Assert.AreEqual(Certificate.Subtypes.Issued, due.Subtype);
        Assert.IsTrue(!string.IsNullOrWhiteSpace(due.ID), "серия выдала номер");
        Assert.AreEqual(DateTime.UtcNow.Date.AddDays(365), due.ExpiresAt.Date);

        var keptAttempt = await PassOneQuestionAsync("CertKept", 30);
        var kept = (await Documents.QueryDocumentsAsync<Certificate>($"ExamAttempt = '{keptAttempt}'")).Single();
        Assert.AreEqual(DateTime.UtcNow.Date.AddDays(30), kept.ExpiresAt.Date);
        Assert.IsTrue(kept.ID != due.ID, "у двух сертификатов разные номера");

        due.ExpiresAt = DateTime.UtcNow.Date.AddDays(-1);
        await Documents.SaveDocumentAsync(due);

        var run = await Db.RunJobAsync(Guid.Parse("5007089d-55b7-4417-a283-5ddb315d1f57"));
        Assert.IsTrue(run.Success, run.Output);

        var expired = await Documents.GetDocumentAsync<Certificate>(due.MetaId);
        Assert.AreEqual(Certificate.Subtypes.Expired, expired!.Subtype);
        Assert.AreEqual(due.ID, expired.ID);
        Assert.AreEqual(due.Learner, expired.Learner);
        Assert.AreEqual(dueAttempt, expired.ExamAttempt);

        var still = await Documents.GetDocumentAsync<Certificate>(kept.MetaId);
        Assert.AreEqual(Certificate.Subtypes.Issued, still!.Subtype);
    }

    [IntegrationTest("Участник не видит чужой прогресс, владелец видит свой стенд")]
    public async Task MemberSeesNoForeignProgressOwnerSeesTheStand()
    {
        var suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        var owner = await LearnerAsync("owner-" + suffix + "@example.com");
        var member = await LearnerAsync("member-" + suffix + "@example.com");
        var outsider = await LearnerAsync("outsider-" + suffix + "@example.com");

        var org = Dictionaries.NewRecord<Organization>();
        org.Name = "Стенд " + suffix;
        org.StandSlug = "stand-" + suffix;
        org = await Dictionaries.SaveRecordAsync(org);
        var other = Dictionaries.NewRecord<Organization>();
        other.Name = "Чужой " + suffix;
        other.StandSlug = "other-" + suffix;
        other = await Dictionaries.SaveRecordAsync(other);

        var links = GetService<ILinkTableManager>();
        await links.AddAsync("Membership", new Dictionary<string, object?>
        {
            ["Learner"] = owner.MetaId,
            ["Organization"] = org.MetaId,
            ["Role"] = "Owner",
        });
        await links.AddAsync("Membership", new Dictionary<string, object?>
        {
            ["Learner"] = member.MetaId,
            ["Organization"] = org.MetaId,
            ["Role"] = "Member",
        });
        await links.AddAsync("Membership", new Dictionary<string, object?>
        {
            ["Learner"] = outsider.MetaId,
            ["Organization"] = other.MetaId,
            ["Role"] = "Owner",
        });

        var trackId = "learn.rep." + suffix;
        var moduleId = trackId + ".mod";
        await GetService<ILearningCatalog>().ImportAsync(
            "{\"tracks\":[{\"stableId\":\"" + trackId + "\",\"name\":\"Путь отчёта\",\"modules\":[\"" + moduleId + "\"]}]," +
            "\"modules\":[{\"stableId\":\"" + moduleId + "\",\"name\":\"Модуль отчёта\",\"minutes\":5," +
            "\"articlePath\":\"\",\"articleRevision\":\"\",\"units\":[{\"stableId\":\"" + moduleId + ".page\"," +
            "\"name\":\"Страница\",\"kind\":\"lesson\",\"sortOrder\":1,\"body\":\"Текст\"}]}]}");
        var track = (await Dictionaries.GetRecordsAsync<Track>($"StableId = '{trackId}'")).First();
        var module = (await Dictionaries.GetRecordsAsync<Module>($"StableId = '{moduleId}'")).First();
        var info = GetService<IInformationRegisterService>();
        await info.SetAsync(
            "ModuleAward",
            DateTime.UtcNow,
            new Dictionary<string, object?> { ["Learner"] = member.MetaId, ["ModuleRevision"] = module.PublishedRevision },
            new Dictionary<string, object?> { ["Awarded"] = true });
        await info.SetAsync(
            "TrackAward",
            DateTime.UtcNow,
            new Dictionary<string, object?> { ["Learner"] = member.MetaId, ["TrackRevision"] = track.PublishedRevision },
            new Dictionary<string, object?> { ["Awarded"] = true });
        await info.SetAsync(
            "ModuleAward",
            DateTime.UtcNow,
            new Dictionary<string, object?> { ["Learner"] = outsider.MetaId, ["ModuleRevision"] = module.PublishedRevision },
            new Dictionary<string, object?> { ["Awarded"] = true });

        var number = await PassForAsync(member.Email);
        var report = GetService<ILearningReport>();
        var owners = await report.ForStandAsync(owner.Email, org.StandSlug);
        Assert.IsTrue(owners.Contains(member.Email), "владелец видит участника");
        Assert.IsTrue(owners.Contains(owner.Email), "владелец видит себя");
        Assert.IsTrue(owners.Contains("Модуль отчёта"), "владелец видит закрытый модуль");
        Assert.IsTrue(owners.Contains("Путь отчёта"), "владелец видит закрытый путь");
        Assert.IsTrue(owners.Contains(number), "владелец видит номер сертификата");
        Assert.IsTrue(owners.IndexOf(outsider.Email, StringComparison.Ordinal) < 0, "чужой стенд не попадает в отчёт");
        Assert.IsTrue(owners.IndexOf("Два плюс два", StringComparison.Ordinal) < 0, "текст экзамена в отчёт не входит");

        var members = await report.ForStandAsync(member.Email, org.StandSlug);
        Assert.AreEqual("{\"error\":\"Нет доступа\"}", members);
        Assert.IsTrue(members.IndexOf(owner.Email, StringComparison.Ordinal) < 0, "участник не видит владельца");
        Assert.IsTrue(members.IndexOf(outsider.Email, StringComparison.Ordinal) < 0, "участник не видит чужой стенд");

        var strangers = await report.ForStandAsync("nobody-" + suffix + "@example.com", org.StandSlug);
        Assert.AreEqual("{\"error\":\"Нет доступа\"}", strangers);
    }

    [IntegrationTest("Владелец назначает путь, участник видит его, письмо не нужно")]
    public async Task OwnerAssignsAPathAndTheMemberSeesIt()
    {
        var suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        var owner = await LearnerAsync("owner-as-" + suffix + "@example.com");
        var member = await LearnerAsync("member-as-" + suffix + "@example.com");
        var org = Dictionaries.NewRecord<Organization>();
        org.Name = "Назначение " + suffix;
        org.StandSlug = "assign-" + suffix;
        org = await Dictionaries.SaveRecordAsync(org);
        var links = GetService<ILinkTableManager>();
        await links.AddAsync("Membership", new Dictionary<string, object?>
        {
            ["Learner"] = owner.MetaId, ["Organization"] = org.MetaId, ["Role"] = "Owner",
        });
        await links.AddAsync("Membership", new Dictionary<string, object?>
        {
            ["Learner"] = member.MetaId, ["Organization"] = org.MetaId, ["Role"] = "Member",
        });

        var trackId = "learn.assign." + suffix;
        await GetService<ILearningCatalog>().ImportAsync(
            "{\"tracks\":[{\"stableId\":\"" + trackId + "\",\"name\":\"Назначенный путь\",\"modules\":[\"learn.assign." + suffix + ".mod\"]}]," +
            "\"modules\":[{\"stableId\":\"learn.assign." + suffix + ".mod\",\"name\":\"Модуль\",\"minutes\":5," +
            "\"articlePath\":\"\",\"articleRevision\":\"\",\"units\":[{\"stableId\":\"learn.assign." + suffix + ".page\"," +
            "\"name\":\"Страница\",\"kind\":\"lesson\",\"sortOrder\":1,\"body\":\"Текст\",\"checks\":[]}]}]}");

        var report = GetService<ILearningReport>();
        var body = "{\"learner\":\"" + member.Email + "\",\"track\":\"" + trackId + "\",\"due\":\"2026-10-01\"}";
        var refused = await report.AssignAsync(member.Email, org.StandSlug, body);
        Assert.AreEqual("{\"error\":\"Нет доступа\"}", refused);

        var before = await report.ForStandAsync(owner.Email, org.StandSlug);
        Assert.IsTrue(before.Contains("{\"id\":\"" + trackId + "\",\"name\":\"Назначенный путь\"}"),
            "владелец выбирает путь из опубликованных, а не пишет код руками");

        var assigned = await report.AssignAsync(owner.Email, org.StandSlug, body);
        Assert.AreEqual("{\"ok\":true}", assigned);

        var place = await GetService<ILearningProgress>().PlaceAsync(member.Email);
        Assert.IsTrue(place.Contains(trackId), "участник видит назначенный путь");
        Assert.IsTrue(place.IndexOf("learn.assign." + suffix + ".page", StringComparison.Ordinal) < 0,
            "назначение само по себе страницу не открывает");

        var owners = await report.ForStandAsync(owner.Email, org.StandSlug);
        Assert.IsTrue(owners.Contains("\"open\":[\"Назначенный путь\"]"), "владелец видит, кто не закончил");
        Assert.IsTrue(owners.IndexOf("\"tracks\":[\"Назначенный путь\"]", StringComparison.Ordinal) < 0,
            "незакрытый путь не числится сданным");
    }

    [IntegrationTest("Группу уроков, урок и тест наполняют с карточки")]
    public async Task AuthorFillsAGroupALessonAndATest()
    {
        var suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        var group = Dictionaries.NewRecord<Track>();
        group.StableId = "learn.fill." + suffix;
        group.Name = "Группа наполнения";
        group = await Dictionaries.SaveRecordAsync(group);

        var course = Dictionaries.NewRecord<Module>();
        course.StableId = "learn.fill." + suffix + ".course";
        course.Name = "Курс наполнения";
        course.Minutes = 20;
        course.Audience = "Пользователь";
        course.Level = "Начальный";
        course = await Dictionaries.SaveRecordAsync(course);

        await GetService<ILinkTableManager>().AddAsync("TrackCourse", new Dictionary<string, object?>
        {
            ["Track"] = group.MetaId,
            ["Module"] = course.MetaId,
            ["SortOrder"] = 1,
        });
        group = await Dictionaries.SaveRecordAsync(group);

        var lesson = Dictionaries.NewRecord<Unit>();
        lesson.StableId = "learn.fill." + suffix + ".lesson";
        lesson.Name = "Урок наполнения";
        lesson.Module = course.MetaId;
        lesson.Kind = UnitKind.Lesson;
        lesson.SortOrder = 1;
        lesson.Body = "Текст урока с карточки";
        lesson = await Dictionaries.SaveRecordAsync(lesson);

        var test = Dictionaries.NewRecord<Unit>();
        test.StableId = "learn.fill." + suffix + ".test";
        test.Name = "Тест наполнения";
        test.Module = course.MetaId;
        test.Kind = UnitKind.Check;
        test.SortOrder = 2;
        test.Body = "Ответьте на вопрос";
        test = await Dictionaries.SaveRecordAsync(test);

        var prompt = Dictionaries.NewRecord<CheckPrompt>();
        prompt.Unit = test.MetaId;
        prompt.Name = "q1";
        prompt.Text = "Два плюс два";
        prompt.SortOrder = 1;
        prompt = await Dictionaries.SaveRecordAsync(prompt);

        var yes = Dictionaries.NewRecord<CheckOption>();
        yes.Prompt = prompt.MetaId;
        yes.Name = "four";
        yes.Text = "4";
        yes.SortOrder = 1;
        yes.IsCorrect = true;
        await Dictionaries.SaveRecordAsync(yes);
        var no = Dictionaries.NewRecord<CheckOption>();
        no.Prompt = prompt.MetaId;
        no.Name = "five";
        no.Text = "5";
        no.SortOrder = 2;
        no.IsCorrect = false;
        await Dictionaries.SaveRecordAsync(no);

        var catalog = GetService<ILearningCatalog>();
        var page = await catalog.ReadPageAsync(lesson.StableId);
        Assert.IsTrue(page.Contains("Текст урока с карточки"), "текст урока читается с опубликованной версии");
        var paper = await catalog.ReadPageAsync(test.StableId);
        Assert.IsTrue(paper.Contains("Два плюс два"), "вопрос теста на месте");
        Assert.IsTrue(paper.Contains("\"4\""), "вариант виден");
        Assert.IsTrue(paper.IndexOf("correct", StringComparison.Ordinal) < 0, "ключ с карточки не уходит в чтение");

        group = (await Dictionaries.GetRecordAsync<Track>(group.MetaId))!;
        Assert.IsTrue(group.PublishedRevision != Guid.Empty, "группа опубликована");
        course = (await Dictionaries.GetRecordAsync<Module>(course.MetaId))!;
        Assert.AreEqual("Пользователь", course.Audience);
        test = (await Dictionaries.GetRecordAsync<Unit>(test.MetaId))!;
        var frozen = (await Dictionaries.GetRecordsAsync<CheckPrompt>($"UnitRevision = '{test.PublishedRevision}'")).ToList();
        Assert.IsTrue(frozen.Count >= 1, "у опубликованного теста есть вопрос");
        Assert.IsTrue(frozen.All(row => row.Unit == Guid.Empty), "опубликованный вопрос не дублируется на карточке");
    }

    [IntegrationTest("Пакет каталога ставит карточку урока и публикует её")]
    public async Task CatalogPackagePublishesTheFindLesson()
    {
        var result = await GetService<IDataPackageService>().ApplyAsync("Learning/catalog");
        Assert.IsTrue(result.Ok, string.Join("; ", result.Issues));

        var catalog = GetService<ILearningCatalog>();
        var page = await catalog.ReadPageAsync("learn.getting-started.find");
        Assert.IsTrue(page.Contains("Milk Chocolate 90g"), page);
        var paper = await catalog.ReadPageAsync("learn.getting-started.check");
        Assert.IsTrue(paper.Contains("Кнопкой на карточке"), paper);
        Assert.IsTrue(paper.IndexOf("\"correct\"", StringComparison.Ordinal) < 0, "ключ не в чтении");
    }

    [IntegrationTest("Экзамен наполняют с карточки: вопросы справа, сохранение публикует")]
    public async Task AuthorFillsAnExamFromTheCard()
    {
        var suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        var exam = Dictionaries.NewRecord<Exam>();
        var name = "ExamFill-" + suffix;
        exam.Name = name;
        exam.PassPercent = 70;
        exam.ValidDays = 365;
        exam = await Dictionaries.SaveRecordAsync(exam);

        var prompt = Dictionaries.NewRecord<ExamQuestion>();
        prompt.Exam = exam.MetaId;
        prompt.Text = "Capital of France";
        prompt.SortOrder = 1;
        prompt = await Dictionaries.SaveRecordAsync(prompt);

        var yes = Dictionaries.NewRecord<ExamOption>();
        yes.Question = prompt.MetaId;
        yes.Text = "Paris";
        yes.SortOrder = 1;
        yes.IsCorrect = true;
        await Dictionaries.SaveRecordAsync(yes);
        var no = Dictionaries.NewRecord<ExamOption>();
        no.Question = prompt.MetaId;
        no.Text = "Lyon";
        no.SortOrder = 2;
        no.IsCorrect = false;
        await Dictionaries.SaveRecordAsync(no);

        exam = (await Dictionaries.GetRecordAsync<Exam>(exam.MetaId))!;
        Assert.IsTrue(exam.PublishedRevision != Guid.Empty, "экзамен опубликован с карточки");
        var frozen = (await Dictionaries.GetRecordsAsync<ExamQuestion>($"ExamRevision = '{exam.PublishedRevision}'")).ToList();
        Assert.AreEqual(1, frozen.Count);
        Assert.IsTrue(frozen.All(row => row.Exam == Guid.Empty), "опубликованный вопрос не висит на карточке");

        var email = "exam-fill-" + suffix + "@example.com";
        var session = GetService<IExamSession>();
        var ask = await session.AskAsync(email, exam.Name);
        Assert.IsTrue(ask.Contains("Capital of France"), ask);
        Assert.IsTrue(ask.IndexOf("correct", StringComparison.Ordinal) < 0, "ключ не в выдаче");
        Assert.IsTrue(ask.Contains("Paris"), ask);
    }

    private static async Task<Learner> LearnerAsync(string email)
    {
        var learner = Dictionaries.NewRecord<Learner>();
        learner.Email = email;
        learner.Name = email;
        return await Dictionaries.SaveRecordAsync(learner);
    }

    private static async Task<string> PassForAsync(string email)
    {
        var session = GetService<IExamSession>();
        var exam = Dictionaries.NewRecord<Exam>();
        exam.Name = "Rep-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        exam = await Dictionaries.SaveRecordAsync(exam);
        await session.PublishAsync(exam.MetaId, 100, 30, "Два плюс два", "4", "5");
        var ask = await session.AskAsync(email, exam.Name);
        var marker = "\"attempt\":\"";
        var at = ask.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var attemptId = ask.Substring(at, ask.IndexOf('"', at) - at);
        var result = await session.ReplyAsync(email, attemptId, "1");
        Assert.IsTrue(result.Contains("\"passed\":true"), result);
        var numberMarker = "\"number\":\"";
        var start = result.IndexOf(numberMarker, StringComparison.Ordinal) + numberMarker.Length;
        return result.Substring(start, result.IndexOf('"', start) - start);
    }

    private static async Task<Guid> PassOneQuestionAsync(string name, int validDays)
    {
        var session = GetService<IExamSession>();
        var exam = Dictionaries.NewRecord<Exam>();
        exam.Name = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        exam = await Dictionaries.SaveRecordAsync(exam);
        await session.PublishAsync(exam.MetaId, 100, validDays, "Два плюс два", "4", "5");

        var email = exam.Name + "@example.com";
        var ask = await session.AskAsync(email, exam.Name);
        var marker = "\"attempt\":\"";
        var at = ask.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var attemptId = ask.Substring(at, ask.IndexOf('"', at) - at);
        var result = await session.ReplyAsync(email, attemptId, "1");
        Assert.IsTrue(result.Contains("\"passed\":true"), result);
        Assert.IsTrue(result.Contains("\"number\":"), "сданный экзамен возвращает номер сертификата");
        return Guid.Parse(attemptId);
    }
}
