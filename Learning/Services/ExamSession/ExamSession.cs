#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;

// Новая публикация добавляет версию и не трогает вопросы старой.
// Попытка копирует текст вопроса в строку, поэтому правка вопроса
// и следующая версия историю не переписывают.
public partial class ExamSession
{
    private static readonly Guid CertificateType = Guid.Parse("178bf8fd-4dac-48c8-b27e-775b5d2d36a4");
    private static readonly Guid AttemptType = Guid.Parse("0a339c7f-6a7c-4ea1-a279-28dc8e67c3f9");

    private readonly IDictionaryManager _dictionaries;
    private readonly IDocumentManager _documents;
    private readonly IDocumentPostingService _posting;

    public ExamSession(
        IDictionaryManager dictionaries,
        IDocumentManager documents,
        IDocumentPostingService posting)
    {
        _dictionaries = dictionaries;
        _documents = documents;
        _posting = posting;
    }

    /// <summary>Publish an exam revision with one question and two options. Returns the revision id.</summary>
    public async Task<Guid> PublishAsync(
        Guid examId, int passPercent, int validDays, string question, string correctOption, string wrongOption)
    {
        var exam = await _dictionaries.GetRecordAsync<Exam>(examId);
        if (exam == null) return Guid.Empty;

        if (exam.PublishedRevision != Guid.Empty
            && await SamePaperAsync(exam.PublishedRevision, passPercent, validDays, question, correctOption, wrongOption))
            return exam.PublishedRevision;

        var number = 1;
        if (exam.PublishedRevision != Guid.Empty)
        {
            var previous = await _dictionaries.GetRecordAsync<ExamRevision>(exam.PublishedRevision);
            if (previous != null) number = previous.Number + 1;
        }

        var revision = _dictionaries.NewRecord<ExamRevision>();
        revision.Exam = examId;
        revision.Number = number;
        revision.PassPercent = passPercent;
        revision.ValidDays = validDays;
        revision.PublishedAt = DateTime.UtcNow;
        revision = await _dictionaries.SaveRecordAsync(revision);

        var row = _dictionaries.NewRecord<ExamQuestion>();
        row.ExamRevision = revision.MetaId;
        row.SortOrder = 1;
        row.Name = Clip(question);
        row.Text = question ?? "";
        row = await _dictionaries.SaveRecordAsync(row);
        await SaveOptionAsync(row.MetaId, 1, correctOption, true);
        await SaveOptionAsync(row.MetaId, 2, wrongOption, false);

        exam.PublishedRevision = revision.MetaId;
        await _dictionaries.SaveRecordAsync(exam);
        return revision.MetaId;
    }

    /// <summary>Open an attempt on the published revision. The line stores the question text.</summary>
    public async Task<Guid> StartAttemptAsync(Guid learnerId, Guid examId)
    {
        var exam = await _dictionaries.GetRecordAsync<Exam>(examId);
        if (exam == null || exam.PublishedRevision == Guid.Empty) return Guid.Empty;

        var questions = (await _dictionaries.GetRecordsAsync<ExamQuestion>(
                $"ExamRevision = '{exam.PublishedRevision}'"))
            .OrderBy(q => q.SortOrder)
            .ToList();

        var attempt = await _documents.NewDocumentAsync<ExamAttempt>();
        attempt.Learner = learnerId;
        attempt.ExamRevision = exam.PublishedRevision;
        foreach (var question in questions)
        {
            attempt.Lines.Add(new ExamAttemptLinesTablePartRow
            {
                Question = question.MetaId,
                SortOrder = question.SortOrder,
                PromptText = question.Text ?? "",
            });
        }

        await _documents.SaveDocumentAsync(attempt);
        return attempt.MetaId;
    }

    /// <summary>
    /// The current question of an in-progress attempt. Starts one when there is none.
    /// Options are labels only: the correct flag is not in the text.
    /// </summary>
    public async Task<string> AskAsync(string email, string examName)
    {
        var learner = await LearnerAsync(email);
        var exam = await ExamByNameAsync(examName);
        if (learner == null || exam == null || exam.PublishedRevision == Guid.Empty)
            return "{\"done\":false,\"error\":\"Экзамен не опубликован\"}";

        var attempt = await OpenAttemptAsync(learner.MetaId, exam);
        if (attempt == null) return "{\"done\":false,\"error\":\"Попытка не открылась\"}";
        var line = NextLine(attempt);
        if (line == null) return await FinishAsync(attempt);
        return await QuestionJsonAsync(attempt, line);
    }

