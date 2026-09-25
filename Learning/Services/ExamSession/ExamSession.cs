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