    /// <summary>
    /// Record the chosen option on the current question and return the next one,
    /// or the result when the paper is complete. The result does not say which option was correct.
    /// </summary>
    public async Task<string> ReplyAsync(string email, string attemptId, string optionId)
    {
        if (!Guid.TryParse(attemptId, out var id))
            return "{\"done\":false,\"error\":\"Нет попытки\"}";
        var attempt = await _documents.GetDocumentAsync<ExamAttempt>(id);
        var learner = await LearnerAsync(email);
        if (attempt == null || learner == null || attempt.Learner != learner.MetaId)
            return "{\"done\":false,\"error\":\"Нет попытки\"}";
        if (attempt.Subtype != ExamAttempt.Subtypes.InProgress)
            return await ResultJsonAsync(attempt, attempt.Subtype == ExamAttempt.Subtypes.Passed, attempt.Score);

        var line = NextLine(attempt);
        if (line == null) return await FinishAsync(attempt);

        var options = (await _dictionaries.GetRecordsAsync<ExamOption>($"Question = '{line.Question}'")).ToList();
        var picked = options.FirstOrDefault(option => option.SortOrder.ToString() == (optionId ?? ""));
        if (picked == null) return await QuestionJsonAsync(attempt, line);

        line.ChosenText = picked.Text ?? "";
        line.ChosenIsCorrect = picked.IsCorrect;
        await _documents.SaveDocumentAsync(attempt);

        attempt = await _documents.GetDocumentAsync<ExamAttempt>(id);
        var next = NextLine(attempt!);
        if (next != null) return await QuestionJsonAsync(attempt!, next);
        return await FinishAsync(attempt!);
    }

    private async Task<ExamAttempt?> OpenAttemptAsync(Guid learnerId, Exam exam)
    {
        var rows = await _documents.QueryDocumentsAsync<ExamAttempt>($"Learner = '{learnerId}'");
        var open = rows.FirstOrDefault(row =>
            row.ExamRevision == exam.PublishedRevision && row.Subtype == ExamAttempt.Subtypes.InProgress);
        if (open != null) return await _documents.GetDocumentAsync<ExamAttempt>(open.MetaId);
        var id = await StartAttemptAsync(learnerId, exam.MetaId);
        return id == Guid.Empty ? null : await _documents.GetDocumentAsync<ExamAttempt>(id);
    }

    private async Task<string> FinishAsync(ExamAttempt attempt)
    {
        var lines = attempt.Lines;
        var correct = lines.Count(line => line.ChosenIsCorrect == true);
        var score = lines.Count == 0 ? 0m : Math.Round(100m * correct / lines.Count, 0);
        attempt.Score = score;
        await _documents.SaveDocumentAsync(attempt);

        var revision = await _dictionaries.GetRecordAsync<ExamRevision>(attempt.ExamRevision);
        var passed = score >= (revision?.PassPercent ?? 100);
        var subtype = passed ? ExamAttempt.Subtypes.Passed : ExamAttempt.Subtypes.Failed;
        await _posting.SetSubtypeAsync(AttemptType, attempt.MetaId, subtype);
        return await ResultJsonAsync(attempt, passed, score);
    }

    private async Task<string> QuestionJsonAsync(ExamAttempt attempt, ExamAttemptLinesTablePartRow line)
    {
        var options = (await _dictionaries.GetRecordsAsync<ExamOption>($"Question = '{line.Question}'"))
            .OrderBy(option => option.SortOrder)
            .Select(option => "{\"id\":" + Quote(option.SortOrder.ToString()) + ",\"text\":" + Quote(option.Text) + "}");
        return "{\"done\":false,\"attempt\":" + Quote(attempt.MetaId.ToString())
            + ",\"prompt\":" + Quote(line.PromptText)
            + ",\"options\":[" + string.Join(",", options) + "]}";
    }

    private async Task<string> ResultJsonAsync(ExamAttempt attempt, bool passed, decimal score)
    {
        var number = "";
        if (passed)
        {
            var issued = await _documents.QueryDocumentsAsync<Certificate>($"ExamAttempt = '{attempt.MetaId}'");
            number = issued.FirstOrDefault()?.ID ?? "";
        }
        var text = "{\"done\":true,\"passed\":" + (passed ? "true" : "false") + ",\"score\":" + score.ToString("0");
        if (number.Length > 0) text += ",\"number\":" + Quote(number);
        return text + "}";
    }

    private static ExamAttemptLinesTablePartRow? NextLine(ExamAttempt attempt)
        => attempt.Lines
            .OrderBy(line => line.SortOrder ?? 0)
            .FirstOrDefault(line => string.IsNullOrEmpty(line.ChosenText));

    private async Task<Learner?> LearnerAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var learner = (await _dictionaries.GetRecordsAsync<Learner>($"Email = '{Esc(email)}'")).FirstOrDefault();
        if (learner != null) return learner;
        learner = _dictionaries.NewRecord<Learner>();
        learner.Email = email;
        learner.Name = email;
        return await _dictionaries.SaveRecordAsync(learner);
    }

    private async Task<Exam?> ExamByNameAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return (await _dictionaries.GetRecordsAsync<Exam>($"Name = '{Esc(name)}'")).FirstOrDefault();
    }

    private static string Quote(string? value)
        => "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Esc(string value) => (value ?? "").Replace("'", "''");

    /// <summary>Create the certificate for a passed attempt. A second call keeps the first certificate.</summary>
    public async Task IssueCertificateAsync(Guid attemptId)
    {
        var attempt = await _documents.GetDocumentAsync<ExamAttempt>(attemptId);
        if (attempt == null || attempt.Subtype != ExamAttempt.Subtypes.Passed) return;

        var existing = await _documents.CountDocumentsAsync<Certificate>($"ExamAttempt = '{attemptId}'");
        if (existing > 0) return;

        var revision = await _dictionaries.GetRecordAsync<ExamRevision>(attempt.ExamRevision);
        var days = revision?.ValidDays ?? 0;
        if (days <= 0)
            days = GlobalConstants.Get<int?>("DefaultCertificateDays") ?? 365;

        var certificate = await _documents.NewDocumentAsync<Certificate>();
        certificate.Learner = attempt.Learner;
        certificate.ExamRevision = attempt.ExamRevision;
        certificate.ExamAttempt = attempt.MetaId;
        certificate.ExpiresAt = DateTime.UtcNow.Date.AddDays(days);
        await _documents.SaveDocumentAsync(certificate);
    }

    /// <summary>Move issued certificates whose date has passed into Expired. Returns how many moved.</summary>
    public async Task<int> ExpireDueAsync(DateTime today)
    {
        var issued = await _documents.QueryDocumentsAsync<Certificate>(
            $"Subtype = '{Certificate.Subtypes.Issued}'");
        var moved = 0;
        foreach (var certificate in issued)
        {
            if (certificate.ExpiresAt.Year < 1902 || certificate.ExpiresAt.Date > today.Date)
                continue;
            await _posting.SetSubtypeAsync(CertificateType, certificate.MetaId, Certificate.Subtypes.Expired);
            moved++;
        }
        return moved;
    }

    private async Task<bool> SamePaperAsync(
        Guid revisionId, int passPercent, int validDays, string question, string correctOption, string wrongOption)
    {
        var revision = await _dictionaries.GetRecordAsync<ExamRevision>(revisionId);
        if (revision == null || revision.PassPercent != passPercent || revision.ValidDays != validDays)
            return false;

        var questions = (await _dictionaries.GetRecordsAsync<ExamQuestion>($"ExamRevision = '{revisionId}'")).ToList();
        if (questions.Count != 1 || questions[0].Text != question) return false;

        var options = (await _dictionaries.GetRecordsAsync<ExamOption>($"Question = '{questions[0].MetaId}'"))
            .OrderBy(o => o.SortOrder)
            .ToList();
        return options.Count == 2
            && options[0].IsCorrect
            && options[0].Text == correctOption
            && !options[1].IsCorrect
            && options[1].Text == wrongOption;
    }

    private async Task SaveOptionAsync(Guid questionId, int order, string text, bool correct)
    {
        var option = _dictionaries.NewRecord<ExamOption>();
        option.Question = questionId;
        option.SortOrder = order;
        option.Name = Clip(text);
        option.Text = text ?? "";
        option.IsCorrect = correct;
        await _dictionaries.SaveRecordAsync(option);
    }

    private static string Clip(string? text)
    {
        var value = text ?? "";
        return value.Length <= 200 ? value : value.Substring(0, 200);
    }
}
